using jokester.admin.Application.DTOs.NanoBananaImages;

namespace jokester.admin.Application.Abstractions;

public interface INanoBananaImageService
{
    Task<GenerateNanoBananaImageResponse> GenerateAsync(GenerateNanoBananaImageRequest request, CancellationToken cancellationToken);

    Task<long> CreateAsync(CreateNanoBananaImageTaskRequest request, CancellationToken cancellationToken);

    Task<GenerateNanoBananaImageResponse> GenerateFromTaskAsync(string prompt, string? modelCode, string resolutionCode, string aspectRatioCode, string size, IReadOnlyList<string> imageUrls, CancellationToken cancellationToken);
}
