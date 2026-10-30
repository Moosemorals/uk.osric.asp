// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Web.Storage;

/// <summary>Imperative shell: wires the web host to the functional core.</summary>
public static class Program {
    public static async Task Main(string[] args) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string connectionString = builder.Configuration.GetConnectionString("Storage")
            ?? throw new InvalidOperationException("ConnectionStrings:Storage must be configured.");
        SqliteConnectionStringBuilder connection = new(connectionString);
        if (connection.DataSource.Length > 0 && connection.DataSource != ":memory:") {
            connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)
                ?? throw new InvalidOperationException("The storage database must have a parent directory."));
        }

        builder.Services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection.ToString()));
        builder.Services.AddScoped<IStorage, SqliteStorage>();


        WebApplication app = builder.Build();
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
            StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
            await database.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
        }

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        } else {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseStaticFiles();

        app.UseRouting();
        app.Map("/edit/{**path}", EditorEndpoint.Handler);
        app.MapGet("/error", ErrorEndpoint.Handle);
        app.Map("{**path}", MainEndpoint.Handle);

        await app.RunAsync();
    }
}
