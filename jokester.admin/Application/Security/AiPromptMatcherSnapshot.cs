using System.Globalization;
using jokester.admin.Application.Models.AiPromptFilter;

namespace jokester.admin.Application.Security;

public sealed class AiPromptMatcherSnapshot
{
    private readonly AhoCorasickMatcher? _containsMatcher;
    private readonly AhoCorasickMatcher? _wordMatcher;
    private readonly AhoCorasickMatcher? _compactMatcher;

    public AiPromptMatcherSnapshot(long revision, DateTime loadedAtUtc, IReadOnlyList<AiPromptFilterRule> rules)
    {
        Revision = revision;
        LoadedAtUtc = loadedAtUtc;
        RuleCount = rules.Count;

        var containsRules = rules
            .Where(x => x.MatchMode == AiPromptFilterMatchModes.Contains)
            .Where(x => !string.IsNullOrEmpty(x.NormalizedTerm))
            .ToArray();
        var wordRules = rules
            .Where(x => x.MatchMode == AiPromptFilterMatchModes.Word)
            .Select(x => x with
            {
                NormalizedTerm = AiPromptTextNormalizer.NormalizeRuleTerm(x.Term, x.MatchMode)
            })
            .Where(x => !string.IsNullOrEmpty(x.NormalizedTerm))
            .ToArray();
        var compactRules = rules
            .Where(x => x.MatchMode == AiPromptFilterMatchModes.Compact)
            .Where(x => !string.IsNullOrEmpty(x.NormalizedTerm))
            .ToArray();

        _containsMatcher = containsRules.Length == 0 ? null : new AhoCorasickMatcher(containsRules);
        _wordMatcher = wordRules.Length == 0 ? null : new AhoCorasickMatcher(wordRules);
        _compactMatcher = compactRules.Length == 0 ? null : new AhoCorasickMatcher(compactRules);
    }

    public long Revision { get; }

    public DateTime LoadedAtUtc { get; }

    public int RuleCount { get; }

    public AiPromptFilterMatch? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var matches = new List<AiPromptFilterRule>();
        var standardText = AiPromptTextNormalizer.Normalize(text);
        if (_containsMatcher is not null && standardText.Length > 0)
        {
            matches.AddRange(_containsMatcher.Find(standardText));
        }

        var wordText = AiPromptTextNormalizer.NormalizeIgnoringSeparators(text);
        if (_wordMatcher is not null && wordText.Value.Length > 0)
        {
            matches.AddRange(_wordMatcher.Find(
                wordText.Value,
                (start, rule) => HasWordBoundaries(wordText, start, rule.NormalizedTerm.Length, rule.LanguageCode)));
        }

        var compactText = AiPromptTextNormalizer.Normalize(text, AiPromptFilterMatchModes.Compact);
        if (_compactMatcher is not null && compactText.Length > 0)
        {
            matches.AddRange(_compactMatcher.Find(compactText));
        }

        var selected = matches
            .OrderByDescending(x => x.Action == AiPromptFilterActions.Block)
            .ThenByDescending(x => x.Severity)
            .ThenByDescending(x => x.NormalizedTerm.Length)
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        return selected is null
            ? null
            : new AiPromptFilterMatch(
                selected.Id,
                selected.Term,
                selected.LanguageCode,
                selected.CategoryCode,
                selected.MatchMode,
                selected.Action,
                selected.Severity);
    }

    private sealed class AhoCorasickMatcher
    {
        private readonly List<Node> _nodes = [new Node()];

        public AhoCorasickMatcher(IEnumerable<AiPromptFilterRule> rules)
        {
            foreach (var rule in rules)
            {
                Add(rule);
            }

            BuildFailureLinks();
        }

        public IReadOnlyList<AiPromptFilterRule> Find(
            string text,
            Func<int, AiPromptFilterRule, bool>? acceptMatch = null)
        {
            var matches = new List<AiPromptFilterRule>();
            var state = 0;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                while (state != 0 && !_nodes[state].Transitions.ContainsKey(current))
                {
                    state = _nodes[state].Failure;
                }

                if (_nodes[state].Transitions.TryGetValue(current, out var next))
                {
                    state = next;
                }

                foreach (var rule in _nodes[state].Outputs)
                {
                    var start = index - rule.NormalizedTerm.Length + 1;
                    if (start < 0)
                    {
                        continue;
                    }

                    if (acceptMatch is not null && !acceptMatch(start, rule))
                    {
                        continue;
                    }

                    matches.Add(rule);
                }
            }

            return matches;
        }

        private void Add(AiPromptFilterRule rule)
        {
            var state = 0;
            foreach (var current in rule.NormalizedTerm)
            {
                if (!_nodes[state].Transitions.TryGetValue(current, out var next))
                {
                    next = _nodes.Count;
                    _nodes[state].Transitions[current] = next;
                    _nodes.Add(new Node());
                }

                state = next;
            }

            _nodes[state].Outputs.Add(rule);
        }

        private void BuildFailureLinks()
        {
            var queue = new Queue<int>();
            foreach (var child in _nodes[0].Transitions.Values)
            {
                queue.Enqueue(child);
            }

            while (queue.TryDequeue(out var state))
            {
                foreach (var transition in _nodes[state].Transitions)
                {
                    var current = transition.Key;
                    var child = transition.Value;
                    var failure = _nodes[state].Failure;

                    while (failure != 0 && !_nodes[failure].Transitions.ContainsKey(current))
                    {
                        failure = _nodes[failure].Failure;
                    }

                    if (_nodes[failure].Transitions.TryGetValue(current, out var fallback) && fallback != child)
                    {
                        _nodes[child].Failure = fallback;
                    }

                    _nodes[child].Outputs.AddRange(_nodes[_nodes[child].Failure].Outputs);
                    queue.Enqueue(child);
                }
            }
        }

        private sealed class Node
        {
            public Dictionary<char, int> Transitions { get; } = [];

            public List<AiPromptFilterRule> Outputs { get; } = [];

            public int Failure { get; set; }
        }
    }

    private static bool HasWordBoundaries(
        AiPromptSeparatorInsensitiveText text,
        int start,
        int length,
        string languageCode)
    {
        Func<char, bool> isWordCharacter = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)
            ? IsAsciiWordCharacter
            : IsUnicodeWordCharacter;
        var leftBoundary = start == 0
            || text.SeparatorBefore[start]
            || !isWordCharacter(text.Value[start - 1]);
        var end = start + length;
        var rightBoundary = end >= text.Value.Length
            || text.SeparatorBefore[end]
            || !isWordCharacter(text.Value[end]);
        return leftBoundary && rightBoundary;
    }

    private static bool IsAsciiWordCharacter(char value) => char.IsAsciiLetterOrDigit(value);

    private static bool IsUnicodeWordCharacter(char value) => char.IsLetterOrDigit(value)
        || CharUnicodeInfo.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}
