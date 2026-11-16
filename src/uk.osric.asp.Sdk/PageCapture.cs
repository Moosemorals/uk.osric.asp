// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A rendered page's body beside the frame it contributes.</summary>
/// <remarks>
/// <para>
/// A page rendered inside another page yields this value: its whole body, wrappers and all, plus the
/// title, head, sheets and script permission of the outermost layer. A wrapper receives the layer
/// inside it as <see cref="IPageLayout.Content"/>, and a page that does not want to be wrapped can
/// ask for the same value with <see cref="IPageComposition.CaptureAsync"/>.
/// </para>
/// <para>
/// It is a value, not a capability: the captured page's response stays withheld, so its status,
/// headers and content type never reach the caller. A page that would have redirected refuses
/// instead, because a capture has no response to redirect.
/// </para>
/// </remarks>
public sealed record PageCapture : PageFrame {

    /// <summary>Pairs a body with the frame of the layer that wrote it.</summary>
    /// <param name="body">The outermost layer's body.</param>
    /// <param name="frame">The outermost layer's frame.</param>
    /// <exception cref="ArgumentNullException">The body or the frame is null.</exception>
    public PageCapture(string body, PageFrame frame)
        : base(Checked(frame)) {
        ArgumentNullException.ThrowIfNull(body);

        Body = body;
    }

    /// <summary>The outermost layer's body.</summary>
    public string Body { get; }

    private static PageFrame Checked(PageFrame frame) {
        ArgumentNullException.ThrowIfNull(frame);

        return frame;
    }
}
