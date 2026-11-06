// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>How an approval a run was waiting on was resolved.</summary>
internal enum ApprovalResolutionResult {
    Approved,
    Denied,
    NotFound,
    Conflict,
}
