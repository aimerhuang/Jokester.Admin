namespace jokester.admin.Application.Abstractions;

public interface IEmailValidationService
{
    Task<string> ValidateAndNormalizeAsync(string email, CancellationToken cancellationToken);
}
