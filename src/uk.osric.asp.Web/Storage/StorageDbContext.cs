// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Agent;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Users;

/// <summary>The EF Core context: every table the site stores, mapped in one place.</summary>
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

        modelBuilder.Entity<StoredPageHead>(head => {
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

        modelBuilder.Entity<AuthTicket>(ticket => {
            ticket.ToTable("AuthTickets");
            ticket.HasKey(row => row.Key);
            ticket.Property(row => row.Key).HasMaxLength(32).UseCollation("BINARY");
            ticket.Property(row => row.UserName).HasMaxLength(UserName.MaximumLength).UseCollation("BINARY");
            ticket.HasIndex(row => row.ExpiresAt);
        });

        modelBuilder.Entity<AgentConversation>(conversation => {
            conversation.ToTable("AgentConversations");
            conversation.HasKey(row => row.Id);
            conversation.Property(row => row.Owner).HasMaxLength(UserName.MaximumLength).UseCollation("BINARY");
            conversation.Property(row => row.Title).HasMaxLength(200);
            conversation.Property(row => row.ApprovalMode).HasMaxLength(32);
            conversation.HasIndex(row => new { row.Owner, row.UpdatedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<AgentRun>(run => {
            run.ToTable("AgentRuns");
            run.HasKey(row => row.Id);
            run.Property(row => row.Id).HasMaxLength(32).UseCollation("BINARY");
            run.Property(row => row.Status).HasMaxLength(32);
            run.Property(row => row.Model).HasMaxLength(200);
            run.Property(row => row.ApprovalMode).HasMaxLength(32);
            run.Property(row => row.PromptVersion).HasMaxLength(100);
            run.Property(row => row.ErrorCode).HasMaxLength(100);
            run.HasOne<AgentConversation>()
                .WithMany()
                .HasForeignKey(row => row.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            run.HasIndex(row => row.ConversationId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'running', 'awaiting_approval')");
        });

        modelBuilder.Entity<AgentMessage>(message => {
            message.ToTable("AgentMessages");
            message.HasKey(row => row.Id);
            message.Property(row => row.Role).HasMaxLength(16);
            message.Property(row => row.Status).HasMaxLength(16);
            message.Property(row => row.ToolCallId).UseCollation("BINARY");
            message.Property(row => row.RunId).HasMaxLength(32).UseCollation("BINARY");
            message.HasOne<AgentConversation>()
                .WithMany()
                .HasForeignKey(row => row.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            message.HasOne<AgentRun>()
                .WithMany()
                .HasForeignKey(row => row.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            message.HasIndex(row => new { row.ConversationId, row.Sequence }).IsUnique();
            message.HasIndex(row => new { row.ConversationId, row.RunId });
        });

        modelBuilder.Entity<AgentToolApproval>(approval => {
            approval.ToTable("AgentToolApprovals");
            approval.HasKey(row => new { row.RunId, row.ToolCallId });
            approval.Property(row => row.RunId).HasMaxLength(32).UseCollation("BINARY");
            approval.Property(row => row.ToolCallId).UseCollation("BINARY");
            approval.Property(row => row.Status).HasMaxLength(16);
            approval.Property(row => row.ResolvedBy).HasMaxLength(UserName.MaximumLength).UseCollation("BINARY");
            approval.Property(row => row.FailureCode).HasMaxLength(100);
            approval.HasOne<AgentRun>()
                .WithMany()
                .HasForeignKey(row => row.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
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
                (source, runtimeId, savedAt, revisionId) => PageRevisionHash.Of(
                    source,
                    runtimeId,
                    DateTimeOffset.Parse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    revisionId),
                isDeterministic: true);
        }
    }
}
