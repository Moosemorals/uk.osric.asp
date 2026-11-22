// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Declares one table a stored source owns.</summary>
/// <remarks>
/// <para>
/// The attribute is the table: its name is the SQL name the source writes, its subject decides whether
/// there is an owner column, and its lifetime decides how long a visitor's rows live. The properties of
/// the type it is put on are the columns, in declaration order.
/// </para>
/// <para>
/// A save compares the declaration with what was last applied and applies the difference — a table it
/// has not made, a column it has not made, and a column or table the source no longer names — so an
/// edit that removes a property takes every row in that column with it. Nothing else may be declared:
/// no index, no unique constraint and no foreign key, because uniqueness is a statement the page writes
/// and a child row is deleted by the page's transaction.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class PageTableAttribute : Attribute {

    /// <summary>Declares a table under a name.</summary>
    /// <param name="name">The table's name, as the source writes it in SQL.</param>
    /// <exception cref="ArgumentNullException">No name was given.</exception>
    public PageTableAttribute(string name) {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>The table's name, as the source writes it in SQL.</summary>
    public string Name { get; }

    /// <summary>Whose rows the table holds; <see cref="StoreSubject.Page"/> unless the source says otherwise.</summary>
    public StoreSubject Subject { get; set; }

    /// <summary>
    /// How long a visitor's rows live; <see cref="StoreLifetimeKind.Person"/> unless the source says
    /// otherwise. A page's table always follows the page, so this is ignored when
    /// <see cref="Subject"/> is <see cref="StoreSubject.Page"/>.
    /// </summary>
    public StoreLifetimeKind Lifetime { get; set; } = StoreLifetimeKind.Person;

    /// <summary>The one column <see cref="IRecordStore.SearchAsync"/> matches, or <see langword="null"/>.</summary>
    public string? Search { get; set; }
}
