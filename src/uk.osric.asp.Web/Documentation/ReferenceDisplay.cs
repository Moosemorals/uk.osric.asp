// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Globalization;

/// <summary>The string rules that turn a documentation id into a name, an anchor or a link.</summary>
/// <remarks>
/// Roslyn's member ids are the only identity the XML carries, and they are not a representation a
/// reader wants: a parameter list names <c>System.Collections.Generic.IReadOnlyList{System.String}</c>
/// and an explicit implementation has <c>#</c> where a dot belongs. Every rule here is a pure function
/// over the id, so the corpus and the tool can be tested without a document.
/// </remarks>
internal static class ReferenceDisplay {

    /// <summary>Where the API browser keeps a member.</summary>
    public const string LearnBase = "https://learn.microsoft.com/en-us/dotnet/api/";

    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal) {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int16"] = "short",
        ["System.Int32"] = "int",
        ["System.Int64"] = "long",
        ["System.UInt16"] = "ushort",
        ["System.UInt32"] = "uint",
        ["System.UInt64"] = "ulong",
        ["System.IntPtr"] = "nint",
        ["System.UIntPtr"] = "nuint",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void",
    };

    /// <summary>The kind letter at the front of an id.</summary>
    /// <param name="id">A documentation id.</param>
    public static char Kind(string id) {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return id.Length >= 2 && id[1] == ':'
            ? id[0]
            : throw new ArgumentException($"'{id}' is not a documentation id.", nameof(id));
    }

    /// <summary>The id without its kind prefix.</summary>
    /// <param name="id">A documentation id.</param>
    public static string Body(string id) {
        _ = Kind(id);
        return id[2..];
    }

    /// <summary>The id without its parameter list or conversion return type.</summary>
    /// <param name="id">A documentation id.</param>
    public static string WithoutParameters(string id) {
        string body = Body(id);
        int open = body.IndexOf('(');
        if (open >= 0) {
            body = body[..open];
        }

        int tilde = body.IndexOf('~');
        return tilde < 0 ? body : body[..tilde];
    }

    /// <summary>The type names in the id's parameter list, in order.</summary>
    /// <param name="id">A documentation id.</param>
    public static IReadOnlyList<string> Parameters(string id) {
        string body = Body(id);
        int open = body.IndexOf('(');
        int close = body.LastIndexOf(')');
        if (open < 0 || close <= open) {
            return [];
        }

        string inside = body[(open + 1)..close];
        if (inside.Length == 0) {
            return [];
        }

        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int index = 0; index < inside.Length; index++) {
            switch (inside[index]) {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(inside[start..index]);
                    start = index + 1;
                    break;
            }
        }

        parts.Add(inside[start..]);
        return parts;
    }

    /// <summary>The type a member belongs to, or null for a type's own id.</summary>
    /// <param name="id">A documentation id.</param>
    public static string? Owner(string id) {
        if (Kind(id) == 'T') {
            return null;
        }

        string body = WithoutParameters(id);
        int dot = body.LastIndexOf('.');
        return dot < 0 ? null : "T:" + body[..dot];
    }

    /// <summary>The id without the assembly's root namespace.</summary>
    /// <param name="id">A documentation id.</param>
    /// <param name="root">The assembly name, which is its root namespace.</param>
    public static string Relative(string id, string root) {
        string name = WithoutParameters(id);
        return root.Length > 0 && name.StartsWith(root + ".", StringComparison.Ordinal)
            ? name[(root.Length + 1)..]
            : name;
    }

    /// <summary>A type as a reader sees it, with its documented type parameters.</summary>
    /// <param name="id">A type's id.</param>
    /// <param name="root">The assembly's root namespace.</param>
    /// <param name="typeParameters">The names from its <c>typeparam</c> elements, in order.</param>
    public static string TypeDisplay(string id, string root, IReadOnlyList<string> typeParameters) =>
        TypeName(Relative(id, root), typeParameters);

    /// <summary>A member's signature: its name, type parameters and shortened parameter list.</summary>
    /// <param name="id">A member's id.</param>
    /// <param name="root">The assembly's root namespace.</param>
    /// <param name="typeParameters">The declaring type's type parameter names.</param>
    /// <param name="methodTypeParameters">The member's own type parameter names.</param>
    /// <param name="parameterNames">The names from its <c>param</c> elements, in order.</param>
    public static string MemberDisplay(
        string id,
        string root,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodTypeParameters,
        IReadOnlyList<string> parameterNames) {
        char kind = Kind(id);
        string relative = Relative(id, root);
        int dot = relative.LastIndexOf('.');
        string owner = dot < 0 ? string.Empty : relative[..dot];
        string member = dot < 0 ? relative : relative[(dot + 1)..];
        string name = MemberNameText(member, owner, methodTypeParameters);

        IReadOnlyList<string> parameters = Parameters(id);
        bool showParentheses = kind == 'M' || parameters.Count > 0;
        if (!showParentheses) {
            return name;
        }

        bool namesFit = parameterNames.Count == parameters.Count;
        string joined = string.Join(", ", parameters.Select((parameter, index) => {
            string display = DisplayType(parameter, root, typeParameters, methodTypeParameters);
            return namesFit && !string.IsNullOrEmpty(parameterNames[index])
                ? display + " " + parameterNames[index]
                : display;
        }));

        return name + "(" + joined + ")";
    }

    /// <summary>A member's name alone, for a sentence that links to it.</summary>
    /// <param name="id">A member's id.</param>
    /// <param name="root">The assembly's root namespace.</param>
    /// <param name="methodTypeParameters">The member's own type parameter names.</param>
    public static string MemberName(string id, string root, IReadOnlyList<string> methodTypeParameters) {
        string relative = Relative(id, root);
        int dot = relative.LastIndexOf('.');
        return MemberText(dot < 0 ? relative : relative[(dot + 1)..], methodTypeParameters);
    }

    /// <summary>The anchor a type or member is addressed by.</summary>
    /// <param name="id">A documentation id.</param>
    /// <param name="root">The assembly's root namespace.</param>
    /// <param name="typeParameters">The declaring type's type parameter names.</param>
    /// <param name="methodTypeParameters">The member's own type parameter names.</param>
    public static string Anchor(
        string id,
        string root,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodTypeParameters) {
        char kind = Kind(id);
        string relative = Relative(id, root);
        if (kind == 'T') {
            return "t-" + SlugType(relative);
        }

        int dot = relative.LastIndexOf('.');
        string owner = dot < 0 ? string.Empty : relative[..dot];
        string member = dot < 0 ? relative : relative[(dot + 1)..];
        string arity = string.Empty;
        int tick = member.IndexOf('`');
        if (tick >= 0) {
            int digits = tick;
            while (digits < member.Length && member[digits] == '`') {
                digits++;
            }

            arity = "-g" + member[digits..];
            member = member[..tick];
        }

        member = member is "#ctor" or "#cctor"
            ? member[1..]
            : member.Replace('#', '.');
        string anchor = "m-" + SlugType(owner) + "." + SlugText(member) + arity;
        foreach (string parameter in Parameters(id)) {
            anchor += "-" + Slug(DisplayType(parameter, root, typeParameters, methodTypeParameters));
        }

        return anchor;
    }

    /// <summary>The API browser's page for a platform member, or null when it cannot be computed.</summary>
    /// <param name="id">An id the corpus does not hold.</param>
    public static string? LearnUrl(string id) {
        if (id.Length < 3) {
            return null;
        }

        char kind = Kind(id);
        string[] segments = WithoutParameters(id).Split('.');
        if (segments.Length == 0 || segments[0] is not ("System" or "Microsoft")) {
            return null;
        }

        if (kind == 'T') {
            return LearnBase + Arity(segments);
        }

        if (segments.Length < 2) {
            return null;
        }

        string[] owner = segments[..^1];
        string member = segments[^1];
        if (member.Contains('#')
            && !string.Equals(member, "#ctor", StringComparison.Ordinal)
            && !string.Equals(member, "#cctor", StringComparison.Ordinal)) {
            return null;
        }

        if (kind is 'F' or 'E'
            || string.Equals(member, "#ctor", StringComparison.Ordinal)
            || string.Equals(member, "#cctor", StringComparison.Ordinal)) {
            return LearnBase + Arity(owner);
        }

        int tick = member.IndexOf('`');
        if (tick >= 0) {
            member = member[..tick];
        }

        return LearnBase + Arity(owner) + "." + member.ToLowerInvariant();
    }

    /// <summary>An unresolved id as a short phrase, for a link's text or a code span.</summary>
    /// <param name="id">A documentation id.</param>
    public static string CrefText(string id) {
        char kind = Kind(id);
        string[] segments = WithoutParameters(id).Split('.');
        if (kind == 'T') {
            return SegmentName(segments[^1], []);
        }

        string member = segments[^1];
        if (string.Equals(member, "#ctor", StringComparison.Ordinal)
            || string.Equals(member, "#cctor", StringComparison.Ordinal)) {
            return segments.Length > 1 ? ShortName(segments[^2], string.Empty, [], []) : member;
        }

        string owner = segments.Length > 1
            ? ShortName(string.Join('.', segments[..^1]), string.Empty, [], [])
            : string.Empty;
        string name = MemberNameText(member, owner, []);
        return owner.Length > 0 ? owner + "." + name : name;
    }

    /// <summary>The word a kind letter stands for.</summary>
    /// <param name="kind">The letter.</param>
    public static string KindName(char kind) => kind switch {
        'T' => "Type",
        'M' => "Method",
        'P' => "Property",
        'F' => "Field",
        'E' => "Event",
        _ => "Member",
    };

    private static string TypeName(string relative, IReadOnlyList<string> typeParameters) {
        string[] segments = relative.Split('.');
        for (int index = 0; index < segments.Length - 1; index++) {
            segments[index] = StripArity(segments[index]);
        }

        segments[^1] = SegmentName(segments[^1], typeParameters);
        return string.Join('.', segments);
    }

    private static string SegmentName(string segment, IReadOnlyList<string> typeParameters) {
        int tick = segment.IndexOf('`');
        if (tick < 0) {
            return segment;
        }

        int arity = ParseIndex(segment[(tick + 1)..]);
        return segment[..tick] + "<" + Names(typeParameters, arity) + ">";
    }

    private static string StripArity(string segment) {
        int tick = segment.IndexOf('`');
        return tick < 0 ? segment : segment[..tick];
    }

    private static string MemberNameText(string member, string owner, IReadOnlyList<string> methodTypeParameters) {
        if (string.Equals(member, "#ctor", StringComparison.Ordinal)) {
            return LocalName(owner);
        }

        if (string.Equals(member, "#cctor", StringComparison.Ordinal)) {
            return "static " + LocalName(owner);
        }

        return MemberText(member, methodTypeParameters);
    }

    private static string MemberText(string member, IReadOnlyList<string> methodTypeParameters) {
        string name = member.Replace('#', '.');
        int tick = name.IndexOf('`');
        if (tick < 0) {
            return name;
        }

        int digits = tick;
        while (digits < name.Length && name[digits] == '`') {
            digits++;
        }

        int arity = ParseIndex(name[digits..]);
        return name[..tick] + "<" + Names(methodTypeParameters, arity) + ">";
    }

    private static string LocalName(string relative) {
        int dot = relative.LastIndexOf('.');
        return StripArity(dot < 0 ? relative : relative[(dot + 1)..]);
    }

    private static string DisplayType(
        string raw,
        string root,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodTypeParameters) {
        string type = raw.Trim();
        string prefix = string.Empty;
        string suffix = string.Empty;
        if (type.EndsWith('@')) {
            type = type[..^1];
            prefix = "ref ";
        }

        int arrays = 0;
        while (type.EndsWith("[]", StringComparison.Ordinal)) {
            type = type[..^2];
            arrays++;
        }

        if (type.EndsWith('*')) {
            type = type[..^1];
            suffix = "*";
        }

        string core;
        if (type.StartsWith("System.Nullable{", StringComparison.Ordinal) && type.EndsWith('}')) {
            core = DisplayType(type["System.Nullable{".Length..^1], root, typeParameters, methodTypeParameters) + "?";
        } else if (TrySplitGeneric(type, out string name, out IReadOnlyList<string> arguments)) {
            core = ShortName(name, root, typeParameters, methodTypeParameters)
                + "<" + string.Join(", ", arguments.Select(argument =>
                    DisplayType(argument, root, typeParameters, methodTypeParameters))) + ">";
        } else {
            core = ShortName(type, root, typeParameters, methodTypeParameters);
        }

        for (int index = 0; index < arrays; index++) {
            core += "[]";
        }

        return prefix + core + suffix;
    }

    private static bool TrySplitGeneric(string type, out string name, out IReadOnlyList<string> arguments) {
        int open = type.IndexOf('{');
        if (open < 0 || !type.EndsWith('}')) {
            name = type;
            arguments = [];
            return false;
        }

        name = type[..open];
        string inside = type[(open + 1)..^1];
        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int index = 0; index < inside.Length; index++) {
            switch (inside[index]) {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(inside[start..index]);
                    start = index + 1;
                    break;
            }
        }

        parts.Add(inside[start..]);
        arguments = parts;
        return true;
    }

    private static string ShortName(
        string name,
        string root,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodTypeParameters) {
        if (name.StartsWith("``", StringComparison.Ordinal)) {
            return NameAt(methodTypeParameters, ParseIndex(name[2..]));
        }

        if (name.StartsWith('`')) {
            return NameAt(typeParameters, ParseIndex(name[1..]));
        }

        if (Keywords.TryGetValue(name, out string? keyword)) {
            return keyword;
        }

        if (root.Length > 0 && name.StartsWith(root + ".", StringComparison.Ordinal)) {
            return name[(root.Length + 1)..];
        }

        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static string NameAt(IReadOnlyList<string> names, int index) =>
        index < names.Count
            ? names[index]
            : index == 0 ? "T" : "T" + index.ToString(CultureInfo.InvariantCulture);

    private static string Names(IReadOnlyList<string> names, int arity) {
        if (arity <= 0) {
            return string.Empty;
        }

        List<string> used = [];
        for (int index = 0; index < arity; index++) {
            used.Add(index < names.Count
                ? names[index]
                : index == 0 ? "T" : "T" + index.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(", ", used);
    }

    private static int ParseIndex(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : 0;

    private static string Arity(IReadOnlyList<string> segments) =>
        string.Join('.', segments.Select(segment => {
            int tick = segment.IndexOf('`');
            return tick < 0
                ? segment.ToLowerInvariant()
                : segment[..tick].ToLowerInvariant() + "-" + segment[(tick + 1)..];
        }));

    private static string SlugType(string value) {
        List<char> characters = [];
        for (int index = 0; index < value.Length; index++) {
            if (value[index] == '`') {
                characters.Add('-');
                while (index + 1 < value.Length && char.IsAsciiDigit(value[index + 1])) {
                    index++;
                    characters.Add(value[index]);
                }
            } else {
                characters.Add(value[index]);
            }
        }

        return SlugText(new string([.. characters]));
    }

    private static string SlugText(string value) => Slug(value.Replace("[]", "-array", StringComparison.Ordinal)
        .Replace("?", "-nullable", StringComparison.Ordinal));

    private static string Slug(string value) {
        List<char> characters = [];
        bool dash = false;
        foreach (char character in value) {
            if (char.IsAsciiLetterOrDigit(character) || character == '.') {
                characters.Add(character);
                dash = false;
            } else if (!dash) {
                characters.Add('-');
                dash = true;
            }
        }

        string slug = new string([.. characters]).Trim('-');
        return slug.Length > 0 ? slug : "member";
    }
}
