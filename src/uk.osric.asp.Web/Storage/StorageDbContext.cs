// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options) {
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<StoredFile>().ToTable("Files");
        modelBuilder.Entity<StoredFile>().HasKey(file => file.Path);
        modelBuilder.Entity<StoredFile>().Property(file => file.Path).UseCollation("BINARY");
    }
}
