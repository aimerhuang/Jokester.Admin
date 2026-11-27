using System.Text.RegularExpressions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Services;

public static partial class SiteNoticeRules
{
    public static string RequiredText(string? value, int max, string field) =>
        OptionalText(value, max, field) ?? throw Invalid($"请填写{field}");

    public static string? OptionalText(string? value, int max, string field)
    {
        if (value is null) return null;
        if (value.Any(char.IsControl)) throw Invalid($"{field}不能含控制字符或换行");
        var text = value.Trim();
        if (text.Length > max) throw Invalid($"{field}最多 {max} 个字符");
        return text.Length == 0 ? null : text;
    }

    public static string? Content(string? value)
    {
        var text = value?.Replace("\r\n", "\n");
        if (text is null) return null;
        if (text.Length > 5000 || text.Any(c => char.IsControl(c) && c != '\n' && c != '\t'))
            throw Invalid("正文最多 5000 个字符，仅支持纯文本、换行及制表符");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static string Category(string? value) => value is "feature" or "activity" or "maintenance" or "notice"
        ? value : throw Invalid("请选择有效的公告分类");

    public static string? CoverUrl(string? value)
    {
        var url = OptionalText(value, 255, "封面地址");
        if (url != null && !CoverPath().IsMatch(url)) throw Invalid("请使用本站上传的公告封面");
        return url;
    }

    public static string? LinkUrl(string? value)
    {
        var url = OptionalText(value, 500, "跳转链接");
        if (url is null) return null;
        if (!url.Contains('\\') && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return url;
        if (!TryHttps(url, out _)) throw Invalid("跳转链接须为站内路径或 HTTPS 地址");
        return url;
    }

    public static string? JoinUrl(string? value)
    {
        var url = OptionalText(value, 500, "加群链接");
        if (url is null) return null;
        if (!TryHttps(url, out var uri) || !uri!.IsDefaultPort || uri.IdnHost.ToLowerInvariant() is not ("qm.qq.com" or "qun.qq.com" or "jq.qq.com"))
            throw Invalid("请填写 QQ 官方加群链接");
        return url;
    }

    public static string GroupNumber(string? value, bool required)
    {
        var number = OptionalText(value, 12, "群号");
        if (number is null && !required) return string.Empty;
        if (number is null || !QqNumber().IsMatch(number)) throw Invalid("群号须为 5 至 12 位数字且首位非零");
        return number;
    }

    public static int RemindInterval(int days) => days is >= 1 and <= 30 ? days : throw Invalid("再次提醒间隔须为 1 至 30 天");

    private static bool TryHttps(string url, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length != 0 && uri.UserInfo.Length == 0 && !url.Contains('\\');

    private static AppException Invalid(string message) => new(ErrorCodes.BadRequest, message);

    [GeneratedRegex(@"\A/site-media/announcement/[0-9]{6}/[0-9a-f]{32}\.(png|jpe?g|webp)\z")]
    private static partial Regex CoverPath();

    [GeneratedRegex(@"\A[1-9][0-9]{4,11}\z")]
    private static partial Regex QqNumber();
}
