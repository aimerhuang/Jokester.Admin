namespace jokester.admin.Application.Abstractions;

public interface ICurrentUser
{
    long? UserId { get; }

    string? UserName { get; }

    bool IsSuperAdmin { get; }
}
