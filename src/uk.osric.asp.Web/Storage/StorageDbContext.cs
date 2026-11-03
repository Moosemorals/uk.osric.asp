// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Users;

internal sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options) {
    private static readonly DbConnectionInterceptor s_revisionHashInterceptor = new RevisionHashInterceptor();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        optionsBuilder.AddInterceptors(s_revisionHashInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<StoredRevision>(revision => {
            revision.ToTable("PageRevisions");
            revision.HasKey(row => row.RevisionId);
            revision.HasAlternateKey(row => new { row.RevisionId, row.Path });
            revision.Property(row => row.RevisionId).UseCollation("BINARY");
            revision.Property(row => row.Path).UseCollation("BINARY");
            revision.Property(row => row.Hash).UseCollation("BINARY");
            revision.HasIndex(row => row.Hash).IsUnique();
            revision.HasIndex(row => row.Path);
        });

        modelBuilder.Entity<PageHead>(head => {
            head.ToTable("PageHeads");
            head.HasKey(row => row.Path);
            head.Property(row => row.Path).UseCollation("BINARY");
            head.Property(row => row.RevisionId).UseCollation("BINARY");
            head.HasOne<StoredRevision>()
                .WithMany()
                .HasForeignKey(row => new { row.RevisionId, row.Path })
                .HasPrincipalKey(row => new { row.RevisionId, row.Path })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LocalUser>().ToTable("Users");
        modelBuilder.Entity<LocalUser>().HasKey(user => user.UserName);
        modelBuilder.Entity<LocalUser>().Property(user => user.UserName).HasMaxLength(UserName.MaximumLength).UseCollation("BINARY");
        modelBuilder.Entity<LocalUser>().Property(user => user.PasswordHash).HasMaxLength(256);
        modelBuilder.Entity<LocalUser>().Property(user => user.SecurityStamp).HasMaxLength(128);
    }

    private sealed class RevisionHashInterceptor : DbConnectionInterceptor {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) {
            RegisterHash(connection);
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) {
            RegisterHash(connection);
            return Task.CompletedTask;
        }

        private static void RegisterHash(DbConnection connection) {
            if (connection is not SqliteConnection sqlite) {
                return;
            }

            sqlite.CreateFunction<string, string, string, string, string>(
                "page_revision_hash",
                (source, runtimeId, savedAt, revisionId) => PageRevisionHash.Compute(
                    source,
                    runtimeId,
                    DateTimeOffset.Parse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    revisionId),
                isDeterministic: true);
        }
    }
}
