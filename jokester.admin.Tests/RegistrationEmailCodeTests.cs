using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class RegistrationEmailCodeTests
{
    [Fact]
    public async Task SendEmailCodeAsync_RemovesOnlyTheWrittenCode_WhenEmailDeliveryFails()
    {
        const string normalizedEmail = "delivery-failure@example.test";
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var emailValidation = new Mock<IEmailValidationService>();
        emailValidation
            .Setup(x => x.ValidateAndNormalizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedEmail);

        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(x => x.SendAsync(
                normalizedEmail,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var service = new RegistrationService(
            Mock.Of<ISqlSugarClient>(),
            Mock.Of<IPasswordHasher>(),
            emailValidation.Object,
            emailSender.Object,
            connection.Object,
            Options.Create(new RedisOptions { InstanceName = "test:" }),
            NullLogger<RegistrationService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendEmailCodeAsync(
            new SendRegisterEmailCodeRequest { Email = normalizedEmail },
            CancellationToken.None));

        database.Verify(x => x.ScriptEvaluateAsync(
            It.Is<string>(script => script.Contains("redis.call('get'", StringComparison.Ordinal)),
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == (RedisKey)$"test:register_email_code:{normalizedEmail}"),
            It.Is<RedisValue[]>(values => values.Length == 1 && values[0].ToString().Length == 6),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
