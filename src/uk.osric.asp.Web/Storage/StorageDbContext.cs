// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Web.Users;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options) {
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<StoredFile>().ToTable("Files");
        modelBuilder.Entity<StoredFile>().HasKey(file => file.Path);
        modelBuilder.Entity<StoredFile>().Property(file => file.Path).UseCollation("BINARY");

        modelBuilder.Entity<LocalUser>().ToTable("Users");
        modelBuilder.Entity<LocalUser>().HasKey(user => user.UserName);
        modelBuilder.Entity<LocalUser>().Property(user => user.UserName).HasMaxLength(UserName.MaximumLength).UseCollation("BINARY");
        modelBuilder.Entity<LocalUser>().Property(user => user.PasswordHash).HasMaxLength(256);
        modelBuilder.Entity<LocalUser>().Property(user => user.SecurityStamp).HasMaxLength(128);
    }
}
