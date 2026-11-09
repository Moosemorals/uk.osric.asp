// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class MemorySecretsTests {

    [TestCase("-----BEGIN RSA PRIVATE KEY-----\nMIIE...")]
    [TestCase("The deployment key is api_key=7f3c9a1b2e")]
    [TestCase("password: hunter2 and a note")]
    [TestCase("Use token=ghp_abcdefghijkl for the check")]
    [TestCase("The provider key sk-4f9c2d8b1a7e6f3c is set")]
    [TestCase("The value aB3+/zY9mN0pQ2rS4tU6vW8xY0zA2bC4dE6fG8hI0jK= is in the vault")]
    public void ASecretShapeIsRefused(string content) {
        Assert.That(MemorySecrets.LooksLikeSecret(content), Is.Not.Null);
    }

    [TestCase("The API key lives in the deployment environment, not in the repository.")]
    [TestCase("The owner never stores passwords in a page.")]
    [TestCase("Ask before changing the token refresh interval.")]
    [TestCase("The private key file is mounted read-only by the operator.")]
    [TestCase("The owner prefers British English and says \"colour\".")]
    public void ASentenceAboutASecretIsNotASecret(string content) {
        Assert.That(MemorySecrets.LooksLikeSecret(content), Is.Null);
    }
}
