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
    private const int MinutesPerImage = 3;
    private const int MaxTaskTimeoutMinutes = 10;
    private const string GenericGenerationFailureMessage = "图片生成服务暂时不可用，请稍后重试。";

    public async Task ProcessAsync(long taskId, CancellationToken cancellationToken)
    {
        var task = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);

        if (task is null || task.Status != 0)
        {
            return;
        }

        var results = DeserializeImageUrls(task.ResultUrls).ToList();
        try
        {
            using var taskTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            taskTimeout.CancelAfter(ResolveTaskTimeout(task.ImageCount));
            var taskToken = taskTimeout.Token;

            var modelCode = AiImageModelConfigService.NormalizeModelCode(task.ModelName);
            if (AiImageModelConfigService.IsNanoBananaModel(modelCode))
            {
                var imageUrls = DeserializeReferenceImageUrls(task.ReferenceImageUrls);
                for (var i = results.Count; i < task.ImageCount; i++)
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
                    await PersistPartialResultsAsync(taskId, results, CancellationToken.None);
                }
            }
            else
            {
                var referenceImageUrls = DeserializeReferenceImageUrls(task.ReferenceImageUrls);
                for (var i = results.Count; i < task.ImageCount; i++)
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
                        referenceImageUrls,
                        task.MaskImageUrl,
                        taskToken);
                    results.Add(response.Url);
                    await PersistPartialResultsAsync(taskId, results, CancellationToken.None);
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
                .Where(x => x.Id == taskId && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(CancellationToken.None);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkFailedAsync(task, new TimeoutException($"AI image generation timed out after {ResolveTaskTimeout(task.ImageCount).TotalMinutes:0} minutes.", ex), results, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkFailedAsync(task, ex, results, cancellationToken);
        }
    }

    public static TimeSpan ResolveTaskTimeout(int imageCount)
    {
        var minutes = Math.Min(Math.Max(imageCount, 1) * MinutesPerImage, MaxTaskTimeoutMinutes);
        return TimeSpan.FromMinutes(minutes);
    }

    private async Task PersistPartialResultsAsync(long taskId, IReadOnlyList<string> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return;
        }

        await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity
            {
                ResultUrls = JsonSerializer.Serialize(results),
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == taskId && !x.IsDeleted && x.Status == 0)
            .ExecuteCommandAsync(cancellationToken);
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

    private static IReadOnlyList<string> DeserializeImageUrls(string? imageUrls)
    {
        if (string.IsNullOrWhiteSpace(imageUrls))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(imageUrls) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task MarkFailedAsync(AiImageTaskEntity task, Exception ex, IReadOnlyList<string> results, CancellationToken cancellationToken)
    {
        var message = SanitizeFailureMessage(ex);
        if (message.Length > 1000)
        {
            message = message[..1000];
        }

        var latestTask = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == task.Id && !x.IsDeleted, cancellationToken);
        if (latestTask is null || latestTask.Status != 0)
        {
            return;
        }

        var mergedResults = MergeImageUrls(DeserializeImageUrls(latestTask.ResultUrls), results);
        var affected = mergedResults.Count > 0
            ? await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = message,
                    ResultUrls = JsonSerializer.Serialize(mergedResults),
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken)
            : await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = message,
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken);
        if (affected == 0)
        {
            return;
        }

        var failedImageCount = Math.Max(0, task.ImageCount - mergedResults.Count);
        if (failedImageCount == 0)
        {
            return;
        }

        var refundPoints = await ResolveTaskCostAsync(task, failedImageCount, cancellationToken);
        await pointService.RefundForImageAsync(task.UserId, task.Id, refundPoints, cancellationToken);
    }

    private static IReadOnlyList<string> MergeImageUrls(params IReadOnlyList<string>[] urlLists)
    {
        return urlLists
            .SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(int.MaxValue)
            .ToArray();
    }

    private static string SanitizeFailureMessage(Exception ex)
    {
        return ex switch
        {
            AppException { Code: ErrorCodes.BadRequest } => GenericGenerationFailureMessage,
            HttpRequestException => GenericGenerationFailureMessage,
            TimeoutException => GenericGenerationFailureMessage,
            TaskCanceledException => GenericGenerationFailureMessage,
            _ => string.IsNullOrWhiteSpace(ex.Message) ? GenericGenerationFailureMessage : ex.Message
        };
    }

    private async Task<int> ResolveTaskCostAsync(AiImageTaskEntity task, int imageCount, CancellationToken cancellationToken)
    {
        if (imageCount <= 0)
        {
            return 0;
        }

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
                    return await pointService.GetImageGenerateCostAsync(modelCode, resolutionCode, string.Empty, imageCount, cancellationToken);
                }
                catch (AppException ex) when (ex.Code == ErrorCodes.BadRequest)
                {
                }
            }

            return 0;
        }

        return await pointService.GetImageGenerateCostAsync(modelCode, task.ResolutionCode, task.QualityCode, imageCount, cancellationToken);
    }
}
