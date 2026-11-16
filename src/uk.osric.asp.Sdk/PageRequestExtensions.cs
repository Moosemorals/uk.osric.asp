// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Copies of a <see cref="PageRequest"/> with a changed query.</summary>
/// <remarks>
/// <para>
/// A composed page runs against a request, so the request is the page's input. These are the
/// copy-and-change helpers: the record keeps its shape and its own surface stays the values it
/// carries, while a caller shapes the query an embed or a wrapper sees without building a second
/// type for it.
/// </para>
/// <para>
/// <see cref="PageRequest.Query"/> and <see cref="PageRequest.QueryValues"/> are rebuilt together, so
/// the two views of the same query cannot disagree. The order the names were added in is kept, and a
/// repeated name keeps the order of its values.
/// </para>
/// </remarks>
public static class PageRequestExtensions {

    /// <summary>Replaces every value of one query name with a single value.</summary>
    /// <param name="request">The request to copy.</param>
    /// <param name="name">The query name.</param>
    /// <param name="value">The value to give it; it may not be <see langword="null"/>.</param>
    /// <returns>A copy with the name's values replaced, in the position the name already had.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PageRequest WithQuery(this PageRequest request, string name, string value) {
        CheckName(request, name);
        ArgumentNullException.ThrowIfNull(value);

        List<KeyValuePair<string, string>> values = [];
        bool replaced = false;

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in request.QueryValues) {
            if (!string.Equals(entry.Key, name, StringComparison.Ordinal)) {
                AddAll(values, entry);
                continue;
            }

            if (!replaced) {
                values.Add(new(name, value));
                replaced = true;
            }
        }

        if (!replaced) {
            values.Add(new(name, value));
        }

        return Rebuild(request, values);
    }

    /// <summary>Replaces the whole query with the pairs given.</summary>
    /// <param name="request">The request to copy.</param>
    /// <param name="query">The query, in the order the caller wants it, repeated names included.</param>
    /// <returns>A copy whose query is exactly <paramref name="query"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">A name is blank, or a value is null.</exception>
    public static PageRequest WithQuery(this PageRequest request, IReadOnlyList<KeyValuePair<string, string>> query) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);

        List<KeyValuePair<string, string>> values = [];

        foreach (KeyValuePair<string, string> pair in query) {
            if (string.IsNullOrWhiteSpace(pair.Key)) {
                throw new ArgumentException("A query name is required.", nameof(query));
            }

            ArgumentNullException.ThrowIfNull(pair.Value, nameof(query));

            values.Add(pair);
        }

        return Rebuild(request, values);
    }

    /// <summary>Adds a value beside the ones the query name already has.</summary>
    /// <param name="request">The request to copy.</param>
    /// <param name="name">The query name.</param>
    /// <param name="value">The value to add; it may not be <see langword="null"/>.</param>
    /// <returns>A copy with the value added after the name's existing values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PageRequest WithAnotherQuery(this PageRequest request, string name, string value) {
        CheckName(request, name);
        ArgumentNullException.ThrowIfNull(value);

        List<KeyValuePair<string, string>> values = [];

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in request.QueryValues) {
            AddAll(values, entry);
        }

        values.Add(new(name, value));

        return Rebuild(request, values);
    }

    /// <summary>Drops one query name and every value it carried.</summary>
    /// <param name="request">The request to copy.</param>
    /// <param name="name">The query name to drop.</param>
    /// <returns>A copy without the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PageRequest WithoutQuery(this PageRequest request, string name) {
        CheckName(request, name);

        List<KeyValuePair<string, string>> values = [];

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in request.QueryValues) {
            if (!string.Equals(entry.Key, name, StringComparison.Ordinal)) {
                AddAll(values, entry);
            }
        }

        return Rebuild(request, values);
    }

    /// <summary>Drops the whole query.</summary>
    /// <param name="request">The request to copy.</param>
    /// <returns>A copy with no query.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static PageRequest WithoutQuery(this PageRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        return Rebuild(request, []);
    }

    private static void CheckName(PageRequest request, string name) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    private static void AddAll(List<KeyValuePair<string, string>> values, KeyValuePair<string, IReadOnlyList<string>> entry) {
        foreach (string value in entry.Value) {
            values.Add(new(entry.Key, value));
        }
    }

    // One builder for both views: the collapsed query keeps the first value of each name, and the
    // ordered view keeps every value, so the two cannot be built from different sequences.
    private static PageRequest Rebuild(PageRequest request, List<KeyValuePair<string, string>> values) {
        Dictionary<string, string> query = [with(StringComparer.Ordinal)];
        Dictionary<string, List<string>> grouped = [with(StringComparer.Ordinal)];
        List<string> order = [];

        foreach (KeyValuePair<string, string> pair in values) {
            if (!grouped.TryGetValue(pair.Key, out List<string>? list)) {
                list = [];
                grouped.Add(pair.Key, list);
                order.Add(pair.Key);
            }

            list.Add(pair.Value);
            _ = query.TryAdd(pair.Key, pair.Value);
        }

        Dictionary<string, IReadOnlyList<string>> every = [with(StringComparer.Ordinal)];

        foreach (string name in order) {
            every.Add(name, grouped[name]);
        }

        return request with { Query = query, QueryValues = every };
    }
}
