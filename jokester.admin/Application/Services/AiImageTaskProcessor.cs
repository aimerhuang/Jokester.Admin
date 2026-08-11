using System.Runtime.ExceptionServices;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Models.AiPromptFilter;
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
    IAiImageAdmissionService admissionService,
    IAiImageProviderGate providerGate,
    IAiPromptFilter promptFilter,
    ILogger<AiImageTaskProcessor> logger) : IAiImageTaskProcessor
{
    private const int MinutesPerImage = 5;
    private const int MaxTaskTimeoutMinutes = 10;
    private const int PendingTaskTimeoutMinutes = 120;
    private const int MaxImageGenerationConcurrency = 4;
    private const string GenericGenerationFailureMessage = "图片生成服务暂时不可用，请稍后重试。";

    public async Task ProcessAsync(long taskId, CancellationToken cancellationToken)
    {
        var claimed = await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity
            {
                Status = 3,
                StartedAt = HongKongNow(),
                UpdatedAt = HongKongNow()
            })
            .Where(x => x.Id == taskId && !x.IsDeleted && x.Status == 0 && x.BillingStatus == 0)
            .ExecuteCommandAsync(cancellationToken);
        if (claimed != 1)
        {
            return;
        }

        var task = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == taskId && !x.IsDeleted && x.Status == 3, cancellationToken);
        if (task is null)
        {
            return;
        }

        var results = DeserializeImageUrls(task.ResultUrls).ToList();
        try
        {
            using var taskTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            taskTimeout.CancelAfter(ResolveTaskTimeout(task.ImageCount));
            var taskToken = taskTimeout.Token;

            task.PromptPolicyVersion = await promptFilter.EnsureAllAllowedAsync(
                [
                    new AiPromptFilterText("prompt", task.Prompt),
                    new AiPromptFilterText("negativePrompt", task.NegativePrompt)
                ],
                taskToken);
            task.PromptCheckedAt = HongKongNow();
            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    PromptPolicyVersion = task.PromptPolicyVersion,
                    PromptCheckedAt = task.PromptCheckedAt,
                    UpdatedAt = task.PromptCheckedAt
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 3)
                .ExecuteCommandAsync(taskToken);

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

            var settlement = await pointService.SettleImageTaskAsync(
                task.Id,
                1,
                JsonSerializer.Serialize(results),
                null,
                results.Count,
                CancellationToken.None);
            if (settlement.Transitioned)
            {
                await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
            }
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

    public static TimeSpan ResolvePendingTaskTimeout() => TimeSpan.FromMinutes(PendingTaskTimeoutMinutes);

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
            .Where(x => x.Id == taskId && !x.IsDeleted && x.Status == 3)
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

    private async Task<string> GenerateImageWithConcurrencyAsync(
        Func<CancellationToken, Task<string>> generateImageAsync,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var providerLease = await providerGate.AcquireAsync(cancellationToken);
            try
            {
                var result = await generateImageAsync(cancellationToken);
                await providerGate.ReportSuccessAsync();
                return result;
            }
            catch (Exception ex) when (ex is AiPromptRejectedException or AiPromptFilterUnavailableException)
            {
                throw;
            }
            catch
            {
                await providerGate.ReportFailureAsync();
                throw;
            }
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
            task.UserId,
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
            task.UserId,
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
            "AI image task failed. TaskId={TaskId}, UserId={UserId}, ModelName={ModelName}, ResolutionCode={ResolutionCode}, QualityCode={QualityCode}, AspectRatioCode={AspectRatioCode}, Size={Size}, ImageCount={ImageCount}, CompletedImageCount={CompletedImageCount}, FailureType={FailureType}",
            task.Id,
            task.UserId,
            task.ModelName,
            task.ResolutionCode,
            task.QualityCode,
            task.AspectRatioCode,
            task.Size,
            task.ImageCount,
            results.Count,
            ex.GetType().Name);

        var message = SanitizeFailureMessage(ex);
        if (message.Length > 1000)
        {
            message = message[..1000];
        }

        var latestTask = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == task.Id && !x.IsDeleted, cancellationToken);
        var mergedResults = MergeImageUrls(
            DeserializeImageUrls(latestTask?.ResultUrls),
            results);
        var settlement = await pointService.SettleImageTaskAsync(
            task.Id,
            2,
            mergedResults.Count == 0 ? latestTask?.ResultUrls : JsonSerializer.Serialize(mergedResults),
            message,
            mergedResults.Count,
            cancellationToken);
        if (settlement.Transitioned)
        {
            await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
        }
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
            AiPromptRejectedException => "提示词包含不允许的内容，任务已取消并退款。",
            AiPromptFilterUnavailableException => GenericGenerationFailureMessage,
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

}
