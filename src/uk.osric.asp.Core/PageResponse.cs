// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>What a page asked the response to be.</summary>
/// <remarks>
/// The shell applies this after the handler has returned, which is why the body is buffered: a page
/// that throws contributes no status, no headers and no output.
/// </remarks>
/// <param name="Status">The status code, normally <c>200</c>.</param>
/// <param name="ContentType">The content type, normally <c>text/html; charset=utf-8</c>.</param>
/// <param name="Headers">The extra response headers the page set.</param>
/// <param name="Location">The redirect target, or <see langword="null"/> when the page did not redirect.</param>
public sealed record PageResponse(
    int Status,
    string ContentType,
    IReadOnlyDictionary<string, string> Headers,
    string? Location) {

    /// <summary>The response a page gets when it asks for nothing in particular.</summary>
    public static PageResponse Default { get; } = new(
        200,
        "text/html; charset=utf-8",
        new Dictionary<string, string>(),
        null);
}
