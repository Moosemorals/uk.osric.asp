// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;
using uk.osric.asp.Web.Pages;

/// <summary>
/// The order of the gate is the behaviour: an unaddressable path is refused before a shadowed key is
/// looked for, a read beneath a reserved route is refused by the shadow check, and the wording of a
/// refusal comes from the tool rather than from the helper.
/// </summary>
[TestFixture]
public sealed class ToolPathsTests {

    [Test]
    public void AnUnaddressablePathIsRefusedBeforeTheShadowCheck() {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(
            Spec("read_page", "Read"), Values("/hello?world"), "path", Services());

        Assert.Multiple(() => {
            Assert.That(path, Is.Null);
            Assert.That(refusal?.IsSuccess, Is.False);
            Assert.That(refusal?.Content, Does.Contain("not addressable"));
        });
    }

    [Test]
    public void AReadBeneathAReservedRouteIsRefusedByTheShadowCheck() {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(
            Spec("read_page", "Read"), Values("/admin/scrub/child"), "path", Services());

        Assert.Multiple(() => {
            Assert.That(path, Is.Null);
            Assert.That(refusal?.Content, Does.Contain("beneath the shell file"));
            Assert.That(refusal?.Summary, Is.EqualTo("Read /admin/scrub/child: beneath /admin/scrub"));
        });
    }

    [Test]
    public void AReadOfAnOrdinaryPageIsNotRefused() {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(
            Spec("read_page", "Read"), Values("/notes"), "path", Services());

        Assert.Multiple(() => {
            Assert.That(path?.Key, Is.EqualTo("/notes"));
            Assert.That(refusal, Is.Null);
        });
    }

    [Test]
    public void AWriteRefusalUsesTheToolsOwnVerb() {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveWrite(
            Spec("edit_page", "Edit"), Values("/admin/scrub"), "path", Services());

        Assert.Multiple(() => {
            Assert.That(path, Is.Null);
            Assert.That(refusal?.Summary, Is.EqualTo("Edit refused"));
        });
    }

    [Test]
    public void AShellFileHasNoPageSourceAndTheRefusalSaysWhatTheToolReads() {
        ToolExecutionResult? refusal = ToolPaths.RequirePageSource(
            Spec("check_script", "Check"), PagePath.Normalize("/admin/scrub"), "reads page source", Services());

        Assert.Multiple(() => {
            Assert.That(refusal?.IsSuccess, Is.False);
            Assert.That(refusal?.Content, Does.Contain("check_script reads page source"));
            Assert.That(refusal?.Summary, Is.EqualTo("Check refused"));
        });
    }

    [Test]
    public void APageIsPageSource() {
        ToolExecutionResult? refusal = ToolPaths.RequirePageSource(
            Spec("check_script", "Check"), PagePath.Normalize("/notes"), "reads page source", Services());

        Assert.That(refusal, Is.Null);
    }

    private static ToolSpec Spec(string name, string verb) => new(name, "test", false, []) {
        Verb = verb,
    };

    private static ToolValues Values(string path) =>
        new(new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
            ["path"] = JsonSerializer.SerializeToElement(path),
        });

    private static ToolServices Services() => new(
        new ServiceCollection().BuildServiceProvider(),
        new ToolContext("owner", ConversationId: 1, RunId: "run-1"),
        new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), new AssistantOptions()),
        new AssistantOptions(),
        new AssistantMetrics(),
        new FakeAssistantFetcher(),
        new DiffRenderer());
}
