// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Reflection;
using System.Xml.Linq;

/// <summary>Copies an <c>inheritdoc</c> entry's text down from the member it inherits.</summary>
/// <remarks>
/// <para>
/// The XML says <c>&lt;inheritdoc/&gt;</c> and nothing else, and the compiler does not write the
/// source it meant. The assemblies are in this process, so the source can be named exactly: an
/// override's base definition, the interface member the implementation maps to, or a type's base.
/// </para>
/// <para>
/// The copy is best-effort. A source outside the corpus (the BCL, EF Core) is named and, where the
/// API browser has a page, linked; a member reflection cannot name keeps the note. Nothing here can
/// fail a request.
/// </para>
/// </remarks>
internal static class ReferenceInheritance {

    private const int MaxDepth = 8;

    /// <summary>Resolves every inherit-only entry in place.</summary>
    /// <param name="documents">The corpus, already normalised and with its anchors.</param>
    /// <param name="logger">Where a summary line goes.</param>
    public static void Resolve(IReadOnlyList<ReferenceDocument> documents, ILogger logger) {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(logger);

        Dictionary<string, XElement> elements = [];
        Dictionary<string, (string Slug, ReferenceEntry Entry)> entries = [];
        foreach (ReferenceDocument document in documents) {
            foreach (XElement element in document.Content.Descendants().Where(item => item.Attribute("id") is not null)) {
                elements[element.Attribute("id")!.Value] = element;
            }

            foreach ((string id, ReferenceEntry entry) in document.Entries) {
                entries[id] = (document.Info.Slug, entry);
            }
        }

        Dictionary<string, MemberInfo> members = [];
        foreach (ReferenceDocument document in documents) {
            if (!document.Content.Descendants("inheritdoc").Any()) {
                continue;
            }

            if (Load(document.Info.AssemblyName) is { } assembly) {
                foreach ((string memberId, MemberInfo member) in ReferenceDocIds.Map(assembly)) {
                    _ = members.TryAdd(memberId, member);
                }
            }
        }

        int copied = 0;
        int noted = 0;
        HashSet<string> active = [];
        foreach ((string id, XElement element) in elements) {
            if (!IsInheritOnly(element)) {
                continue;
            }

            switch (ResolveOne(id, element, elements, entries, members, active, depth: 0)) {
                case Outcome.Copied:
                    copied++;
                    break;
                case Outcome.Noted:
                    noted++;
                    break;
                default:
                    break;
            }
        }

        logger.LogDebug(
            "Resolved {Copied} inherited documentation entries and noted {Noted} whose source is elsewhere",
            copied,
            noted);
    }

    private enum Outcome {
        None,
        Copied,
        Noted,
    }

    private static Outcome ResolveOne(
        string id,
        XElement target,
        IReadOnlyDictionary<string, XElement> elements,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries,
        IReadOnlyDictionary<string, MemberInfo> members,
        HashSet<string> active,
        int depth) {
        if (depth > MaxDepth) {
            Note(target);
            return Outcome.Noted;
        }

        if (!active.Add(id)) {
            Note(target);
            return Outcome.Noted;
        }

        try {
            if (!members.TryGetValue(id, out MemberInfo? member)) {
                Note(target);
                return Outcome.Noted;
            }

            MemberInfo? source = Source(member, entries);
            if (source is null) {
                Note(target);
                return Outcome.Noted;
            }

            string sourceId = ReferenceDocIds.Of(source);
            string display = Display(sourceId, entries);
            target.Elements("inheritdoc").Remove();
            target.SetAttributeValue("inherited-from", sourceId);
            target.SetAttributeValue("inherited-display", display);

            if (elements.TryGetValue(sourceId, out XElement? sourceElement)
                && !ReferenceEquals(sourceElement, target)
                && entries.TryGetValue(sourceId, out (string Slug, ReferenceEntry Entry) entry)) {
                if (IsInheritOnly(sourceElement)) {
                    _ = ResolveOne(sourceId, sourceElement, elements, entries, members, active, depth + 1);
                }

                if (!IsInheritOnly(sourceElement)) {
                    foreach (XElement child in sourceElement.Elements()
                        .Where(child => child.Name != "inheritdoc" && child.Name != "member")) {
                        target.Add(new XElement(child));
                    }

                    RewriteNames(target, member);
                    target.SetAttributeValue(
                        "inherited-href",
                        "/admin/reference/" + entry.Slug + "#" + entry.Entry.Anchor);
                    return Outcome.Copied;
                }
            }

            if (ReferenceDisplay.LearnUrl(sourceId) is { } learn) {
                target.SetAttributeValue("inherited-href", learn);
            }

            return Outcome.Noted;
        } finally {
            _ = active.Remove(id);
        }
    }

    private static MemberInfo? Source(
        MemberInfo member,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) {
        return member switch {
            Type type => SourceType(type, entries),
            MethodInfo method => SourceMethod(method, entries),
            PropertyInfo property => SourceProperty(property, entries),
            EventInfo @event => SourceEvent(@event, entries),
            _ => null,
        };
    }

