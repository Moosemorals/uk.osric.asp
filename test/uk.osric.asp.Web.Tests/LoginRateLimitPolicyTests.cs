// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Globalization;
using System.Net;
using uk.osric.asp.Web.Authentication;

[TestFixture]
public sealed class LoginRateLimitPolicyTests {

    [Test]
    public void AMissingAddressSharesOneBucket() {
        Assert.That(LoginRateLimitPolicy.PartitionFor(null), Is.EqualTo("unknown"));
    }

    [Test]
    public void AnAddressAlwaysLandsInTheSameBucket() {
        IPAddress address = IPAddress.Parse("203.0.113.7");

        Assert.That(
            LoginRateLimitPolicy.PartitionFor(address),
            Is.EqualTo(LoginRateLimitPolicy.PartitionFor(address)));
    }

    [Test]
    public void SeparateAddressesGetSeparateBuckets() {
        Assert.That(
            LoginRateLimitPolicy.PartitionFor(IPAddress.Parse("203.0.113.7")),
            Is.Not.EqualTo(LoginRateLimitPolicy.PartitionFor(IPAddress.Parse("198.51.100.9"))));
    }

    [Test]
    public void TheBucketCountStaysBoundedHoweverManyAddressesAsk() {
        HashSet<string> buckets = [];
        for (int third = 0; third < 16; third++) {
            for (int last = 0; last < 256; last++) {
                string bucket = LoginRateLimitPolicy.PartitionFor(IPAddress.Parse($"198.51.{third}.{last}"));

                Assert.That(
                    int.Parse(bucket, CultureInfo.InvariantCulture),
                    Is.InRange(0, LoginRateLimitPolicy.Buckets - 1));
                buckets.Add(bucket);
            }
        }

        Assert.That(buckets.Count, Is.LessThanOrEqualTo(LoginRateLimitPolicy.Buckets));
    }
}
