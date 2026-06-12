using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskProcessor(ISqlSugarClient db, IAiImageService aiImageService, INanoBananaImageService nanoBananaImageService, IPointService pointService) : IAiImageTaskProcessor
{
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(5);

    public async Task ProcessAsync(long taskId, CancellationToken cancellationToken)
    {
        var task = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);

        if (task is null || task.Status != 0)
        {
            return;
        }

        try
        {
            using var taskTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            taskTimeout.CancelAfter(TaskTimeout);
            var taskToken = taskTimeout.Token;

            var results = new List<string>(task.ImageCount);
            var modelCode = AiImageModelConfigService.NormalizeModelCode(task.ModelName);
            if (AiImageModelConfigService.IsNanoBananaModel(modelCode))
            {
                var imageUrls = DeserializeReferenceImageUrls(task.ReferenceImageUrls);
                for (var i = 0; i < task.ImageCount; i++)
                {
                    var response = await nanoBananaImageService.GenerateFromTaskAsync(
                        task.Prompt,
                        task.ModelName,
                        task.ResolutionCode,
                        task.AspectRatioCode,
                        task.Size,
                        imageUrls,
                        taskToken);
                    results.Add(response.Url);
                }
            }
            else
            {
                for (var i = 0; i < task.ImageCount; i++)
                {
                    var response = await aiImageService.GenerateFromResolvedAsync(
                        task.Prompt,
                        task.ModelName,
                        new ResolveAiImageParametersResponse
                        {
                            ResolutionCode = task.ResolutionCode,
                            QualityCode = task.QualityCode,
                            AspectRatioCode = task.AspectRatioCode,
                            Width = task.Width,
                            Height = task.Height,
                            Size = task.Size,
                            ProviderQuality = task.Quality
                        },
                        DeserializeReferenceImageUrls(task.ReferenceImageUrls),
                        task.MaskImageUrl,
                        taskToken);
                    results.Add(response.Url);
                }
            }

            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 1,
                    ResultUrls = JsonSerializer.Serialize(results),
                    ErrorMessage = null,
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == taskId && !x.IsDeleted)
                .ExecuteCommandAsync();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkFailedAsync(task, new TimeoutException("AI image generation timed out after 5 minutes.", ex), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkFailedAsync(task, ex, cancellationToken);
        }
    }

    private static IReadOnlyList<string> DeserializeReferenceImageUrls(string? referenceImageUrls)
    {
        if (string.IsNullOrWhiteSpace(referenceImageUrls))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(referenceImageUrls) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task MarkFailedAsync(AiImageTaskEntity task, Exception ex, CancellationToken cancellationToken)
    {
        var message = ex is AppException ? ex.Message : ex.Message;
        if (message.Length > 1000)
        {
            message = message[..1000];
        }

        await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity
            {
                Status = 2,
                ErrorMessage = message,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == task.Id && !x.IsDeleted)
            .ExecuteCommandAsync();

        var refundPoints = await ResolveTaskCostAsync(task, cancellationToken);
        await pointService.RefundForImageAsync(task.UserId, task.Id, refundPoints, cancellationToken);
    }

    private async Task<int> ResolveTaskCostAsync(AiImageTaskEntity task, CancellationToken cancellationToken)
    {
        var modelCode = AiImageModelConfigService.NormalizeModelCode(task.ModelName);
        var isNanoBananaModel = AiImageModelConfigService.IsNanoBananaModel(modelCode);
        if (isNanoBananaModel)
        {
            var candidates = string.Equals(task.Size, task.ResolutionCode, StringComparison.OrdinalIgnoreCase)
                ? [task.Size]
                : new[] { task.Size, task.ResolutionCode };

            foreach (var resolutionCode in candidates)
            {
                try
                {
                    return await pointService.GetImageGenerateCostAsync(modelCode, resolutionCode, string.Empty, task.ImageCount, cancellationToken);
                }
                catch (AppException ex) when (ex.Code == ErrorCodes.BadRequest)
                {
                }
            }

            return 0;
        }

        return await pointService.GetImageGenerateCostAsync(modelCode, task.ResolutionCode, task.QualityCode, task.ImageCount, cancellationToken);
    }
}
