<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The page API admits no asymmetric key, so a page cannot verify a signature

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Core (compilation), docs |
| Location | `src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs`, `test/uk.osric.asp.Core.Tests/PageApiPolicyTests.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md`, `docs/decisions.md` |
| Spike | — |

## Idea

`PageApiPolicy` admits `System.Text.Encoding`, the SHA-2 hashes, the matching HMACs,
`RandomNumberGenerator` and `CryptographicOperations`, and nothing else in
`System.Security.Cryptography`. A page can hash and sign with a shared secret, and cannot verify a
signature made with a public key — which is what an inbound federation request is. A page can
therefore read a public document and cannot read a members-only one, because it cannot prove who
asked.

The list is data, and the entry a page needs is a type whose only effect is to compute over a value
the page already holds: `RSA` with `RSAParameters`, `HashAlgorithmName` and
`RSASignaturePadding`. None of them opens a file, a socket, a certificate store or a process-wide
resource, so they pass `pages-admit-what-adds-no-capability` unchanged. `RSAParameters` is not
optional beside `RSA`: a public key arrives as a JWK, and its `n` and `e` are the modulus and
exponent a page must pass to `ImportParameters`. PEM import and export are on `RSA` alone.

The old rationale — "a private key has no business in a stored page" — was about where a key is
*kept*, not about whether a page may compute with one. Where it is kept is the secrets shape in
[`a-page-cannot-keep-a-secret`](a-page-cannot-keep-a-secret.md); this item is only the arithmetic.

Ed25519 has no type in .NET 11: `System.Security.Cryptography` carries ML-DSA, SLH-DSA and the
composite names that mention Ed25519, and no primitive. It cannot be an entry in a list of types, so
it is deferred; a managed implementation in `Sdk` would be its own decision, and it is not what a
Mastodon peer checks first.

## Sketch

1. **Four entries.** `System.Security.Cryptography.RSA`, `RSAParameters`, `HashAlgorithmName` and
   `RSASignaturePadding`, each with the test that proves it is admitted and its neighbours still are
   not (`MD5`, `ECDsa`, `X509Certificate2`, `Rfc2898DeriveBytes`, `CngKey`).
2. **The rationale is rewritten.** The policy's remarks and
   [`pages-the-api-policy`](../decisions.md#pages-the-api-policy) lose the sentence that refuses an
   asymmetric primitive and say what the test is instead: a key is a value, and where it is stored is
   a storage decision.
3. **The version moves.** `PolicyVersion` changes, so every stored image is rechecked once; the
   existing tests prove the recompile rather than the entry.
4. **The manual says what is not there.** `/help` names RSA as admitted and says Ed25519 is absent
   from the framework, so the next author does not add it to the list and wonder why it fails to
   compile.

## Not in scope

- **Where the key lives.** A key in page source is readable through `/pages` and the history; that is
  the secrets shape in [`a-page-cannot-keep-a-secret`](a-page-cannot-keep-a-secret.md), and this item does
  not create a store.
- **The wire format.** Building the signature base, canonicalising JSON and the `Digest` header are
  page code and [`outbound-headers-are-an-allow-list`](outbound-headers-are-an-allow-list.md); the
  policy only decides what the page may call.
- **Ed25519.** Deferred until the framework has a primitive or the owner asks for a managed one.

## Open questions

None. The owner chose on 2026-11-22 to open the policy rather than add a signing capability, and to
defer Ed25519 rather than ship a hand-written primitive.

**Origin:** the owner's ask 2 of 2026-11-22, whose node could not verify an inbound `Follow` because
no asymmetric primitive is admitted.
