using System.Security.Cryptography;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class MediaAssetService(ISqlSugarClient db, IAiMediaPathResolver mediaPathResolver) : IMediaAssetService
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int ThumbnailLongSide = 512;
    private const string PrivateMediaPrefix = "/api/media/ai/";

    public async Task<UploadAiImageResponse> UploadAsync(
        long ownerUserId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var image = await ImageUploadValidator.ValidateAsync(file, MaxUploadBytes, cancellationToken);
        var now = DateTime.UtcNow;
        var assetId = CreateAssetId(now);
        var directory = $"{ownerUserId}/assets/{now:yyyyMM}";
        var baseName = Guid.NewGuid().ToString("N");
        var storageKey = $"{directory}/{baseName}{image.Extension}";
        var thumbnailKey = $"{directory}/{baseName}_thumb.webp";
        var storagePath = mediaPathResolver.ResolveFilePath(storageKey);
        var thumbnailPath = mediaPathResolver.ResolveFilePath(thumbnailKey);
        Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);

        await File.WriteAllBytesAsync(storagePath, image.Content, cancellationToken);
        try
        {
            await WriteThumbnailAsync(image.Content, thumbnailPath, cancellationToken);
            var entity = new MediaAssetEntity
            {
                AssetId = assetId,
                OwnerUserId = ownerUserId,
                AssetType = "reference",
                StorageKey = storageKey,
                ThumbnailKey = thumbnailKey,
                MimeType = image.MimeType,
                Width = image.Width,
                Height = image.Height,
                SizeBytes = image.Content.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(image.Content)),
                MetadataStripped = true,
                CreatedAt = now,
                IsDeleted = false
            };
            await db.Insertable(entity).ExecuteCommandAsync(cancellationToken);
            return MapUploadResponse(entity);
        }
        catch
        {
            TryDelete(storagePath);
            TryDelete(thumbnailPath);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ResolveOwnedReferenceUrlsAsync(
        long userId,
        bool isSuperAdmin,
        IReadOnlyList<string>? assetIds,
        IReadOnlyList<string>? legacyUrls,
        CancellationToken cancellationToken)
    {
        var normalizedIds = NormalizeAssetIds(assetIds);
        var urls = new List<string>(normalizedIds.Count + (legacyUrls?.Count ?? 0));
        if (normalizedIds.Count > 0)
        {
            var idArray = normalizedIds.ToArray();
            var assets = await db.Queryable<MediaAssetEntity>()
                .Where(x => idArray.Contains(x.AssetId) && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            var lookup = assets.ToDictionary(x => x.AssetId, StringComparer.Ordinal);
            foreach (var assetId in normalizedIds)
            {
                if (!lookup.TryGetValue(assetId, out var asset))
                {
                    throw HiddenAssetNotFound();
                }
                if (!isSuperAdmin && asset.OwnerUserId != userId)
                {
                    throw HiddenAssetNotFound();
                }
                EnsureStorageFileExists(asset.StorageKey);
                urls.Add(ToPrivateMediaUrl(asset.StorageKey));
            }
        }

        foreach (var legacyUrl in legacyUrls ?? [])
        {
            urls.Add(ValidateLegacyUrl(legacyUrl, userId, isSuperAdmin));
        }

        if (urls.Count > 6)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Reference images must not exceed 6.");
        }
        return urls.Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task<string?> ResolveOwnedMaskUrlAsync(
        long userId,
        bool isSuperAdmin,
        string? assetId,
        string? legacyUrl,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(assetId) && !string.IsNullOrWhiteSpace(legacyUrl))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Use either maskAssetId or maskImageUrl, not both.");
        }
        if (!string.IsNullOrWhiteSpace(assetId))
        {
            var normalized = NormalizeAssetId(assetId);
            var asset = await db.Queryable<MediaAssetEntity>()
                .FirstAsync(x => x.AssetId == normalized && !x.IsDeleted, cancellationToken);
            if (asset is null || (!isSuperAdmin && asset.OwnerUserId != userId)) throw HiddenAssetNotFound();
            EnsureStorageFileExists(asset.StorageKey);
            if (!string.Equals(Path.GetExtension(asset.StorageKey), ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Mask assets must be PNG images.");
            }
            return ToPrivateMediaUrl(asset.StorageKey);
        }
        return string.IsNullOrWhiteSpace(legacyUrl) ? null : ValidateLegacyUrl(legacyUrl, userId, isSuperAdmin);
    }

    public async Task<MediaAssetContent?> GetContentAsync(
        string assetId,
        long userId,
        bool isSuperAdmin,
        bool thumbnail,
        CancellationToken cancellationToken)
    {
        string normalized;
        try
        {
            normalized = NormalizeAssetId(assetId);
        }
        catch (AppException)
        {
            return null;
        }
        var asset = await db.Queryable<MediaAssetEntity>()
            .FirstAsync(x => x.AssetId == normalized && !x.IsDeleted, cancellationToken);
        if (asset is null || (!isSuperAdmin && asset.OwnerUserId != userId)) return null;
        var key = thumbnail ? asset.ThumbnailKey : asset.StorageKey;
        if (string.IsNullOrWhiteSpace(key)) return null;
        string fullPath;
        try
        {
            fullPath = mediaPathResolver.ResolveFilePath(key);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        if (!File.Exists(fullPath)) return null;
        return new MediaAssetContent(fullPath, thumbnail ? "image/webp" : asset.MimeType, asset.CreatedAt);
    }

    public async Task DeleteOwnedAsync(
        string assetId,
        long userId,
        CancellationToken cancellationToken)
    {
        string normalized;
        try
        {
            normalized = NormalizeAssetId(assetId);
        }
        catch (AppException)
        {
            throw HiddenAssetNotFound();
        }

        var asset = await db.Queryable<MediaAssetEntity>()
            .FirstAsync(x => x.AssetId == normalized, cancellationToken);
        if (asset is null || asset.OwnerUserId != userId)
        {
            throw HiddenAssetNotFound();
        }

        var storagePath = ResolveStoredFilePath(asset.StorageKey);
        var thumbnailPath = string.IsNullOrWhiteSpace(asset.ThumbnailKey)
            ? null
            : ResolveStoredFilePath(asset.ThumbnailKey);

        if (!asset.IsDeleted)
        {
            var now = DateTime.UtcNow;
            await db.Updateable<MediaAssetEntity>()
                .SetColumns(x => new MediaAssetEntity { IsDeleted = true, DeletedAt = now })
                .Where(x => x.Id == asset.Id && x.OwnerUserId == userId && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }

        DeleteStoredFile(storagePath);
        if (thumbnailPath is not null)
        {
            DeleteStoredFile(thumbnailPath);
        }
    }

    private static async Task WriteThumbnailAsync(byte[] content, string path, CancellationToken cancellationToken)
    {
        using var image = Image.Load(content);
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(ThumbnailLongSide, ThumbnailLongSide),
            Sampler = KnownResamplers.Lanczos3
        }));
        await image.SaveAsync(path, new WebpEncoder { Quality = 80 }, cancellationToken);
    }

    private string ValidateLegacyUrl(string? value, long userId, bool isSuperAdmin)
    {
        var url = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (!url.StartsWith(PrivateMediaPrefix, StringComparison.Ordinal)
            || url.IndexOfAny(['?', '#']) >= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Legacy image URLs must be private same-origin media paths.");
        }
        var relative = url[PrivateMediaPrefix.Length..];
        if (!isSuperAdmin && !relative.StartsWith($"{userId}/", StringComparison.Ordinal)) throw HiddenAssetNotFound();
        EnsureStorageFileExists(relative);
        return url;
    }

    private void EnsureStorageFileExists(string storageKey)
    {
        var fullPath = ResolveStoredFilePath(storageKey);
        if (!File.Exists(fullPath)) throw HiddenAssetNotFound();
    }

    private string ResolveStoredFilePath(string storageKey)
    {
        try
        {
            return mediaPathResolver.ResolveFilePath(storageKey);
        }
        catch (InvalidOperationException)
        {
            throw HiddenAssetNotFound();
        }
    }

    private static IReadOnlyList<string> NormalizeAssetIds(IReadOnlyList<string>? assetIds)
    {
        if (assetIds is null || assetIds.Count == 0) return [];
        if (assetIds.Count > 6)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Reference assets must not exceed 6.");
        }
        return assetIds.Select(NormalizeAssetId).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeAssetId(string? assetId)
    {
        var value = string.IsNullOrWhiteSpace(assetId) ? string.Empty : assetId.Trim().ToUpperInvariant();
        if (value.Length is < 12 or > 40 || !value.StartsWith("AST", StringComparison.Ordinal)
            || value.Any(ch => !(ch is >= 'A' and <= 'Z' || char.IsDigit(ch))))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Invalid assetId.");
        }
        return value;
    }

    private static UploadAiImageResponse MapUploadResponse(MediaAssetEntity entity) => new()
    {
        AssetId = entity.AssetId,
        Url = $"/api/assets/{entity.AssetId}/content",
        ThumbnailUrl = $"/api/assets/{entity.AssetId}/thumbnail",
        FileName = entity.AssetId,
        MimeType = entity.MimeType,
        FileSize = entity.SizeBytes,
        SizeBytes = entity.SizeBytes,
        Width = entity.Width,
        Height = entity.Height,
        MetadataStripped = entity.MetadataStripped,
        CreatedAt = ApiDateTime.FromUtcStorage(entity.CreatedAt)
    };

    private static string ToPrivateMediaUrl(string storageKey) => PrivateMediaPrefix + storageKey.Replace('\\', '/');

    private static string CreateAssetId(DateTime now) => $"AST{now:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..35].ToUpperInvariant();

    private static AppException HiddenAssetNotFound() =>
        new(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Asset does not exist.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteStoredFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
