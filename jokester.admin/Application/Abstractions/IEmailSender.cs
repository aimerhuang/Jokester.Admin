namespace jokester.admin.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken);
}
