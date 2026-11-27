using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Tests;

public sealed class SiteNoticeRulesTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.test/a")]
    [InlineData("/\\example.test")]
    [InlineData("/test\\page")]
    [InlineData("http://example.test/a")]
    [InlineData("data:text/plain,x")]
    [InlineData("https://user@example.test")]
    [InlineData("/page\n")]
    public void RejectsInvalidLinks(string url) => Assert.Equal("VALIDATION_ERROR", Assert.Throws<AppException>(() => SiteNoticeRules.LinkUrl(url)).MachineCode);

    [Theory]
    [InlineData("/pricing")]
    [InlineData("/")]
    [InlineData("https://example.test/a?b=c")]
    public void AllowsHttpsAndSitePaths(string url) => Assert.Equal(url, SiteNoticeRules.LinkUrl(url));

    [Theory]
    [InlineData("https://qq.com/invite")]
    [InlineData("https://qm.qq.com.example.test/q")]
    [InlineData("https://qm.qq.com:4567/q")]
    [InlineData("https://qun.qq.com@jq.qq.com/q")]
    [InlineData("/invite")]
    public void RejectsUnofficialInvites(string url) => Assert.Throws<AppException>(() => SiteNoticeRules.JoinUrl(url));

    [Theory]
    [InlineData("https://QM.qq.com/q/x")]
    [InlineData("https://qun.qq.com:443/x")]
    [InlineData("https://jq.qq.com/?k=x")]
    public void AllowsOfficialInvites(string url) => Assert.Equal(url, SiteNoticeRules.JoinUrl(url));

    [Fact]
    public void ChecksNumberIntervalAndMediaBoundaries()
    {
        foreach (var number in new[] { "", "1234", "01234", "１２３４５", "1234567890123" })
            Assert.Throws<AppException>(() => SiteNoticeRules.GroupNumber(number, true));
        Assert.Equal("12345", SiteNoticeRules.GroupNumber(" 12345 ", true));
        Assert.Equal("123456789012", SiteNoticeRules.GroupNumber("123456789012", true));
        Assert.Equal(string.Empty, SiteNoticeRules.GroupNumber(null, false));
        Assert.Throws<AppException>(() => SiteNoticeRules.RemindInterval(0));
        Assert.Throws<AppException>(() => SiteNoticeRules.RemindInterval(31));
        var prefix = "/site-media/announcement/202611/" + new string('a', 32);
        Assert.Equal(prefix + ".webp", SiteNoticeRules.CoverUrl(prefix + ".webp"));
        foreach (var path in new[] { prefix + ".gif", prefix + ".PNG", "https://example.test" + prefix + ".png", prefix + "/../test.png" })
            Assert.Throws<AppException>(() => SiteNoticeRules.CoverUrl(path));
    }

    [Fact]
    public void NormalizesTextAndPreservesContentFormatting()
    {
        Assert.Equal("公告", SiteNoticeRules.RequiredText(" 公告 ", 2, "标题"));
        Assert.Throws<AppException>(() => SiteNoticeRules.RequiredText(" ", 80, "标题"));
        Assert.Throws<AppException>(() => SiteNoticeRules.OptionalText("x\na", 80, "标题"));
        Assert.Throws<AppException>(() => SiteNoticeRules.OptionalText("abc", 2, "标题"));
        Assert.Equal(" 一\n\t二 ", SiteNoticeRules.Content(" 一\r\n\t二 "));
        Assert.Throws<AppException>(() => SiteNoticeRules.Content("x\ra"));
        Assert.Throws<AppException>(() => SiteNoticeRules.Content(new string('a', 5001)));
        Assert.Null(SiteNoticeRules.Content("\t \r\n"));
    }
}
