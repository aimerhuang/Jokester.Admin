using System.Runtime.ExceptionServices;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskProcessor(
    ISqlSugarClient db,
    IServiceScopeFactory scopeFactory,
    IPointService pointService,
    ILogger<AiImageTaskProcessor> logger) : IAiImageTaskProcessor
{
    private const int MinutesPerImage = 3;
    private const int MaxTaskTimeoutMinutes = 10;
    private const int MaxImageGenerationConcurrency = 4;
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
                await GenerateRemainingImagesConcurrentlyAsync(
                    taskId,
                    results,
                    task.ImageCount,
                    ct => GenerateNanoBananaImageFromTaskAsync(task, imageUrls, ct),
                    taskToken);
            }
            else
            {
                var referenceImageUrls = DeserializeReferenceImageUrls(task.ReferenceImageUrls);
                await GenerateRemainingImagesConcurrentlyAsync(
                    taskId,
                    results,
                    task.ImageCount,
                    ct => GenerateGptImageFromTaskAsync(task, referenceImageUrls, ct),
                    taskToken);
            }

            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 1,
                    ResultUrls = JsonSerializer.Serialize(results),
                    ErrorMessage = null,
                    UpdatedAt = HongKongNow()
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
                UpdatedAt = HongKongNow()
            })
            .Where(x => x.Id == taskId && !x.IsDeleted && x.Status == 0)
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task GenerateRemainingImagesConcurrentlyAsync(
        long taskId,
        List<string> results,
        int targetImageCount,
        Func<CancellationToken, Task<string>> generateImageAsync,
        CancellationToken cancellationToken)
    {
        var remainingImageCount = targetImageCount - results.Count;
        if (remainingImageCount <= 0)
        {
            return;
        }

        using var generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var semaphore = new SemaphoreSlim(Math.Min(MaxImageGenerationConcurrency, remainingImageCount));
        var runningTasks = Enumerable.Range(0, remainingImageCount)
            .Select(_ => GenerateImageWithConcurrencyAsync(generateImageAsync, semaphore, generationCancellation.Token))
            .ToList();

        Exception? firstException = null;
        while (runningTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(runningTasks);
            runningTasks.Remove(completedTask);

            try
            {
                var url = await completedTask;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    results.Add(url);
                    await PersistPartialResultsAsync(taskId, results, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                firstException ??= ex;
                await generationCancellation.CancelAsync();
            }
        }

        if (firstException is not null)
        {
            ExceptionDispatchInfo.Capture(firstException).Throw();
        }
    }

    private static async Task<string> GenerateImageWithConcurrencyAsync(
        Func<CancellationToken, Task<string>> generateImageAsync,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await generateImageAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<string> GenerateGptImageFromTaskAsync(AiImageTaskEntity task, IReadOnlyList<string> referenceImageUrls, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var aiImageService = scope.ServiceProvider.GetRequiredService<IAiImageService>();
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
            cancellationToken);

        return response.Url;
    }

    private async Task<string> GenerateNanoBananaImageFromTaskAsync(AiImageTaskEntity task, IReadOnlyList<string> imageUrls, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var nanoBananaImageService = scope.ServiceProvider.GetRequiredService<INanoBananaImageService>();
        var response = await nanoBananaImageService.GenerateFromTaskAsync(
            task.Prompt,
            task.ModelName,
            task.ResolutionCode,
            task.AspectRatioCode,
            task.Size,
            imageUrls,
            cancellationToken);

        return response.Url;
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
        logger.LogError(
            ex,
            "AI image task failed. TaskId={TaskId}, UserId={UserId}, ModelName={ModelName}, ResolutionCode={ResolutionCode}, QualityCode={QualityCode}, AspectRatioCode={AspectRatioCode}, Size={Size}, ImageCount={ImageCount}, CompletedImageCount={CompletedImageCount}, ReferenceImageUrls={ReferenceImageUrls}, MaskImageUrl={MaskImageUrl}",
            task.Id,
            task.UserId,
            task.ModelName,
            task.ResolutionCode,
            task.QualityCode,
            task.AspectRatioCode,
            task.Size,
            task.ImageCount,
            results.Count,
            task.ReferenceImageUrls,
            task.MaskImageUrl);

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
                    UpdatedAt = HongKongNow()
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken)
            : await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = message,
                    UpdatedAt = HongKongNow()
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

    private static DateTime HongKongNow()
    {
        return DateTime.UtcNow.AddHours(8);
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
