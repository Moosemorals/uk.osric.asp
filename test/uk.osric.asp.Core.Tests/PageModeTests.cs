// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageModeTests {

    [TestCase(true, ExpectedResult = "-rw-r--r--")]
    [TestCase(false, ExpectedResult = "-r--r--r--")]
    public string DescribeAnswersWhetherASaveWouldBeAccepted(bool canWrite) =>
        PageMode.Describe(canWrite);

    [TestCase(true, ExpectedResult = "drwxr-xr-x")]
    [TestCase(false, ExpectedResult = "dr-xr-xr-x")]
    public string DescribeFolderAnswersWhetherASaveBelowItWouldBeAccepted(bool canWrite) =>
        PageMode.DescribeFolder(canWrite);
}
