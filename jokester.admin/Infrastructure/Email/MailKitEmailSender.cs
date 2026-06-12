using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace jokester.admin.Infrastructure.Email;

public sealed class MailKitEmailSender(
    IOptions<MailOptions> options,
    ILogger<MailKitEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken)
    {
        var mailOptions = options.Value;
        ValidateOptions(mailOptions);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(mailOptions.FromName) ? mailOptions.FromAddress : mailOptions.FromName,
            mailOptions.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        using var client = new SmtpClient();
        var secureSocketOptions = ResolveSecureSocketOptions(mailOptions);
        try
        {
            await client.ConnectAsync(mailOptions.Host, mailOptions.Port, secureSocketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mailOptions.UserName))
            {
                await client.AuthenticateAsync(mailOptions.UserName, mailOptions.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSmtpFailure(logger, ex, mailOptions, secureSocketOptions);
            throw new AppException(
                ErrorCodes.BadRequest,
                "SMTP email sending failed. Check Mail host, port, secure socket option, account, and SMTP authorization code.");
        }
    }

    private static void LogSmtpFailure(
        ILogger logger,
        Exception exception,
        MailOptions options,
        SecureSocketOptions secureSocketOptions)
    {
        if (exception is SmtpCommandException smtpCommandException)
        {
            logger.LogError(
                smtpCommandException,
                "SMTP email sending failed. Host={Host}, Port={Port}, SecureSocketOptions={SecureSocketOptions}, UserNameConfigured={UserNameConfigured}, SmtpErrorCode={SmtpErrorCode}, SmtpStatusCode={SmtpStatusCode}",
                options.Host,
                options.Port,
                secureSocketOptions,
                !string.IsNullOrWhiteSpace(options.UserName),
                smtpCommandException.ErrorCode,
                smtpCommandException.StatusCode);
            return;
        }

        logger.LogError(
            exception,
            "SMTP email sending failed. Host={Host}, Port={Port}, SecureSocketOptions={SecureSocketOptions}, UserNameConfigured={UserNameConfigured}",
            options.Host,
            options.Port,
            secureSocketOptions,
            !string.IsNullOrWhiteSpace(options.UserName));
    }

    private static void ValidateOptions(MailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("Missing Mail:Host configuration.");
        }

        if (options.Port <= 0)
        {
            throw new InvalidOperationException("Mail:Port must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException("Missing Mail:FromAddress configuration.");
        }

        if (!string.IsNullOrWhiteSpace(options.UserName) && string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Missing Mail:Password configuration.");
        }
    }

    private static SecureSocketOptions ResolveSecureSocketOptions(MailOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SecureSocketOptions))
        {
            return Enum.TryParse<SecureSocketOptions>(options.SecureSocketOptions, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Invalid Mail:SecureSocketOptions configuration.");
        }

        if (options.UseSsl)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        return options.Port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.StartTlsWhenAvailable
        };
    }
}
