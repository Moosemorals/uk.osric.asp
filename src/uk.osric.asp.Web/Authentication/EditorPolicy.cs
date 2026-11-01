// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>Names shared by the editor's authorisation policy and its cookies.</summary>
internal static class EditorPolicy {

    /// <summary>The policy that protects the editor endpoints.</summary>
    public const string Name = "Editor";

    /// <summary>The claim that carries the account's revocation stamp.</summary>
    public const string SecurityStampClaim = "uk.osric.asp.security_stamp";
}
