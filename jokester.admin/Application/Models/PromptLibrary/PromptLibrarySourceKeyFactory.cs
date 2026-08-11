using System.Security.Cryptography;
using System.Text;

namespace jokester.admin.Application.Models.PromptLibrary;

public static class PromptLibrarySourceKeyFactory
{
    private const int MaxStoredSourceUrlLength = 1000;
    private const int MaxStoredTitleLength = 300;

    public static IReadOnlyList<PromptLibrarySourceKeyAssignment> CreateAssignments(
        IReadOnlyList<ParsedPromptReadmeItem> items,
        IReadOnlyList<ExistingPromptLibrarySourceKey>? existingItems = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return CreateAssignmentsCore(
            items.Select(item => new SourceKeyInput(
                item.StableId,
                item.ExternalNo,
                item.Title,
                item.PromptText,
                item.SourceUrl,
                item.SourcePosition)).ToArray(),
            existingItems);
    }

    public static IReadOnlyList<PromptLibrarySourceKeyAssignment> CreateAssignments(
        IReadOnlyList<PromptLibrarySourceItem> items,
        IReadOnlyList<ExistingPromptLibrarySourceKey>? existingItems = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return CreateAssignmentsCore(
            items.Select(item => new SourceKeyInput(
                item.StableId,
                item.ExternalNo,
                item.Title,
                item.PromptText,
                item.SourceUrl,
                item.SourcePosition)).ToArray(),
            existingItems);
    }

    private static IReadOnlyList<PromptLibrarySourceKeyAssignment> CreateAssignmentsCore(
        IReadOnlyList<SourceKeyInput> items,
        IReadOnlyList<ExistingPromptLibrarySourceKey>? existingItems)
    {
        var externalOccurrences = new Dictionary<int, int>();
        var pending = new PendingAssignment[items.Count];
        foreach (var index in Enumerable.Range(0, items.Count)
            .OrderBy(index => items[index].SourcePosition)
            .ThenBy(index => index))
        {
            var item = items[index];
            externalOccurrences.TryGetValue(item.ExternalNo, out var externalOccurrence);
            externalOccurrence++;
            externalOccurrences[item.ExternalNo] = externalOccurrence;

            var promptHash = Hash(item.PromptText.Trim());
            pending[index] = new PendingAssignment(
                index,
                item.SourcePosition,
                item.ExternalNo,
                externalOccurrence,
                BuildSourceIdentity(item.SourceUrl, promptHash),
                BuildContentFingerprint(promptHash, item.Title),
                BuildDirectSourceKey(item.StableId));
        }

        var existing = (existingItems ?? [])
            .Select(item => new ExistingAssignment(
                item.SourceKey,
                item.SourcePosition,
                item.ExternalNo,
                item.ExternalOccurrence,
                BuildSourceIdentity(item.SourceUrl, item.PromptHash),
                BuildContentFingerprint(item.PromptHash, item.Title),
                item.IsActive))
            .ToArray();
        var allKnownKeys = existing
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        var assignments = new PromptLibrarySourceKeyAssignment?[items.Count];

        var directKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var current in pending.Where(item => item.DirectSourceKey is not null))
        {
            var directKey = current.DirectSourceKey!;
            if (!directKeys.Add(directKey))
            {
                throw new InvalidDataException("Prompt source contains a duplicate stable ID.");
            }

            var existingMatch = existing.SingleOrDefault(item =>
                string.Equals(item.SourceKey, directKey, StringComparison.Ordinal));
            if (existingMatch is null && !allKnownKeys.Add(directKey))
            {
                throw new InvalidDataException("Prompt source stable ID conflicts with an existing source key.");
            }
            assignments[current.Index] = new PromptLibrarySourceKeyAssignment(
                existingMatch?.SourceKey ?? directKey,
                current.ExternalOccurrence,
                1);
        }

