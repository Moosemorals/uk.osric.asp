// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Markdown;

/// <summary>Which URLs the restricted Markdown renderer may turn into a link.</summary>
/// <remarks>
/// <para>
/// The assistant renders model output, and model output is influenced by the page source it reads,
/// so a link is a place a prompt injection could try to send its reader. Only a same-site target —
/// a relative URL or a fragment — and an absolute <c>http</c> or <c>https</c> URL are allowed.
/// Everything else is written as its text: <c>javascript:</c>, <c>data:</c>, <c>mailto:</c>,
/// <c>file:</c>, a protocol-relative <c>//host</c>, a backslash-escaped host and any other scheme.
/// </para>
/// <para>
/// This is a pure string rule rather than part of the renderer, so the policy is readable and
/// testable without a parser. It is not the page renderer's policy: a page author may write raw
/// HTML and is trusted with whatever a page contains.
/// </para>
/// </remarks>
public static class LinkPolicy {

    /// <summary>Returns whether a restricted renderer may make <paramref name="url"/> a link.</summary>
    /// <param name="url">The URL a Markdown link or image named. It is not normalised.</param>
    /// <returns>
    /// <see langword="true"/> for a same-site relative URL, a fragment, or an absolute
    /// <c>http</c>/<c>https</c> URL; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsAllowed(string? url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        string value = url.Trim();
        foreach (char character in value) {
            // A control character can end an attribute, and a browser treats a backslash as a
            // separator in some URL positions, so either would make the text say one thing and the
            // browser do another.
            if (char.IsControl(character) || character == '\\') {
                return false;
            }
        }

        if (value.StartsWith("//", StringComparison.Ordinal)) {
            return false;
        }

        // A rooted path is a same-site URL. It is checked before the absolute parse because .NET
        // reads "/about" as an absolute file URI, whose scheme would then be refused.
        if (value.StartsWith('/')) {
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute)) {
            return absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        // A colon before the first path, query or fragment separator is a scheme the URL parser did
        // not recognise; a colon after one belongs to a relative path and is harmless.
        int colon = value.IndexOf(':');
        if (colon < 0) {
            return true;
        }

        int separator = value.IndexOfAny(['/', '?', '#']);
        return separator >= 0 && separator < colon;
    }
}
