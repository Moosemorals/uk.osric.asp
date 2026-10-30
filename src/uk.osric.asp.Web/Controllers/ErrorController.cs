// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Controllers;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Models;

[Route("/error")]
public sealed class ErrorController : Controller {
    [Route("")]
    public IActionResult Index() {
        Exception? exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        UserFacingException? userFacingException = exception switch {
            UserFacingException safeException => safeException,
            TargetInvocationException { InnerException: UserFacingException safeException } => safeException,
            _ => null
        };

        string message = userFacingException?.Message ?? "An unexpected error occurred. Please try again.";
        ErrorViewModel model = new(message, HttpContext.TraceIdentifier);
        return View(model);
    }
}
