// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Sdk;
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
            return services.Results.Success(new { type = TypeData(match.Type) }, $"Read {match.Type.Display}");
        }

        if (lookup.Candidates.Count > 0) {
            string names = string.Join(", ", lookup.Candidates
                .Take(5)
                .Select(candidate => candidate.Type.Display));
            return services.Results.Failure(
                $"`{name}` names more than one type: {names}. Ask with the full name.",
                "Reference refused");
        }

        if (lookup.Elsewhere is { } elsewhere) {
            return services.Results.Failure(
                $"`{name}` is in {elsewhere.Assembly.Info.AssemblyName}; the reference reads the page contract "
                + "(uk.osric.asp.Sdk) only, because that is what a page may use.",
                "Reference refused");
        }

        return services.Results.Failure(
            $"No type named `{name}` in the page contract. Use `reference query=` to search for it.",
            "Reference refused");
    }

    private static object TypeData(ReferenceType type) => new {
        id = type.Id,
        name = type.Display,
        kind = type.Kind,
        declaration = type.Declaration.Text,
        summary = type.Summary.Text,
        remarks = type.Remarks.Text,
        typeParameters = Parameters(type.TypeParameters),
        members = type.Members.Select(MemberData).ToArray(),
    };

    private static object MemberData(ReferenceMember member) => new {
        id = member.Id,
        kind = member.Kind,
        display = member.Display,
        declaration = member.Declaration.Text,
        summary = member.Summary.Text,
        remarks = member.Remarks.Text,
        typeParameters = Parameters(member.TypeParameters),
        parameters = Parameters(member.Parameters),
        returns = member.Returns.Text,
        exceptions = member.Exceptions.Select(exception => new {
            type = exception.Name,
            text = exception.Doc.Text,
        }).ToArray(),
    };

    private static object[] Parameters(IReadOnlyList<ReferenceParameter> parameters) =>
        [.. parameters.Select(parameter => new {
            name = parameter.Name,
            text = parameter.Doc.Text,
        })];
}