    private static Type? SourceType(
        Type type,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) {
        List<Type> candidates = [];
        if (type.BaseType is { } baseType && baseType != typeof(object)) {
            candidates.Add(baseType);
        }

        candidates.AddRange(type.GetInterfaces());
        return candidates.FirstOrDefault(candidate => entries.ContainsKey(ReferenceDocIds.Of(candidate)))
            ?? candidates.FirstOrDefault();
    }

    private static MethodInfo? SourceMethod(
        MethodInfo method,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) {
        if (method.IsVirtual && method.GetBaseDefinition() is { } baseMethod && baseMethod != method) {
            return baseMethod;
        }

        List<MethodInfo> candidates = [];
        foreach (Type contract in method.DeclaringType!.GetInterfaces()) {
            InterfaceMapping map;
            try {
                map = method.DeclaringType.GetInterfaceMap(contract);
            } catch (ArgumentException) {
                continue;
            }

            for (int index = 0; index < map.TargetMethods.Length; index++) {
                if (ReferenceEquals(map.TargetMethods[index], method)) {
                    candidates.Add(map.InterfaceMethods[index]);
                }
            }
        }

        return candidates
            .OrderBy(candidate => entries.ContainsKey(ReferenceDocIds.Of(candidate)) ? 0 : 1)
            .ThenBy(candidate => ReferenceDocIds.Of(candidate), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static PropertyInfo? SourceProperty(
        PropertyInfo property,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) =>
        AccessorSource(property.GetMethod, entries, PropertyOf)
        ?? AccessorSource(property.SetMethod, entries, PropertyOf);

    private static EventInfo? SourceEvent(
        EventInfo @event,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) =>
        AccessorSource(@event.AddMethod, entries, EventOf)
        ?? AccessorSource(@event.RemoveMethod, entries, EventOf);

    private static T? AccessorSource<T>(
        MethodInfo? accessor,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries,
        Func<Type, MethodInfo, T?> find)
        where T : MemberInfo {
        if (accessor is null || SourceMethod(accessor, entries) is not { } sourceAccessor) {
            return null;
        }

        return find(sourceAccessor.DeclaringType!, sourceAccessor);
    }

    private static EventInfo? EventOf(Type type, MethodInfo accessor) =>
        type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .FirstOrDefault(candidate => candidate.AddMethod == accessor || candidate.RemoveMethod == accessor);

    private static PropertyInfo? PropertyOf(Type type, MethodInfo accessor) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .FirstOrDefault(property => property.GetMethod == accessor || property.SetMethod == accessor);

    private static string Display(
        string id,
        IReadOnlyDictionary<string, (string Slug, ReferenceEntry Entry)> entries) {
        if (entries.TryGetValue(id, out (string Slug, ReferenceEntry Entry) entry)) {
            if (entry.Entry.IsType || ReferenceDisplay.Owner(id) is not { } owner) {
                return entry.Entry.Text;
            }

            return entries.TryGetValue(owner, out (string Slug, ReferenceEntry Entry) source)
                ? source.Entry.Text + "." + entry.Entry.Text
                : entry.Entry.Text;
        }

        return ReferenceDisplay.CrefText(id);
    }

    private static void RewriteNames(XElement target, MemberInfo member) {
        IReadOnlyList<string> parameters = member switch {
            MethodBase method => [.. method.GetParameters().Select(parameter => parameter.Name).OfType<string>()],
            PropertyInfo property => [.. property.GetIndexParameters().Select(parameter => parameter.Name).OfType<string>()],
            _ => [],
        };

        IReadOnlyList<string> typeParameters = member switch {
            MethodInfo method => [.. method.GetGenericArguments().Select(argument => argument.Name)],
            Type type => [.. type.GetGenericArguments().Select(argument => argument.Name)],
            _ => [],
        };

        Rewrite(target.Elements("param"), parameters);
        Rewrite(target.Elements("typeparam"), typeParameters);
    }

    private static void Rewrite(IEnumerable<XElement> elements, IReadOnlyList<string> names) {
        int index = 0;
        foreach (XElement element in elements) {
            if (index < names.Count) {
                element.SetAttributeValue("name", names[index]);
            }

            index++;
        }
    }

    // A type element also holds its members, so only the type's own documentation is considered.
    private static bool IsInheritOnly(XElement element) {
        List<XElement> content = [.. element.Elements().Where(child => child.Name != "member")];
        return content.Any(child => child.Name == "inheritdoc")
            && content.All(child => child.Name == "inheritdoc")
            && !element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
    }

    private static void Note(XElement target) {
        target.Elements("inheritdoc").Remove();
        target.SetAttributeValue("inherits", "true");
    }

    private static Assembly? Load(string name) {
        try {
            return Assembly.Load(new AssemblyName(name));
        } catch (FileNotFoundException) {
            return null;
        } catch (FileLoadException) {
            return null;
        } catch (BadImageFormatException) {
            return null;
        }
    }

}
