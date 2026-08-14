namespace jokester.admin.Application.Abstractions;

public interface IAppleAppAccountTokenService
{
    string GetForUser(long userId);
}
