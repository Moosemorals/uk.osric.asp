// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Reflection;

/// <summary>Rebuilds the documentation id Roslyn writes from the reflected member.</summary>
/// <remarks>
/// The XML carries no token, file or line: an id is the only handle on a member, and
/// <c>inheritdoc</c> resolution has to go from an id to a member and back to another id. The rules
/// are Roslyn's, and the suite proves this bridge by rebuilding every id in the four real files.
/// </remarks>
internal static class ReferenceDocIds {

    /// <summary>The id of a type or member.</summary>
    /// <param name="member">The reflected member.</param>
    public static string Of(MemberInfo member) {
        ArgumentNullException.ThrowIfNull(member);

        return member switch {
            Type type => Of(type),
            MethodBase method => Method(method),
            PropertyInfo property => "P:" + TypeName(property.DeclaringType!) + "." + MemberName(property.Name)
                + Parameters(property.GetIndexParameters()),
            FieldInfo field => "F:" + TypeName(field.DeclaringType!) + "." + MemberName(field.Name),
            EventInfo @event => "E:" + TypeName(@event.DeclaringType!) + "." + MemberName(@event.Name),
            _ => throw new ArgumentException($"'{member}' has no documentation id.", nameof(member)),
        };
    }

    /// <summary>The id of a type.</summary>
    /// <param name="type">The reflected type.</param>
    public static string Of(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        return "T:" + TypeName(type);
    }

    /// <summary>Every type and member of an assembly, by id.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <remarks>A member whose id cannot be built is left out; the entry that names it keeps its note.</remarks>
    public static IReadOnlyDictionary<string, MemberInfo> Map(Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);

        Dictionary<string, MemberInfo> map = [];
        Type[] types;
        try {
            types = assembly.GetTypes();
        } catch (ReflectionTypeLoadException exception) {
            types = [.. exception.Types.OfType<Type>()];
        }

        foreach (Type type in types) {
            Add(map, type);
            foreach (MemberInfo member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                Add(map, member);
            }
        }

        return map;
    }

    private static void Add(Dictionary<string, MemberInfo> map, MemberInfo member) {
        try {
            _ = map.TryAdd(Of(member), member);
        } catch (ArgumentException) {
            // A member the bridge cannot name is one inheritdoc cannot resolve; nothing else needs it.
        }
    }

    private static string Method(MethodBase method) {
        string name = method switch {
            ConstructorInfo { IsStatic: true } => "#cctor",
            ConstructorInfo => "#ctor",
            _ => MemberName(method.Name),
        };

        if (method.IsGenericMethod) {
            name += "``" + method.GetGenericArguments().Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string suffix = method is MethodInfo { Name: "op_Implicit" or "op_Explicit" } info
            ? "~" + ParameterName(info.ReturnType)
            : string.Empty;

        return "M:" + TypeName(method.DeclaringType!) + "." + name + Parameters(method.GetParameters()) + suffix;
    }

    private static string Parameters(ParameterInfo[] parameters) =>
        parameters.Length == 0
            ? string.Empty
            : "(" + string.Join(",", parameters.Select(parameter => ParameterName(parameter.ParameterType))) + ")";

    private static string ParameterName(Type type) {
        if (type.IsByRef) {
            return ParameterName(type.GetElementType()!) + "@";
        }

        if (type.IsArray) {
            return ParameterName(type.GetElementType()!) + "[]";
        }

        if (type.IsPointer) {
            return ParameterName(type.GetElementType()!) + "*";
        }

        if (type.IsGenericParameter) {
            return (type.DeclaringMethod is null ? "`" : "``")
                + type.GenericParameterPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition) {
            Type definition = type.GetGenericTypeDefinition();
            string name = TypeName(definition);
            int tick = name.LastIndexOf('`');
            return name[..tick]
                + "{" + string.Join(",", type.GetGenericArguments().Select(ParameterName)) + "}";
        }

        return TypeName(type);
    }

    private static string TypeName(Type type) {
        if (type.IsGenericParameter) {
            return (type.DeclaringMethod is null ? "`" : "``")
                + type.GenericParameterPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type.IsNested) {
            return TypeName(type.DeclaringType!) + "." + type.Name;
        }

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
    }

    // An explicit interface implementation is written Ns#IFoo#Member in an id: every dot inside the
    // member's own name is a hash, and the dot before the type is added by the caller.
    private static string MemberName(string name) => name.Replace('.', '#');
}
