using System.Runtime.ExceptionServices;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskProcessor(
    ISqlSugarClient db,
    IServiceScopeFactory scopeFactory,
    IPointService pointService,
    IAiImageAdmissionService admissionService,
    IAiImageProviderGate providerGate,
    IAiPromptFilter promptFilter,
    IAiImageModelConfigService modelConfigService,
    IAiImageCatalogService catalogService,
    IUserConsentService userConsentService,
    IOptions<AiImageSizeModeOptions> sizeModeOptions,
    ILogger<AiImageTaskProcessor> logger) : IAiImageTaskProcessor
{
    private const int MinutesPerImage = 5;
    private const int MaxTaskTimeoutMinutes = 10;
    private const int PendingTaskTimeoutMinutes = 120;
    private const int MaxImageGenerationConcurrency = 4;
    private const string GenericGenerationFailureMessage = "图片生成服务暂时不可用，请稍后重试。";
    private static readonly TimeSpan ClaimLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ClaimHeartbeatInterval = TimeSpan.FromSeconds(30);

    public async Task ProcessAsync(long taskId, CancellationToken cancellationToken)
    {
        var claimTokenHash = AiImageIdempotency.HashKey(Guid.NewGuid().ToString("N"));
        var claimNow = HongKongNow();
        var claimed = await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity
            {
                Status = 3,
                ClaimTokenHash = claimTokenHash,
                LeaseExpiresAt = claimNow.Add(ClaimLeaseDuration),
                HeartbeatAt = claimNow,
                StartedAt = claimNow,
                UpdatedAt = claimNow
            })
            .SetColumns(x => x.ClaimEpoch == x.ClaimEpoch + 1)
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

        if (task.SizeContractVersion == AiImageCatalogService.SizeContractVersion)
        {
            await ProcessVersionedTaskAsync(task, claimTokenHash, cancellationToken);
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
            var modelConfig = await modelConfigService.ResolveAsync(modelCode, task.ResolutionCode, taskToken);
            await userConsentService.EnsureAiProcessingConsentAsync(task.UserId, modelConfig.ConsentProviderCode, taskToken);
            if (AiImageModelConfigService.UsesGeminiImageProtocol(modelConfig))
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

    private async Task ProcessVersionedTaskAsync(
        AiImageTaskEntity task,
        string claimTokenHash,
        CancellationToken cancellationToken)
    {
        using var heartbeatStop = new CancellationTokenSource();
        using var claimLost = new CancellationTokenSource();
        var heartbeat = RunClaimHeartbeatAsync(task.Id, task.ClaimEpoch, claimTokenHash, heartbeatStop.Token, claimLost);
        AiImageProviderAttemptEntity? attempt = null;
        var providerCallStarted = false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, claimLost.Token);
            timeout.CancelAfter(ResolveTaskTimeout(1));
            var taskToken = timeout.Token;
            await EnsureClaimAsync(task.Id, task.ClaimEpoch, claimTokenHash, taskToken);

            task.PromptPolicyVersion = await promptFilter.EnsureAllAllowedAsync(
                [
                    new AiPromptFilterText("prompt", task.Prompt),
                    new AiPromptFilterText("negativePrompt", task.NegativePrompt)
                ],
                taskToken);
            task.PromptCheckedAt = HongKongNow();
            var promptUpdated = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    PromptPolicyVersion = task.PromptPolicyVersion,
                    PromptCheckedAt = task.PromptCheckedAt,
                    UpdatedAt = task.PromptCheckedAt
                })
                .Where(x => x.Id == task.Id
                    && !x.IsDeleted
                    && x.Status == 3
                    && x.BillingStatus == 0
                    && x.ClaimEpoch == task.ClaimEpoch
                    && x.ClaimTokenHash == claimTokenHash)
                .ExecuteCommandAsync(taskToken);
            if (promptUpdated != 1)
            {
                throw new AiImageExecutionLeaseLostException();
            }

            var accountAvailable = await db.Queryable<SysUserEntity>()
                .AnyAsync(x => x.Id == task.UserId && !x.IsDeleted && x.Status == 1, taskToken);
            if (!accountAvailable)
            {
                throw new VersionedPreflightException(
                    MachineErrorCodes.AccountUnavailable,
                    "账户当前不可用于 AI 生图。",
                    false);
            }
            await ValidateVersionedInputsAsync(task, taskToken);
            if (!task.ModelReleaseId.HasValue)
            {
                throw new VersionedPreflightException(
                    MachineErrorCodes.ModelReleaseRevoked,
                    "任务绑定的模型发布版本不可用。",
                    true);
            }

            IReadOnlyList<ResolvedAiImageModelConfig> routes;
            try
            {
                routes = await catalogService.ResolveRoutesAsync(
                    task.ModelReleaseId.Value,
                    task.SizeMode ?? AiImageCatalogService.ExplicitSizeMode,
                    task.ResolutionCode,
                    taskToken);
            }
            catch (AppException ex)
            {
                throw new VersionedPreflightException(
                    MachineErrorCodes.ModelReleaseRevoked,
                    "任务绑定的模型发布版本不可用。",
                    ex.Code == ErrorCodes.ServiceUnavailable,
                    ex);
            }

            foreach (var providerCode in routes.Select(x => x.ConsentProviderCode).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await userConsentService.EnsureAiProcessingConsentAsync(task.UserId, providerCode, taskToken);
                }
                catch (AppException ex) when (ex.MachineCode == MachineErrorCodes.AiConsentRequired)
                {
                    throw new VersionedPreflightException(
                        MachineErrorCodes.AiConsentRequired,
                        "AI 数据处理授权已失效。",
                        false,
                        ex);
                }
            }

            var now = HongKongNow();
            attempt = new AiImageProviderAttemptEntity
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                TaskId = task.Id,
                ClaimEpoch = task.ClaimEpoch,
                ModelReleaseId = task.ModelReleaseId,
                ReleaseRouteId = routes[0].ReleaseRouteId,
                RouteRole = routes[0].RouteRole,
                ConsentProviderCode = routes[0].ConsentProviderCode,
                UpstreamIdempotencyKey = task.IdempotencyKey,
                State = "prepared",
                StartedAt = now,
                Deadline = now.Add(ResolveTaskTimeout(1)),
                ReconcileBy = now.Add(ResolveTaskTimeout(1)).AddMinutes(sizeModeOptions.Value.AttemptReconcileMinutes)
            };
            attempt.Id = await db.Insertable(attempt).ExecuteReturnBigIdentityAsync();

            await using var providerLease = await providerGate.AcquireAsync(taskToken);
            providerLease.ThrowIfLost();
            await EnsureClaimAsync(task.Id, task.ClaimEpoch, claimTokenHash, taskToken);
            foreach (var providerCode in routes.Select(x => x.ConsentProviderCode).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await userConsentService.EnsureAiProcessingConsentAsync(task.UserId, providerCode, taskToken);
            }
            var inflight = await db.Updateable<AiImageProviderAttemptEntity>()
                .SetColumns(x => new AiImageProviderAttemptEntity { State = "inflight" })
                .Where(x => x.Id == attempt.Id && x.State == "prepared" && x.ClaimEpoch == task.ClaimEpoch)
                .ExecuteCommandAsync(taskToken);
            if (inflight != 1)
            {
                throw new AiImageExecutionLeaseLostException();
            }
            providerCallStarted = true;

            using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(taskToken, providerLease.LeaseLostToken);
            using var scope = scopeFactory.CreateScope();
            var aiImageService = scope.ServiceProvider.GetRequiredService<IAiImageService>();
            var response = await aiImageService.GenerateFromResolvedRoutesAsync(
                task.Prompt,
                routes,
                new ResolveAiImageParametersResponse
                {
                    SizeContractVersion = task.SizeContractVersion,
                    ModelCode = task.ModelCode,
                    SizeMode = task.SizeMode,
                    CatalogVersion = routes[0].CatalogVersion,
                    RequestedSize = task.RequestedSize,
                    ResolutionCode = task.ResolutionCode,
                    QualityCode = task.QualityCode,
                    AspectRatioCode = task.AspectRatioCode,
                    Width = task.RequestedWidth,
                    Height = task.RequestedHeight,
                    Size = task.RequestedSize ?? task.Size,
                    ProviderQuality = task.Quality
                },
                DeserializeReferenceImageUrls(task.ReferenceImageUrls),
                task.MaskImageUrl,
                task.UserId,
                providerCancellation.Token);
            providerLease.ThrowIfLost();
            await EnsureClaimAsync(task.Id, task.ClaimEpoch, claimTokenHash, taskToken);
            var output = response.Results?.FirstOrDefault(x => x.Status == "succeeded" && x.Url is not null)
                ?? throw new VersionedProviderOutputException();
            var resultUrls = JsonSerializer.Serialize(new[] { output.Url! });
            var settlement = await pointService.SettleVersionedImageTaskAsync(
                task.Id,
                1,
                new VersionedImageTaskSettlement(
                    resultUrls,
                    output.OutputWidth,
                    output.OutputHeight,
                    output.OutputSize,
                    output.MimeType,
                    null,
                    null,
                    null,
                    task.ClaimEpoch,
                    claimTokenHash,
                    attempt.AttemptId,
                    "succeeded"),
                null,
                1,
                CancellationToken.None);
            if (settlement.Transitioned)
            {
                await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
            }
            await providerGate.ReportSuccessAsync();
        }
        catch (OperationCanceledException ex) when (providerCallStarted)
        {
            await MarkProviderOutcomeUnknownAsync(task, claimTokenHash, attempt, ex, CancellationToken.None);
        }
        catch (AiImageExecutionLeaseLostException ex) when (providerCallStarted)
        {
            await MarkProviderOutcomeUnknownAsync(task, claimTokenHash, attempt, ex, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (providerCallStarted && ex is not VersionedPreflightException)
            {
                await providerGate.ReportFailureAsync();
            }
            await SettleVersionedFailureAsync(task, claimTokenHash, attempt, ex, CancellationToken.None);
        }
        finally
        {
            heartbeatStop.Cancel();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunClaimHeartbeatAsync(
        long taskId,
        long claimEpoch,
        string claimTokenHash,
        CancellationToken cancellationToken,
        CancellationTokenSource claimLost)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(ClaimHeartbeatInterval, cancellationToken);
            var now = HongKongNow();
            var affected = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    HeartbeatAt = now,
                    LeaseExpiresAt = now.Add(ClaimLeaseDuration),
                    UpdatedAt = now
                })
                .Where(x => x.Id == taskId
                    && !x.IsDeleted
                    && x.Status == 3
                    && x.BillingStatus == 0
                    && x.ClaimEpoch == claimEpoch
                    && x.ClaimTokenHash == claimTokenHash)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                claimLost.Cancel();
                return;
            }
        }
    }

    private async Task EnsureClaimAsync(
        long taskId,
        long claimEpoch,
        string claimTokenHash,
        CancellationToken cancellationToken)
    {
        var now = HongKongNow();
        var ownsClaim = await db.Queryable<AiImageTaskEntity>()
            .AnyAsync(x => x.Id == taskId
                && !x.IsDeleted
                && x.Status == 3
                && x.BillingStatus == 0
                && x.ClaimEpoch == claimEpoch
                && x.ClaimTokenHash == claimTokenHash
                && x.LeaseExpiresAt > now,
                cancellationToken);
        if (!ownsClaim)
        {
            throw new AiImageExecutionLeaseLostException();
        }
    }

    private async Task ValidateVersionedInputsAsync(AiImageTaskEntity task, CancellationToken cancellationToken)
    {
        var inputs = await db.Queryable<AiImageTaskInputEntity>()
            .Where(x => x.TaskId == task.Id)
            .OrderBy(x => x.Role)
            .OrderBy(x => x.InputOrdinal)
            .ToListAsync(cancellationToken);
        var expectedReferenceCount = DeserializeReferenceImageUrls(task.ReferenceImageUrls).Count;
        var expectedMaskCount = string.IsNullOrWhiteSpace(task.MaskImageUrl) ? 0 : 1;
        if (inputs.Count != expectedReferenceCount + expectedMaskCount
            || inputs.Any(x => x.OwnerUserId != task.UserId))
        {
            throw new VersionedPreflightException(
                MachineErrorCodes.InputAssetInvalid,
                "任务输入快照不完整。",
                false);
        }
        var assetIds = inputs
            .Where(x => x.InputKind == "asset" && x.AssetId is not null)
            .Select(x => x.AssetId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (assetIds.Length == 0)
        {
            return;
        }
        var assets = await db.Queryable<MediaAssetEntity>()
            .Where(x => assetIds.Contains(x.AssetId) && x.OwnerUserId == task.UserId && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        var lookup = assets.ToDictionary(x => x.AssetId, StringComparer.Ordinal);
        foreach (var input in inputs.Where(x => x.InputKind == "asset"))
        {
            if (input.AssetId is null
                || !lookup.TryGetValue(input.AssetId, out var asset)
                || !string.Equals(asset.StorageKey, input.StorageKey, StringComparison.Ordinal)
                || !string.Equals(asset.Sha256, input.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new VersionedPreflightException(
                    MachineErrorCodes.InputAssetInvalid,
                    "引用图片已失效或内容发生变化。",
                    false);
            }
        }
    }

    private async Task MarkProviderOutcomeUnknownAsync(
        AiImageTaskEntity task,
        string claimTokenHash,
        AiImageProviderAttemptEntity? attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "AI image provider outcome is unknown. TaskId={TaskId}, AttemptId={AttemptId}, FailureType={FailureType}",
            task.Id,
            attempt?.AttemptId,
            exception.GetType().Name);
        if (attempt is null)
        {
            return;
        }
        await db.Ado.BeginTranAsync();
        try
        {
            var attemptAffected = await db.Updateable<AiImageProviderAttemptEntity>()
                .SetColumns(x => new AiImageProviderAttemptEntity { State = "provider_unknown" })
                .Where(x => x.Id == attempt.Id && x.TaskId == task.Id && x.ClaimEpoch == task.ClaimEpoch && x.State == "inflight")
                .ExecuteCommandAsync(cancellationToken);
            var taskAffected = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    FailureCode = MachineErrorCodes.ProviderOutcomeUnknown,
                    FailureStage = "provider",
                    Retryable = false,
                    ErrorMessage = "图片服务结果暂时无法确认，系统正在对账。",
                    ClaimTokenHash = null,
                    LeaseExpiresAt = null,
                    UpdatedAt = HongKongNow()
                })
                .Where(x => x.Id == task.Id
                    && x.Status == 3
                    && x.BillingStatus == 0
                    && x.ClaimEpoch == task.ClaimEpoch
                    && x.ClaimTokenHash == claimTokenHash)
                .ExecuteCommandAsync(cancellationToken);
            if (attemptAffected != 1 || taskAffected != 1)
            {
                throw new AiImageExecutionLeaseLostException();
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
        }
    }

    private async Task SettleVersionedFailureAsync(
        AiImageTaskEntity task,
        string claimTokenHash,
        AiImageProviderAttemptEntity? attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var failure = MapVersionedFailure(exception);
        try
        {
            var settlement = await pointService.SettleVersionedImageTaskAsync(
                task.Id,
                2,
                new VersionedImageTaskSettlement(
                    null,
                    null,
                    null,
                    null,
                    null,
                    failure.Code,
                    failure.Stage,
                    failure.Retryable,
                    task.ClaimEpoch,
                    claimTokenHash,
                    attempt?.AttemptId,
                    "failed"),
                failure.Message,
                0,
                cancellationToken);
            if (settlement.Transitioned)
            {
                await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
            }
        }
        catch (Exception settlementException)
        {
            logger.LogWarning(
                "Versioned AI image failure could not be settled by the current claim. TaskId={TaskId}, FailureType={FailureType}",
                task.Id,
                settlementException.GetType().Name);
        }
    }

    private static VersionedTaskFailure MapVersionedFailure(Exception exception) => exception switch
    {
        VersionedPreflightException preflight => new(preflight.Code, "preflight", preflight.Retryable, preflight.Message),
        AiPromptRejectedException => new(MachineErrorCodes.PromptBlocked, "preflight", false, "提示词包含不允许的内容，任务已取消并退款。"),
        AiPromptFilterUnavailableException => new(MachineErrorCodes.ServiceUnavailable, "preflight", true, GenericGenerationFailureMessage),
        VersionedProviderOutputException => new(MachineErrorCodes.ProviderOutputInvalid, "output", true, GenericGenerationFailureMessage),
        AiImageExecutionLeaseLostException => new(MachineErrorCodes.ExecutionLeaseLost, "settlement", true, GenericGenerationFailureMessage),
        _ => new(MachineErrorCodes.ProviderUnavailable, "provider", true, GenericGenerationFailureMessage)
    };

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
            task.ResolutionCode ?? "1k",
            task.AspectRatioCode ?? "1:1",
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

    private sealed record VersionedTaskFailure(string Code, string Stage, bool Retryable, string Message);

    private sealed class VersionedPreflightException : Exception
    {
        public VersionedPreflightException(string code, string message, bool retryable, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            Retryable = retryable;
        }

        public string Code { get; }

        public bool Retryable { get; }
    }

    private sealed class VersionedProviderOutputException : Exception
    {
    }

    private sealed class AiImageExecutionLeaseLostException : Exception
    {
    }

}
