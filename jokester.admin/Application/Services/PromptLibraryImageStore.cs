using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace jokester.admin.Application.Services;

public sealed partial class PromptLibraryImageStore : IPromptLibraryImageStore
{
    private const int MaxWidth = 8192;
    private const int MaxHeight = 8192;
    private const long MaxPixels = 16_777_216;
    private readonly HttpClient _httpClient;
    private readonly PromptLibraryOptions _options;
    private readonly ILogger<PromptLibraryImageStore> _logger;
    private readonly string _rootPath;
    private readonly string _sourcePath;
    private readonly string _stagingRootPath;
    private readonly HashSet<string> _allowedHosts;
    private readonly ConcurrentDictionary<string, StoredImageValidation> _validationCache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public PromptLibraryImageStore(
        HttpClient httpClient,
        IOptions<PromptLibraryOptions> options,
        ILogger<PromptLibraryImageStore> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _rootPath = EnsureTrailingSeparator(Path.GetFullPath(_options.ImageRoot));
        _sourcePath = EnsureTrailingSeparator(Path.Combine(_rootPath, _options.Source));
        _stagingRootPath = EnsureTrailingSeparator(Path.Combine(_rootPath, ".staging"));
        _allowedHosts = _options.ImageAllowedHosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string RootPath => _rootPath;

    public string CreateStagingDirectory(long syncRunId)
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_sourcePath);
        Directory.CreateDirectory(_stagingRootPath);
        var path = Path.Combine(_stagingRootPath, $"{syncRunId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public void DeleteStagingDirectory(string stagingDirectory)
    {
        var fullPath = EnsureTrailingSeparator(Path.GetFullPath(stagingDirectory));
        if (!fullPath.StartsWith(_stagingRootPath, PathComparison)
            || string.Equals(fullPath, _stagingRootPath, PathComparison))
        {
            throw new InvalidOperationException("The staging directory is outside the configured prompt image staging root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    public bool IsStoredImageAvailable(string? relativePath)
    {
        if (!TryResolveStoredPath(relativePath, out var fullPath))
        {
            return false;
        }

        return IsStoredFileValid(fullPath);
    }

    public async Task<PromptStoredImage> PrepareAsync(
        int externalNo,
        string sourceUrl,
        string? reusableRelativePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        ValidateSourceUri(sourceUrl);
        if (IsStoredImageAvailable(reusableRelativePath))
        {
            return new PromptStoredImage(NormalizeRelativePath(reusableRelativePath!), true);
        }

        var stagingRoot = EnsureTrailingSeparator(Path.GetFullPath(stagingDirectory));
        if (!stagingRoot.StartsWith(_stagingRootPath, PathComparison))
        {
            throw new InvalidOperationException("The staging directory is outside the configured prompt image staging root.");
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt <= _options.RetryCount; attempt++)
        {
            var partPath = Path.Combine(stagingRoot, $"{externalNo}-{Guid.NewGuid():N}.part");
            try
            {
                return await DownloadOnceAsync(externalNo, sourceUrl, partPath, cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < _options.RetryCount)
            {
                lastException = ex;
                TryDeleteFile(partPath);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
            }
            catch
            {
                TryDeleteFile(partPath);
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("Prompt image download failed.");
    }

    public Task CleanupAsync(
        IReadOnlySet<string> referencedRelativePaths,
        DateTime retainAfterUtc,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_sourcePath))
        {
            return Task.CompletedTask;
        }

        var normalizedReferences = referencedRelativePaths
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in new DirectoryInfo(_sourcePath).EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GeneratedFileNameRegex().IsMatch(file.Name))
            {
                continue;
            }

            var relativePath = $"{_options.Source}/{file.Name}";
            if (normalizedReferences.Contains(relativePath) || file.LastWriteTimeUtc >= retainAfterUtc)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(file.FullName);
            if (!fullPath.StartsWith(_sourcePath, PathComparison))
            {
                continue;
            }

            try
            {
                file.Delete();
                _validationCache.TryRemove(fullPath, out _);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    "Unable to delete an orphaned prompt image. FileName={FileName}, FailureType={FailureType}",
                    file.Name,
                    ex.GetType().Name);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    "Unable to delete an orphaned prompt image. FileName={FileName}, FailureType={FailureType}",
                    file.Name,
                    ex.GetType().Name);
            }
        }

        return Task.CompletedTask;
    }

    public long GetStoredBytes()
    {
        if (!Directory.Exists(_sourcePath))
        {
            return 0;
        }

        return new DirectoryInfo(_sourcePath)
            .EnumerateFiles()
            .Where(file => GeneratedFileNameRegex().IsMatch(file.Name))
            .Sum(file => file.Length);
    }

    public long? GetAvailableBytes()
    {
        try
        {
            var root = Path.GetPathRoot(_rootPath);
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<PromptStoredImage> DownloadOnceAsync(
        int externalNo,
        string sourceUrl,
        string partPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new InvalidDataException("Prompt image redirects are not allowed.");
        }
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is <= 0
            || response.Content.Headers.ContentLength > _options.ImageMaxBytes)
        {
            throw new InvalidDataException("Prompt image content length is missing or outside the configured limit.");
        }

        var declaredMediaType = response.Content.Headers.ContentType?.MediaType;
        if (!IsSupportedMediaType(declaredMediaType))
        {
            throw new InvalidDataException("Prompt image Content-Type is not supported.");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(
            partPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await CopyWithLimitAsync(source, destination, _options.ImageMaxBytes, cancellationToken);
        }

        var format = await ValidateImageAsync(partPath, declaredMediaType!, cancellationToken);
        var hash = await ComputeSha256Async(partPath, cancellationToken);
        var fileName = $"{externalNo}-{hash[..12]}.{format.Extension}";
        var finalPath = Path.Combine(_sourcePath, fileName);
        Directory.CreateDirectory(_sourcePath);
        try
        {
            File.Move(partPath, finalPath);
            _validationCache.TryRemove(finalPath, out _);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            if (IsStoredFileValid(finalPath))
            {
                TryDeleteFile(partPath);
            }
            else
            {
                File.Move(partPath, finalPath, overwrite: true);
                _validationCache.TryRemove(finalPath, out _);
            }
        }

        return new PromptStoredImage($"{_options.Source}/{fileName}", false);
    }

    private bool IsStoredFileValid(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length <= 0 || file.Length > _options.ImageMaxBytes)
            {
                _validationCache.TryRemove(fullPath, out _);
                return false;
            }

            var fingerprint = new StoredImageValidation(file.Length, file.LastWriteTimeUtc.Ticks, true);
            if (_validationCache.TryGetValue(fullPath, out var cached)
                && cached.Length == fingerprint.Length
                && cached.LastWriteTimeUtcTicks == fingerprint.LastWriteTimeUtcTicks)
            {
                return cached.IsValid;
            }

            var fileNameMatch = GeneratedFileNameRegex().Match(file.Name);
            if (!fileNameMatch.Success)
            {
                _validationCache[fullPath] = fingerprint with { IsValid = false };
                return false;
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actualHash.StartsWith(fileNameMatch.Groups["hash"].Value, StringComparison.Ordinal))
            {
                _validationCache[fullPath] = fingerprint with { IsValid = false };
                return false;
            }

            stream.Position = 0;
            var decoderOptions = new DecoderOptions { SkipMetadata = true, MaxFrames = 2 };
            var info = Image.Identify(decoderOptions, stream);
            if (info is null)
            {
                _validationCache[fullPath] = fingerprint with { IsValid = false };
                return false;
            }

            var format = ResolveFormat(info.Metadata.DecodedImageFormat);
            var isValid = string.Equals(
                    format.Extension,
                    fileNameMatch.Groups["extension"].Value,
                    StringComparison.OrdinalIgnoreCase)
                && info.Width > 0
                && info.Height > 0
                && info.Width <= MaxWidth
                && info.Height <= MaxHeight
                && (long)info.Width * info.Height <= MaxPixels;
            _validationCache[fullPath] = fingerprint with { IsValid = isValid };
            return isValid;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidImageContentException
            or UnknownImageFormatException
            or NotSupportedException)
        {
            _validationCache.TryRemove(fullPath, out _);
            return false;
        }
    }

