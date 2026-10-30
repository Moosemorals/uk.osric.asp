// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

/// <summary>Imperative shell: wires the web host to the functional core.</summary>
public static class Program {
    public static void Main(string[] args) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        } else {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseStaticFiles();
        app.UseRouting();

        app.MapGet("/error", ErrorEndpoint.Handle);
        app.MapFallback(MainEndpoint.Handle);

        app.Run();
    }
}
