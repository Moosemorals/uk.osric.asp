// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Xml.Linq;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Documentation;

/// <summary>Reads the page contract's own documentation: one type, or a search of the corpus.</summary>
/// <remarks>
/// The contract only. The page API admits <c>uk.osric.asp.Sdk</c> and a slice of the BCL, so
/// documenting the host's own assemblies to the model would invite code the compiler refuses. The
/// human page still covers all four.
/// </remarks>
internal sealed class ReferenceTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "reference",
        "Read the page contract's own documentation: a type's summary, remarks and every member with its "
        + "signature, parameters and returns, or search it by name and summary. Pass exactly one of `type` "
        + "or `query`: `type` when you can name the type, `query` when you cannot. It covers "
        + "`uk.osric.asp.Sdk` only, which is what a page may use; pass `limit` (1-500, default 50) with a "
        + "query to bound the matches.",
        false,
        [
            new("type", ToolArgumentType.String, false),
            new("query", ToolArgumentType.String, false),
            new("limit", ToolArgumentType.Integer, false),
        ]) { Verb = "Reference" };

    public override Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string? named = values.String("type");
        string? query = values.String("query");
        bool hasType = !string.IsNullOrWhiteSpace(named);
        bool hasQuery = !string.IsNullOrWhiteSpace(query);

        if (hasType == hasQuery) {
            return Task.FromResult(services.Results.Failure(
                "Give exactly one of `type` or `query`: `type` is a type name, `query` searches when you do not know it.",
                "Reference refused"));
        }

        if (hasType) {
            return Task.FromResult(Type(named!, services));
        }

        if (!PageToolData.TryReadLimit(values, services.Options, out int limit)) {
            return Task.FromResult(services.Results.Failure(
                PageToolData.LimitRefusal(services.Options),
                "Reference refused"));
        }

        if (services.Reference.Contract is null) {
            return Task.FromResult(services.Results.Failure(
                "This build carries no page-contract documentation.",
                "Reference refused"));
        }

        (IReadOnlyList<ReferenceSearchMatch> matches, bool truncated) = services.Reference.Search(query!, limit);
        return Task.FromResult(services.Results.Success(new {
            query,
            matches = matches.Select(match => new {
                type = match.Type,
                member = match.Member,
                kind = match.Kind,
                summary = match.Summary,
            }).ToArray(),
            truncated,
        }, $"Searched the page contract ({matches.Count} found)"));
    }

    private static ToolExecutionResult Type(string name, ToolServices services) {
        ReferenceTypeLookup lookup = services.Reference.FindType(name);

        if (lookup.Match is { } match) {
            return services.Results.Success(new { type = TypeData(match.Type) }, $"Read {match.Type.Attribute("display")?.Value}");
        }

        if (lookup.Candidates.Count > 0) {
            string names = string.Join(", ", lookup.Candidates
                .Take(5)
                .Select(candidate => candidate.Type.Attribute("display")?.Value)
                .OfType<string>());
            return services.Results.Failure(
                $"`{name}` names more than one type: {names}. Ask with the full name.",
                "Reference refused");
        }

        if (lookup.Elsewhere is { } elsewhere) {
            return services.Results.Failure(
                $"`{name}` is in {elsewhere.Info.AssemblyName}; the reference reads the page contract "
                + "(uk.osric.asp.Sdk) only, because that is what a page may use.",
                "Reference refused");
        }

        return services.Results.Failure(
            $"No type named `{name}` in the page contract. Use `reference query=` to search for it.",
            "Reference refused");
    }

    private static object TypeData(XElement type) => new {
        id = (string?)type.Attribute("id"),
        name = (string?)type.Attribute("display"),
        summary = Text(type, "summary"),
        remarks = Text(type, "remarks"),
        typeParameters = Parameters(type, "typeparam"),
        members = type.Elements("member").Select(MemberData).ToArray(),
    };

    private static object MemberData(XElement member) => new {
        id = (string?)member.Attribute("id"),
        kind = (string?)member.Attribute("kind"),
        display = (string?)member.Attribute("display"),
        summary = Text(member, "summary"),
        remarks = Text(member, "remarks"),
        typeParameters = Parameters(member, "typeparam"),
        parameters = Parameters(member, "param"),
        returns = Text(member, "returns"),
        exceptions = member.Elements("exception").Select(exception => new {
            type = (string?)exception.Attribute("text") ?? (string?)exception.Attribute("cref"),
            text = ReferenceText.Flatten(exception),
        }).ToArray(),
    };

    private static string Text(XElement element, string name) =>
        element.Element(name) is { } block ? ReferenceText.Flatten(block) : string.Empty;

    private static object[] Parameters(XElement element, string name) =>
        [.. element.Elements(name).Select(parameter => new {
            name = (string?)parameter.Attribute("name"),
            text = ReferenceText.Flatten(parameter),
        })];
}
