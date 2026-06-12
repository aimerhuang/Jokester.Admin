namespace jokester.admin.Application.Abstractions;

public interface IPasswordHasher
{
    (string Hash, string Salt) HashPassword(string password);

    bool Verify(string password, string passwordHash, string? salt);
}
