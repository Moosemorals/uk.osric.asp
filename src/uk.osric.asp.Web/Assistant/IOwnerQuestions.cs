// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>The seam a tool asks the owner through, supplied by the run that calls it.</summary>
/// <remarks>
/// The tool owns the schema and the result; the run owns the wait, because the wait is the run's
/// lifecycle rather than the tool's. A tool that does not wait never reaches this.
/// </remarks>
internal interface IOwnerQuestions {

    /// <summary>Stops the run until the owner answers, and returns what they gave.</summary>
    /// <param name="questions">The validated questions, in the order they are asked.</param>
    /// <param name="cancellationToken">The run's token, which is what a stop or a restart cancels.</param>
    /// <returns>The answers, or null when the run could not be stopped for the question.</returns>
    Task<OwnerAnswerSet?> AskAsync(OwnerQuestionSet questions, CancellationToken cancellationToken);
}
