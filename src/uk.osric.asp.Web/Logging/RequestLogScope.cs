// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Logging;

using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

/// <summary>Puts the request's ambient facts on every line written while it is answered.</summary>
/// <remarks>
/// Two middleware, because a fact cannot be put on a line before it is known: <see
/// cref="UseRequestLogScope"/> opens the scope with what the request itself carries and <see
/// cref="UseRequestLogSubject"/> adds the account and the route values once authentication and
/// routing have run.
/// </remarks>
internal static class RequestLogScope {

    private const string ItemsKey = "uk.osric.asp.request-log-scope";
    private const string IdSuffix = "id";
    private const string UserNameField = "UserName";
    private const string RouteField = "Route";

    /// <summary>Opens the scope for the request, with the exchange's own identity.</summary>
    /// <param name="app">The pipeline, before the exception handler.</param>
    /// <remarks>
    /// Registered ahead of the exception handler on purpose: the handler logs an unhandled exception
    /// from inside this scope, so that line names the request like every other line about it, and the
    /// error page it renders is written inside the scope too.
    /// </remarks>
    public static IApplicationBuilder UseRequestLogScope(this IApplicationBuilder app) {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) => {
            LogScope scope = new LogScope()
                .Set("RequestId", context.TraceIdentifier)
                .Set("Method", context.Request.Method)
                .Set("Path", context.Request.Path.Value);

            // The scope is kept on the context as well as opened, so the second middleware can fill
            // it in and anything holding the context can find it again.
            context.Items[ItemsKey] = scope;

            ILogger logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RequestLogScope).FullName!);

            using (logger.BeginScope(scope)) {
                await next(context);
            }
        });
    }

    /// <summary>Adds the account and the route's own ids, once both are known.</summary>
    /// <param name="app">The pipeline, after authentication and routing.</param>
    public static IApplicationBuilder UseRequestLogSubject(this IApplicationBuilder app) {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) => {
            if (context.Items.TryGetValue(ItemsKey, out object? found) && found is LogScope scope) {
                scope.Set(UserNameField, context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

                // A route value that names something is what the request is about: the page being
                // edited, the run being stopped. Anything else the route carries is either already in
                // Path or not worth a field on every line.
                foreach ((string name, object? value) in context.Request.RouteValues) {
                    if (value is not null && name.EndsWith(IdSuffix, StringComparison.OrdinalIgnoreCase)) {
                        scope.Set(Title(name), value);
                    }
                }

                // "Id" alone would not say which id it is; the route is what makes it readable.
                scope.Set(RouteField, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText);
            }

            return next(context);
        });
    }

    // Route values are written in the route's own casing ({runId}); a field in the journal is
    // PascalCase like every other scope key.
    private static string Title(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
