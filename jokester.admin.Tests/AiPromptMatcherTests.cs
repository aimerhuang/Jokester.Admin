using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;

namespace jokester.admin.Tests;

public sealed class AiPromptMatcherTests
{
    [Fact]
    public void Normalizer_HandlesFullWidthCaseWhitespaceAndZeroWidthCharacters()
    {
        var input = "  ＢＡＤ\u200B\tWORD  ";

        var normalized = AiPromptTextNormalizer.Normalize(input);
        var compact = AiPromptTextNormalizer.Normalize(input, AiPromptFilterMatchModes.Compact);
        var combiningMarks = AiPromptTextNormalizer.Normalize("r\u0336a\u0336p\u0336e\u0336");

        Assert.Equal("bad word", normalized);
        Assert.Equal("badword", compact);
        Assert.Equal("rape", combiningMarks);
    }

    [Fact]
    public void Matcher_BlocksChineseContainsAndCompactVariants()
    {
        var snapshot = CreateSnapshot(
            Rule(1, "测试禁词", "zh", AiPromptFilterMatchModes.Contains),
            Rule(2, "禁止短语", "zh", AiPromptFilterMatchModes.Compact));

        var direct = snapshot.Find("画面中包含测试禁词");
        var obfuscated = snapshot.Find("禁 止-短.语");

        Assert.Equal(1, direct?.RuleId);
        Assert.Equal(2, obfuscated?.RuleId);
    }

    [Fact]
    public void Matcher_UsesEnglishWordBoundariesWithoutSubstringFalsePositive()
    {
        var snapshot = CreateSnapshot(Rule(1, "ass", "en", AiPromptFilterMatchModes.Word));

        Assert.Null(snapshot.Find("a classroom assistant"));
        Assert.Equal(1, snapshot.Find("an ASS in the scene")?.RuleId);
        Assert.Equal(1, snapshot.Find("中文ASS内容")?.RuleId);
    }

    [Fact]
    public void Matcher_WordModeBlocksInsertedSeparatorsAndCombiningMarks()
    {
        var snapshot = CreateSnapshot(Rule(1, "rape", "en", AiPromptFilterMatchModes.Word));

        Assert.Equal(1, snapshot.Find("an r\u0336.a\u0336.p\u0336.e\u0336 scene")?.RuleId);
        Assert.Null(snapshot.Find("a grape illustration"));
        Assert.Null(snapshot.Find("a drapery study"));
    }

    [Fact]
    public void Matcher_MatchesEnglishPhraseAcrossNormalizedPunctuation()
    {
        var snapshot = CreateSnapshot(Rule(1, "blocked phrase", "en", AiPromptFilterMatchModes.Word));

        var result = snapshot.Find("a BLOCKED---PHRASE appears");

        Assert.Equal(1, result?.RuleId);
    }

    [Fact]
    public void Matcher_PrioritizesBlockingRuleOverAuditRule()
    {
        var snapshot = CreateSnapshot(
            Rule(1, "shared", "en", AiPromptFilterMatchModes.Word, AiPromptFilterActions.Audit, 5),
            Rule(2, "shared phrase", "en", AiPromptFilterMatchModes.Word, AiPromptFilterActions.Block, 1));

        var result = snapshot.Find("shared phrase");

        Assert.Equal(2, result?.RuleId);
        Assert.Equal(AiPromptFilterActions.Block, result?.Action);
    }

    [Theory]
    [InlineData("儿童性虐待", "sexual_minors", "儿 童-性.虐待")]
    [InlineData("性侵犯", "non_consensual_nudity", "性-侵 犯")]
    [InlineData("内脏外露", "graphic_violence", "内.脏 外-露")]
    [InlineData("自残教程", "self_harm", "自 残-教.程")]
    [InlineData("恐怖组织招募", "hate_extremism", "恐怖-组织 招.募")]
    [InlineData("枪械制作教程", "weapons_drugs", "枪 械-制.作 教程")]
    [InlineData("AI换脸裸照", "deepfake_privacy", "ＡＩ-换 脸.裸照")]
    public void Matcher_BlocksRepresentativeRulesFromSevenCategoryTaxonomy(
        string term,
        string category,
        string prompt)
    {
        var snapshot = CreateSnapshot(
            Rule(1, term, "zh", AiPromptFilterMatchModes.Compact, category: category));

        var result = snapshot.Find(prompt);

        Assert.Equal(category, result?.CategoryCode);
        Assert.Equal(AiPromptFilterActions.Block, result?.Action);
    }

    [Fact]
    public void CuratedRules_DoNotBlockExcludedBroadTerms()
    {
        var snapshot = CreateSnapshot(
            Rule(1, "儿童性虐待", "zh", AiPromptFilterMatchModes.Compact),
            Rule(2, "枪械制作教程", "zh", AiPromptFilterMatchModes.Compact),
            Rule(3, "AI换脸裸照", "zh", AiPromptFilterMatchModes.Compact),
            Rule(4, "恐怖组织招募", "zh", AiPromptFilterMatchModes.Compact));

        Assert.Null(snapshot.Find("儿童绘本插画"));
        Assert.Null(snapshot.Find("手持水枪的夏日场景"));
        Assert.Null(snapshot.Find("使用AI换脸滤镜制作头像"));
        Assert.Null(snapshot.Find("反恐怖主义公益海报"));
    }

    [Fact]
    public void Matcher_DoesNotMatchEmptyText()
    {
        var snapshot = CreateSnapshot(Rule(1, "blocked", "en", AiPromptFilterMatchModes.Word));

        Assert.Null(snapshot.Find(null));
        Assert.Null(snapshot.Find("   "));
    }

    private static AiPromptMatcherSnapshot CreateSnapshot(params AiPromptFilterRule[] rules)
    {
        return new AiPromptMatcherSnapshot(7, DateTime.UtcNow, rules);
    }

    private static AiPromptFilterRule Rule(
        long id,
        string term,
        string language,
        string matchMode,
        string action = AiPromptFilterActions.Block,
        int severity = 3,
        string category = "test")
    {
        return new AiPromptFilterRule(
            id,
            term,
            AiPromptTextNormalizer.NormalizeRuleTerm(term, matchMode),
            language,
            category,
            matchMode,
            action,
            severity);
    }
}