        foreach (var sourceGroup in pending
            .Where(item => assignments[item.Index] is null)
            .GroupBy(item => item.SourceIdentity, StringComparer.Ordinal))
        {
            var existingGroup = existing
                .Where(item => string.Equals(item.SourceIdentity, sourceGroup.Key, StringComparison.Ordinal))
                .ToArray();

            foreach (var fingerprintGroup in sourceGroup.GroupBy(item => item.ContentFingerprint, StringComparer.Ordinal))
            {
                var currentMatches = fingerprintGroup.OrderBy(item => item.SourcePosition).ToArray();
                var existingMatches = existingGroup
                    .Where(item => string.Equals(
                        item.ContentFingerprint,
                        fingerprintGroup.Key,
                        StringComparison.Ordinal))
                    .OrderByDescending(item => item.IsActive)
                    .ThenBy(item => item.ExternalOccurrence)
                    .ThenBy(item => item.SourcePosition)
                    .ThenBy(item => item.SourceKey, StringComparer.Ordinal)
                    .ToArray();
                var matchedCount = Math.Min(currentMatches.Length, existingMatches.Length);
                for (var matchIndex = 0; matchIndex < matchedCount; matchIndex++)
                {
                    var current = currentMatches[matchIndex];
                    assignments[current.Index] = new PromptLibrarySourceKeyAssignment(
                        existingMatches[matchIndex].SourceKey,
                        current.ExternalOccurrence,
                        matchIndex + 1);
                }
            }

            var unmatchedCurrent = sourceGroup
                .Where(item => assignments[item.Index] is null)
                .OrderBy(item => item.SourcePosition)
                .ToList();
            var matchedKeys = assignments
                .Where(assignment => assignment is not null)
                .Select(assignment => assignment!.SourceKey)
                .ToHashSet(StringComparer.Ordinal);
            var unmatchedExisting = existingGroup
                .Where(item => !matchedKeys.Contains(item.SourceKey))
                .ToList();

            MatchUnambiguousEdits(unmatchedCurrent, unmatchedExisting, assignments);

            foreach (var fingerprintGroup in unmatchedCurrent
                .Where(item => assignments[item.Index] is null)
                .GroupBy(item => item.ContentFingerprint, StringComparer.Ordinal))
            {
                var occurrence = 0;
                foreach (var current in fingerprintGroup.OrderBy(item => item.SourcePosition))
                {
                    occurrence++;
                    var nonce = occurrence;
                    string sourceKey;
                    do
                    {
                        sourceKey = Hash($"{current.SourceIdentity}\nvariant:{current.ContentFingerprint}\n{nonce}");
                        nonce++;
                    }
                    while (!allKnownKeys.Add(sourceKey));

                    assignments[current.Index] = new PromptLibrarySourceKeyAssignment(
                        sourceKey,
                        current.ExternalOccurrence,
                        occurrence);
                }
            }
        }

        return assignments.Select(assignment => assignment!).ToArray();
    }

    private static void MatchUnambiguousEdits(
        IReadOnlyList<PendingAssignment> currentItems,
        ICollection<ExistingAssignment> existingItems,
        PromptLibrarySourceKeyAssignment?[] assignments)
    {
        foreach (var current in currentItems)
        {
            var matches = existingItems
                .Where(existing => existing.ExternalNo == current.ExternalNo
                    && existing.ExternalOccurrence == current.ExternalOccurrence)
                .ToArray();
            var activeMatches = matches.Where(existing => existing.IsActive).ToArray();
            if (activeMatches.Length == 1)
            {
                matches = activeMatches;
            }
            if (matches.Length != 1)
            {
                continue;
            }

            assignments[current.Index] = new PromptLibrarySourceKeyAssignment(
                matches[0].SourceKey,
                current.ExternalOccurrence,
                1);
            existingItems.Remove(matches[0]);
        }

        var remainingCurrent = currentItems.Where(item => assignments[item.Index] is null).ToArray();
        if (remainingCurrent.Length == 1 && existingItems.Count == 1)
        {
            var existing = existingItems.Single();
            assignments[remainingCurrent[0].Index] = new PromptLibrarySourceKeyAssignment(
                existing.SourceKey,
                remainingCurrent[0].ExternalOccurrence,
                1);
            existingItems.Remove(existing);
        }
    }

    private static string BuildSourceIdentity(string? sourceUrl, string promptHash) =>
        string.IsNullOrWhiteSpace(sourceUrl)
            ? "prompt:" + promptHash.Trim().ToLowerInvariant()
            : "url:" + Limit(sourceUrl.Trim(), MaxStoredSourceUrlLength);

    private static string? BuildDirectSourceKey(string? stableId) =>
        string.IsNullOrWhiteSpace(stableId)
            ? null
            : Hash("external:" + stableId.Trim());

    private static string BuildContentFingerprint(string promptHash, string title) =>
        Hash($"{promptHash.Trim().ToLowerInvariant()}\n{Limit(title.Trim(), MaxStoredTitleLength)}");

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record PendingAssignment(
        int Index,
        int SourcePosition,
        int ExternalNo,
        int ExternalOccurrence,
        string SourceIdentity,
        string ContentFingerprint,
        string? DirectSourceKey);

    private sealed record SourceKeyInput(
        string? StableId,
        int ExternalNo,
        string Title,
        string PromptText,
        string? SourceUrl,
        int SourcePosition);

    private sealed record ExistingAssignment(
        string SourceKey,
        int SourcePosition,
        int ExternalNo,
        int ExternalOccurrence,
        string SourceIdentity,
        string ContentFingerprint,
        bool IsActive);
}

public sealed record ExistingPromptLibrarySourceKey(
    string SourceKey,
    string? SourceUrl,
    string PromptHash,
    string Title,
    int ExternalNo,
    int ExternalOccurrence,
    int SourcePosition,
    bool IsActive);

public sealed record PromptLibrarySourceKeyAssignment(
    string SourceKey,
    int ExternalOccurrence,
    int SourceOccurrence);
