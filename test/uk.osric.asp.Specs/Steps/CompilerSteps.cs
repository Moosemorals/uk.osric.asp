// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Specs.Steps;

using NUnit.Framework;
using Reqnroll;
using uk.osric.asp.Core;

[Binding]
public sealed class CompilerSteps {
    private string _source = string.Empty;
    private bool _compilationSucceeded;

    [Given("the following C# source:")]
    public void GivenTheFollowingCSharpSource(string source) {
        _source = source;
    }

    [When("the source is compiled")]
    public void WhenTheSourceIsCompiled() {
        using MemoryStream assembly = new();
        _compilationSucceeded = Compiler.Compile(_source, assembly).Success;
    }

    [Then("the compilation succeeds")]
    public void ThenTheCompilationSucceeds() {
        Assert.That(_compilationSucceeded, Is.True);
    }

    [Then("the compilation fails")]
    public void ThenTheCompilationFails() {
        Assert.That(_compilationSucceeded, Is.False);
    }
}