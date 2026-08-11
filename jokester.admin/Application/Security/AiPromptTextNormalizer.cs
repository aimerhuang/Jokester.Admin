using System.Globalization;
using System.Text;
using jokester.admin.Application.Models.AiPromptFilter;

namespace jokester.admin.Application.Security;

public static class AiPromptTextNormalizer
{
    public static string NormalizeRuleTerm(string? value, string matchMode)
    {
        return string.Equals(matchMode, AiPromptFilterMatchModes.Word, StringComparison.Ordinal)
            ? Normalize(value, AiPromptFilterMatchModes.Compact)
            : Normalize(value, matchMode);
    }

    public static string Normalize(string? value, string matchMode = AiPromptFilterMatchModes.Contains)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = string.Equals(matchMode, AiPromptFilterMatchModes.Compact, StringComparison.Ordinal);
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune))
            {
                if (!compact)
                {
                    pendingSeparator = builder.Length > 0;
                }

                continue;
            }

            if (IsCombiningMark(category))
            {
                continue;
            }

            if (category is UnicodeCategory.Format
                or UnicodeCategory.Control
                or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (IsContent(category))
            {
                if (!compact && pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(rune.ToString());
                pendingSeparator = false;
                continue;
            }

            if (!compact)
            {
                pendingSeparator = builder.Length > 0;
            }
        }

        return builder.ToString().Trim();
    }

    public static AiPromptSeparatorInsensitiveText NormalizeIgnoringSeparators(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new AiPromptSeparatorInsensitiveText(string.Empty, []);
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var separatorBefore = new List<bool>(normalized.Length);
        var pendingSeparator = false;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (IsCombiningMark(category)
                || category is UnicodeCategory.Format
                    or UnicodeCategory.Control
                    or UnicodeCategory.Surrogate
                    or UnicodeCategory.PrivateUse
                    or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (!IsContent(category))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            var runeText = rune.ToString();
            for (var index = 0; index < runeText.Length; index++)
            {
                builder.Append(runeText[index]);
                separatorBefore.Add(index == 0 && pendingSeparator);
            }

            pendingSeparator = false;
        }

        return new AiPromptSeparatorInsensitiveText(builder.ToString(), separatorBefore.ToArray());
    }

    private static bool IsCombiningMark(UnicodeCategory category) => category is
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or
        UnicodeCategory.EnclosingMark;

    private static bool IsContent(UnicodeCategory category) => category is
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter or
        UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or
        UnicodeCategory.OtherNumber;
}

public sealed record AiPromptSeparatorInsensitiveText(string Value, IReadOnlyList<bool> SeparatorBefore);
