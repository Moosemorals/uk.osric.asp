// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Text;
using System.Text.Unicode;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using SiteText = uk.osric.asp.Core.Text.Utf8;

/// <summary>GETs a path on this site as the run's owner, and returns the response as the server sent it.</summary>
/// <remarks>
/// <para>
/// The counterpart to <c>fetch</c>, which is anonymous and reaches any host. A path is this site as
/// you; an absolute URL is <c>fetch</c>'s job. There is nothing to approve: it reaches nothing but this
/// site, writes nothing, and answers a question about the run's own work — the standing
/// <c>read_page</c> has.
/// </para>
/// <para>
/// The value is the transport's own answer — every header, the body's own bytes read as UTF-8, the
/// redirects it took — rather than `fetch`'s text projection. A body that is not UTF-8 comes back as
/// base64 rather than as replacement characters, so a run never mistakes mangled text for the page.
/// </para>
/// </remarks>
internal sealed class ViewTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "view",
        "GET a path on this site over HTTP and return the response exactly as the server produced it: "
        + "status, every response header, the body, and any redirects it took. Use a path such as "
        + "/hello or /pages?edit=/hello; it is fetched as the signed-in owner of this run, so a guarded "
        + "page renders for you and a redirect to /login is what a stranger sees. An absolute URL is "
        + "refused: use fetch for another host, which is anonymous. Read only. There is no cookie jar, "
        + "so page state starts empty and each call is a first visit; a path that streams will time "
        + "out. The body is text when it is UTF-8 and base64 when it is not, and a large body is "
        + "truncated at the tool limit with truncated set, and length is the byte length of what came back "
        + "(Content-Length in the headers is the server's own figure).",
        false,
        [new("path", ToolArgumentType.String, true)]) {
        Verb = "View",
    };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string path = values.RequiredString("path");

        if (!services.Site.TryResolve(path, out string url, out string? problem)) {
            // The model's other way to reach a page is fetch, so a refusal says which one to use.
            return services.Results.Failure($"{problem} Use fetch for a URL on another host.", $"{Spec.Verb} refused");
        }

        // Asked for at the moment of use and nowhere else: the secret is not on the tool's context,
        // whose record prints every property into a log scope.
        ViewToken? token = services.Views.ForRun(services.Context.RunId);
        if (token is null) {
            return services.Results.Failure(
                "This run holds no view token, so it cannot see the site.",
                $"{Spec.Verb} refused");
        }

        OutboundResponse response = await services.Fetcher.ViewAsync(url, token, cancellationToken);

        // A timeout or a stop surfaces as a cancelled token rather than a value, so the run's own
        // tool-timeout and cancellation handling decides what the transcript says.
        cancellationToken.ThrowIfCancellationRequested();

        switch (response.Failure) {
            case HttpFetchFailure.InvalidRequest:
                return services.Results.Failure("The path did not name a document on this site.", $"{Spec.Verb} refused");
            case HttpFetchFailure.Network:
                return services.Results.Failure("The host could not reach its own site; Site:Origin may not be where it listens.", "View failed: network");
            case HttpFetchFailure.Timeout:
                return services.Results.Failure("The view did not finish within its time limit.", "View failed: timeout");
            case HttpFetchFailure.Aborted:
                return services.Results.Failure("The view was cancelled.", "View cancelled");
            case HttpFetchFailure.None:
            case HttpFetchFailure.HttpError:
            case HttpFetchFailure.TooManyRedirects:
                break;
            default:
                return services.Results.Failure("The view did not produce a response.", "View failed");
        }

        int budget = (int)Math.Min(services.Budget.ToolResultBytes, int.MaxValue);
        bool truncated = response.Truncated;
        string? content = null;
        string? base64 = null;

        // The host writes every GET as UTF-8, so a body that is not valid UTF-8 is a body that is not
        // text: base64 keeps it exact where a decode would replace every bad byte with a question mark.
        if (Utf8.IsValid(response.Body)) {
            content = Encoding.UTF8.GetString(response.Body);
            if (SiteText.SizeInBytes(content) > budget) {
                content = SiteText.Clip(content, budget);
                truncated = true;
            }
        } else {
            base64 = Convert.ToBase64String(response.Body);
            if (SiteText.SizeInBytes(base64) > budget) {
                base64 = base64[..(budget / 4 * 4)];
                truncated = true;
            }
        }

        object data = new {
            path,
            status = response.Status,
            contentType = response.Headers.TryGetValue("Content-Type", out string? type) ? type : null,
            headers = response.Headers,
            content,
            base64,
            validUtf8 = base64 is null,
            length = response.Body.Length,
            truncated,
            redirects = response.Redirects.Select(hop => new { status = hop.Status, location = hop.Location }).ToArray(),
        };

        return services.Results.Success(data, $"Viewed {path}: HTTP {response.Status}");
    }

    // A cut that splits a surrogate pair would serialise as a lone half, so the pair is left whole.
}
