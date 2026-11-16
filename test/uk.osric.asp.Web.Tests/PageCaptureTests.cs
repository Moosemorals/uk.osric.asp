// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

/// <summary>The two values a composed page hands back.</summary>
[TestFixture]
public sealed class PageCaptureTests {

    [Test]
    public void ACaptureIsItsFramesTitleHeadSheetsAndScriptPermission() {
        PageFrame frame = Frame();

        PageCapture capture = new("<p>hi</p>", frame);

        Assert.Multiple(() => {
            Assert.That(capture.Body, Is.EqualTo("<p>hi</p>"));
            Assert.That(capture.Title, Is.EqualTo(frame.Title));
            Assert.That(capture.Head, Is.EqualTo(frame.Head));
            Assert.That(capture.Stylesheets, Is.EqualTo(frame.Stylesheets));
            Assert.That(capture.AllowsScripts, Is.EqualTo(frame.AllowsScripts));
        });
    }

    [Test]
    public void ACaptureIsAFrame() {
        Assert.That(new PageCapture("body", Frame()), Is.InstanceOf<PageFrame>());
    }

    [Test]
    public void ANullBodyOrFrameIsRefused() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => new PageCapture(null!, Frame()));
            Assert.Throws<ArgumentNullException>(() => new PageCapture("body", null!));
        });
    }

    [Test]
    public void TwoCapturesOfTheSameFrameAreEqual() {
        PageFrame frame = Frame();
        PageCapture left = new("body", frame);
        PageCapture right = new("body", frame);

        Assert.Multiple(() => {
            Assert.That(left, Is.EqualTo(right));
            Assert.That(left == right, Is.True);
        });
    }

    [Test]
    public void ACapturesBodyTakesPartInEquality() {
        Assert.That(new PageCapture("one", Frame()), Is.Not.EqualTo(new PageCapture("two", Frame())));
    }

    private static PageFrame Frame() => new(
        "A title",
        "<meta name=\"x\">",
        ["/one.css"],
        AllowsScripts: true);
}
