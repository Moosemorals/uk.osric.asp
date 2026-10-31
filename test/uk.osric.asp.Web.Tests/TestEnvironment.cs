// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

/// <summary>Minimal <see cref="IWebHostEnvironment"/> so a test can name the environment it runs as.</summary>
internal sealed class TestEnvironment(string environmentName) : IWebHostEnvironment {

    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "uk.osric.asp.Web.Tests";

    public string WebRootPath { get; set; } = string.Empty;

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

    public string ContentRootPath { get; set; } = string.Empty;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
