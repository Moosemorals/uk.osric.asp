// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using uk.osric.asp.Core;


[Route("/")]
public class HomeController : Controller {
    [HttpGet("")]
    public IActionResult Index() {

        IContext ctx = new SimpleContext();

        MemoryStream assemblyStream = new();
        ctx.BuildResult = Compiler.Compile(""" 
            using uk.osric.asp.Core;
            public class Hello : IHandler {

                public void Handle(IContext context) {
                    throw new UserFacingException("Hello from the error handler!");
                    context.Log("Hello, world!");
                }
            }
        """, assemblyStream);

        if (ctx.BuildResult.Success) {
            assemblyStream.Seek(0, SeekOrigin.Begin);
            Compiler.Run(assemblyStream, ctx);
        }


        return View(ctx);
    }
}