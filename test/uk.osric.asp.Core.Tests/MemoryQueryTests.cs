// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class MemoryQueryTests {

    [Test]
    public void TermsAreQuotedPrefixesOfTheMessagesOwnWords() {
        Assert.That(MemoryQuery.Terms("Make the deploy script say deployment", 12),
            Is.EqualTo("\"deploy\"* \"script\"* \"say\"* \"deployment\"*"));
    }

    [Test]
    public void StopwordsAndShortTermsAreDropped() {
        Assert.That(MemoryQuery.Terms("I want it to be a nicer blue", 12), Is.EqualTo("\"nicer\"* \"blue\"*"));
    }

    [Test]
    public void DuplicatesAreKeptOnceInTheOrderTheyAppear() {
        Assert.That(MemoryQuery.Terms("colour Colour colours", 12), Is.EqualTo("\"colour\"* \"colours\"*"));
    }

    [Test]
    public void CaseIsFolded() {
        Assert.That(MemoryQuery.Terms("PREFERS British English", 12), Is.EqualTo("\"prefers\"* \"british\"* \"english\"*"));
    }

    [Test]
    public void AtMostTheMaximumTermsAreKept() {
        string message = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike";

        Assert.That(MemoryQuery.Terms(message, 4), Is.EqualTo("\"alpha\"* \"bravo\"* \"charlie\"* \"delta\"*"));
    }

    [Test]
    public void Fts5SyntaxInTheMessageIsNeutralisedByQuoting() {
        string terms = MemoryQuery.Terms("deploy OR NEAR( \"unbalanced", 12);

        Assert.That(terms, Is.EqualTo("\"deploy\"* \"near\"* \"unbalanced\"*"));
    }

    [Test]
    public void AMessageWithNoUsableTermYieldsNothing() {
        Assert.That(MemoryQuery.Terms("a I, and the...", 12), Is.Empty);
    }

    [Test]
    public void NonAsciiWordsSurvive() {
        Assert.That(MemoryQuery.Terms("Bevorzugt Straße café", 12), Is.EqualTo("\"bevorzugt\"* \"straße\"* \"café\"*"));
    }
}
