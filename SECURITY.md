# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| `0.1.x` (Phase 1 MVP) | Yes |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report cryptographic, authentication, LAN authorization, path-traversal, or update-pipeline vulnerabilities privately through **GitHub Security Advisories** ("Report a vulnerability" in the repository's Security tab).

Please include:
1. Component affected (`Core.Crypto`, `Core.LanAgent`, `Core.Telemetry`, `Core.Updater`, or module name).
2. Minimal reproduction steps (preferably using `--profile` simulation).
3. Impact assessment and suggested remediation if known.

## Threat Model Scope

Ez Company is a **local-first, zero-24/7-server** Windows desktop system operating over trusted/semi-trusted corporate LANs:
- **Protected by design:**
  - Tampered `policy.lic` licenses and clock rollbacks (`Ed25519` + `clock.guard` DPAPI).
  - Unauthenticated or replayed LAN administrative requests (`DirectorSignatureAuthMiddleware`, ±60s window, 2-minute nonce cache).
  - LAN Pairing MITM attacks (6-digit SAS verification derived from Root + Device public keys and fresh nonces).
  - Vault directory path traversal (`VaultPathGuard`).
  - Telemetry snapshot leakage at rest or in transit (X25519 + HKDF-SHA256 + AES-256-GCM ECIES; employee nodes cannot decrypt captured images).
  - Unapproved or broken upgrades (`MaxApprovedVersion` gate + `SafeMigrationService` automatic rollback).
- **Out of scope:** Physical root/administrator compromise of a workstation while unlocked, or compromise of the company `.recovery` file paired with weak recovery passphrases.
