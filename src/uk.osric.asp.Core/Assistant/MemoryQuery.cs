// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Turns a message into the FTS5 expression a recall matches with.</summary>
/// <remarks>
/// The expression is built from the owner's own words, so it is sanitised by construction: a term is
/// letters and digits only, quoted, with a trailing <c>*</c>. Unquoted user text would be FTS5 syntax,
/// and a stray <c>OR</c>, <c>NEAR(</c> or unbalanced quote would be a syntax error rather than a
/// search. Quoting also means a term that happens to be an FTS5 operator is a word like any other.
/// </remarks>
public static class MemoryQuery {

    /// <summary>The shortest term worth searching for.</summary>
    public const int MinimumTermLength = 2;

    // Words that appear in almost every message, so matching on them would rank a memory that shares
    // one as relevant. Kept deliberately short: a stopword list is a bias, and a missed term is a
    // missed memory.
    private static readonly HashSet<string> s_stopwords = new(StringComparer.Ordinal) {
        "about", "after", "again", "all", "also", "am", "an", "and", "any", "are", "as", "at", "be",
        "because", "been", "before", "being", "but", "by", "can", "could", "did", "do", "does", "doing",
        "done", "for", "from", "get", "had", "has", "have", "here", "her", "him", "his", "how", "if",
        "in", "into", "is", "it", "its", "just", "like", "make", "many", "may", "me", "more", "most",
        "much", "must", "my", "need", "no", "not", "now", "of", "off", "on", "once", "only", "or",
        "other", "our", "out", "over", "own", "same", "she", "should", "so", "some", "such", "than",
        "that", "the", "their", "them", "then", "there", "these", "they", "this", "those", "through",
        "to", "too", "under", "until", "very", "want", "was", "we", "well", "were", "what", "when",
        "where", "which", "while", "who", "why", "will", "with", "would", "you", "your",
    };

    /// <summary>The quoted prefix terms a message is searched with.</summary>
    /// <param name="message">The newest user message.</param>
    /// <param name="maximum">The most terms to keep, matching the configured query-term cap.</param>
    /// <returns>A space-separated FTS5 expression, or the empty string when the message has no
    /// usable term.</returns>
    public static string Terms(string message, int maximum) {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        List<string> terms = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string word in Words(message)) {
            string term = word.ToLowerInvariant();
            if (term.Length < MinimumTermLength || s_stopwords.Contains(term) || !seen.Add(term)) {
                continue;
            }

            terms.Add(term);
            if (terms.Count == maximum) {
                break;
            }
        }

        return string.Join(' ', terms.Select(term => $"\"{term}\"*"));
    }

    // A term is a run of letters, digits and underscores: every FTS5 metacharacter is a separator, so
    // nothing that could be read as syntax survives to the query.
    private static IEnumerable<string> Words(string text) {
        int start = -1;
        for (int index = 0; index <= text.Length; index++) {
            bool isWord = index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_');
            if (isWord) {
                start = start < 0 ? index : start;
                continue;
            }

            if (start >= 0) {
                yield return text[start..index];
                start = -1;
            }
        }
    }
}