    private async Task<ImageFormatResult> ValidateImageAsync(
        string path,
        string declaredMediaType,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var decoderOptions = new DecoderOptions { SkipMetadata = true, MaxFrames = 2 };
        var info = await Image.IdentifyAsync(decoderOptions, stream, cancellationToken)
            ?? throw new InvalidDataException("Prompt image cannot be decoded.");
        var format = ResolveFormat(info.Metadata.DecodedImageFormat);
        if (!string.Equals(format.MediaType, NormalizeMediaType(declaredMediaType), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Prompt image Content-Type does not match its file signature.");
        }
        if (info.Width <= 0 || info.Height <= 0
            || info.Width > MaxWidth || info.Height > MaxHeight
            || (long)info.Width * info.Height > MaxPixels)
        {
            throw new InvalidDataException("Prompt image dimensions are outside the supported safety bounds.");
        }

        stream.Position = 0;
        using var image = await Image.LoadAsync(decoderOptions, stream, cancellationToken);
        if (image.Frames.Count != 1)
        {
            throw new InvalidDataException("Animated prompt images are not supported.");
        }

        return format;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidDataException("Prompt image exceeds the configured size limit.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
            {
                throw new InvalidDataException("Prompt image response was empty.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ValidateSourceUri(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !_allowedHosts.Contains(uri.IdnHost))
        {
            throw new InvalidDataException("Prompt image URL is not an allowed HTTPS source.");
        }
    }

    private bool TryResolveStoredPath(string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalized = NormalizeRelativePath(relativePath);
        if (!normalized.StartsWith(_options.Source + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(_sourcePath, PathComparison);
    }

    private static ImageFormatResult ResolveFormat(IImageFormat? format) => format switch
    {
        JpegFormat => new ImageFormatResult("image/jpeg", "jpg"),
        PngFormat => new ImageFormatResult("image/png", "png"),
        WebpFormat => new ImageFormatResult("image/webp", "webp"),
        _ => throw new InvalidDataException("Only JPEG, PNG, and WebP prompt images are supported.")
    };

    private static bool IsSupportedMediaType(string? mediaType) =>
        NormalizeMediaType(mediaType) is "image/jpeg" or "image/png" or "image/webp";

    private static string? NormalizeMediaType(string? mediaType) =>
        string.Equals(mediaType, "image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : mediaType?.ToLowerInvariant();

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or IOException or TaskCanceledException;

    private static string NormalizeRelativePath(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    [GeneratedRegex(@"^\d+-(?<hash>[0-9a-f]{12})\.(?<extension>jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedFileNameRegex();

    private sealed record ImageFormatResult(string MediaType, string Extension);

    private sealed record StoredImageValidation(long Length, long LastWriteTimeUtcTicks, bool IsValid);
}
