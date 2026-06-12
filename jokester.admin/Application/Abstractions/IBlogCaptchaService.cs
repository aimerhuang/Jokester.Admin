using jokester.admin.Application.DTOs.Blog;

namespace jokester.admin.Application.Abstractions;

public interface IBlogCaptchaService
{
    Task<BlogCaptchaDto> CreateAsync(CancellationToken cancellationToken);

    Task<bool> ValidateAsync(string captchaId, string answer, CancellationToken cancellationToken);
}
