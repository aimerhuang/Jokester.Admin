using jokester.admin.Application.DTOs.AiImages;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.Abstractions;

public interface IMediaAssetService
{
    Task<UploadAiImageResponse> UploadAsync(long ownerUserId, IFormFile file, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ResolveOwnedReferenceUrlsAsync(
        long userId,
        bool isSuperAdmin,
        IReadOnlyList<string>? assetIds,
        IReadOnlyList<string>? legacyUrls,
        CancellationToken cancellationToken);

    Task<string?> ResolveOwnedMaskUrlAsync(
        long userId,
        bool isSuperAdmin,
        string? assetId,
        string? legacyUrl,
        CancellationToken cancellationToken);

    Task<MediaAssetContent?> GetContentAsync(
        string assetId,
        long userId,
        bool isSuperAdmin,
        bool thumbnail,
        CancellationToken cancellationToken);

    Task DeleteOwnedAsync(
        string assetId,
        long userId,
        CancellationToken cancellationToken);
}

public sealed record MediaAssetContent(string FullPath, string MimeType, DateTime CreatedAt);
