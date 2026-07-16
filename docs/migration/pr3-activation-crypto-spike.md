# PR 3 — Activation cryptography spike

**Date:** 2026-07-15  
**Status:** Decision recorded  
**Scope:** Proof-of-possession algorithm for Branch Server activation (not LAN TLS, not mTLS).

## Question

Which signature algorithm should bind Cloud activation exchange to the Branch Server?

## Candidates

| Option | Stack                                                    |
| ------ | -------------------------------------------------------- |
| A      | Ed25519 via a third-party NuGet                          |
| B      | ECDSA P-256 + SHA-256 via `System.Security.Cryptography` |

## Findings

### Built-in Ed25519

.NET 10 does **not** ship a first-party Ed25519 API in `System.Security.Cryptography` with a clear Windows + Linux support matrix comparable to ECDSA. Runtime issues tracking Ed25519 remain blocked on underlying platform crypto (notably incomplete/non-uniform OS support). Adding Ed25519 therefore requires a **third-party** library for both Branch (Windows Service) and Cloud (Linux containers).

### Option A — Ed25519 library

Would need package evaluation (license, maintenance, NuGet Audit, import/export, zeroization). Even a clean package increases supply-chain and review cost for PR 3 without a product requirement that demands Ed25519 specifically.

### Option B — ECDSA P-256 + SHA-256

- Available through official `ECDsa.Create(ECCurve.NamedCurves.nistP256)`.
- Sign/verify with `HashAlgorithmName.SHA256`.
- Runs on Windows and Linux under .NET 10.
- No extra crypto NuGet for this ceremony.
- NuGet Audit surface unchanged for the algorithm itself.
- Private material exportable as PKCS#8 / public as SubjectPublicKeyInfo; callers can clear buffers after copy.

Unit spike coverage (see `BranchActivationCryptoTests`): key generation, sign, verify, wrong signature reject, malformed key reject, import/export round-trip, fingerprint stability.

## Decision

**Use ECDSA P-256 + SHA-256** (`System.Security.Cryptography` only).

Do **not** use HMAC as the permanent installation identity: Cloud must verify possession without sharing equivalent secret material.

## Fixed wire formats (single algorithm — no runtime multi-alg abstraction)

| Constant                    | Value                                                                                           |
| --------------------------- | ----------------------------------------------------------------------------------------------- |
| `Algorithm`                 | `ECDSA_P256_SHA256`                                                                             |
| `PublicKeyFormat`           | Base64Url(SubjectPublicKeyInfo DER)                                                             |
| `SignatureFormat`           | Base64Url(IEEE P1363)                                                                           |
| `FingerprintFormat`         | lowercase hex SHA-256 of SPKI DER bytes                                                         |
| `CanonicalChallengePayload` | length-prefixed UTF-8 fields (uint16 BE length + bytes), version `binexus-branch-activation-v1` |

Canonical field order:

1. `binexus-branch-activation-v1`
2. `challengeId` (Guid `D`)
3. `branchInstanceId` (Guid `D`)
4. `publicKeyFingerprint`
5. `installationTokenHash`
6. `nonce` (Base64Url)
7. `expiresAtUtc` (ISO-8601 round-trip `"O"`)

Cloud verifies every signed field against the challenge row and the exchange request. Activation code validation is separate from the signature payload (code is not embedded in the challenge).

## Out of scope

- Algorithm agility / pluggable crypto providers
- Certificate issuance / mTLS
- Ed25519 reconsideration unless .NET gains first-party cross-platform support
