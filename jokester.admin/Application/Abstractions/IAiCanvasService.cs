using jokester.admin.Application.DTOs.AiCanvases;

namespace jokester.admin.Application.Abstractions;

public interface IAiCanvasService
{
    Task<AiCanvasPageResult> GetPageAsync(AiCanvasQuery query, CancellationToken cancellationToken);

    Task<AiCanvasDetailDto> CreateAsync(CreateAiCanvasRequest request, CancellationToken cancellationToken);

    Task<AiCanvasDetailDto> GetAsync(string canvasId, CancellationToken cancellationToken);

    Task<SaveAiCanvasDocumentResponse> SaveDocumentAsync(
        string canvasId,
        SaveAiCanvasDocumentRequest request,
        CancellationToken cancellationToken);

    Task<AiCanvasSummaryDto> RenameAsync(string canvasId, RenameAiCanvasRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(string canvasId, CancellationToken cancellationToken);
}
