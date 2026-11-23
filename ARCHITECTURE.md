# Ez Company — System Architecture (Phase 1 MVP)

## 1. Overview & Design Philosophy

**Ez Company** is an open-source (MPL-2.0), local-first Windows Desktop application built on **.NET 8 (`net8.0-windows`)** and **WPF (`WPF-UI` Fluent Design)** using a **Modular Monolith** architecture. It is designed for small-to-medium enterprises (e.g., 2 Directors, 2 Accountants, 1 Legal Counsel, 2 Staff) operating without a dedicated 24/7 central server.

### Core Architectural Tenets
1. **Single Package Distribution:** One installer (`Setup.exe` / portable `.zip`) runs on every workstation. Node behavior (`Director` vs. `Employee`) is determined solely by local genesis state and cryptographic keys—never by separate builds.
2. **Local-First & Offline Capable:** All business and telemetry data live locally in SQLite (WAL mode). Employees sign in using local Argon2id hashes and verify signed `policy.lic` licenses 100% offline, even when all Director PCs are turned off.
3. **Zero Hardcoded Company Data:** No company names, tax codes, logos, templates, credentials, PATs, or static IP addresses exist anywhere in the source tree.
4. **Separated Window Lifecycle:**
   - `MainWindow`: Minimalist bootstrap & authentication shell (Genesis Bootstrap, Secondary Director Restore via `.recovery`, LAN Pairing, or Offline Login).
   - `WorkspaceWindow`: Main operational workbench, dynamically rendering `IAppModule` navigation entries permitted by `policy.lic`, plus the `ExecutiveDashboardView` and `DbInspectorView` on Director nodes.
5. **Multi-Machine Simulation (`--profile=<name>`):** Isolated per-profile data directories under `%LocalAppData%\EzCompany\Profiles\<profile>\`, OS-assigned dynamic LAN ports (`port 0`), and deterministic simulated `MachineId` values allow full multi-workstation LAN workflows on a single PC.

---

## 2. Solution Modular Monolith (`EzCompany.sln`)

| Project | Role & Responsibility | Dependencies |
|---|---|---|
| `EzCompany.Core.Abstractions` | Pure shared contracts (`IAppModule`, `ModuleMetadata`, `IPermissionService`, `IAuditLogger`, `ILocalStoragePaths`, `IEventBus`, `ISessionContext`) enabling community module extensions without touching core code. | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `EzCompany.Core.Crypto` | Ed25519 signing/verification, X25519/HKDF/AES-256-GCM ECIES, Argon2id password hashing & KDF, Windows DPAPI protection, 6-digit SAS derivation, `.recovery` kit sealing, `policy.lic` engine, and `clock.guard` rollback protection. | `Core.Abstractions`, `BouncyCastle.Cryptography`, `Konscious.Security.Cryptography.Argon2`, `System.Security.Cryptography.ProtectedData` |
| `EzCompany.Core.Data` | EF Core 8 SQLite dual-database layer (`app.db` & `telemetry.db`) with `WAL` + `foreign_keys=ON`, Guid primary keys, migrations, `DatabaseAuditLogger`, and `GenesisBootstrapService`. | `Core.Abstractions`, `Core.Crypto`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` |
| `EzCompany.Core.LanAgent` | Zero-config mDNS (`_ezcompany._tcp.local.`) + UDP Broadcast/Multicast fallback discovery, embedded Kestrel Minimal API (`port 0`), `DirectorSignatureAuthMiddleware` (Ed25519 + timestamp + nonce replay cache), `VaultPathGuard`, SQLite `VACUUM INTO` snapshot streaming, Pending Policy Queue, and 2-Director registry sync. | `Core.Abstractions`, `Core.Crypto`, `Core.Data`, `Makaretu.Dns.Multicast.New`, `Microsoft.AspNetCore.App` |
| `EzCompany.Core.Telemetry` | Antivirus-safe Method B local telemetry: Win32 `GetLastInputInfo` + `SessionSwitch` presence tracking, foreground app tracking with `PrivacySanitizer`, `RenderTargetBitmap` capture restricted strictly to `WorkspaceWindow`, ECIES end-to-end encryption, FIFO Ring Buffer retention (7 days / 200 MB), and Director delta pull service. | `Core.Abstractions`, `Core.Crypto`, `Core.Data` |
| `EzCompany.Core.Updater` | Velopack public GitHub Releases integration (`GithubSource`, zero PAT), SemVer `MaxApprovedVersion` gate (Directors exempt for pre-release testing), and `SafeMigrationService` (pre-migration SHA-256 backup + automatic rollback on failure). | `Core.Abstractions`, `Core.Crypto`, `Core.Data`, `Velopack` |
| `EzCompany.Modules.Accounting` | `accounting.invoice_xml`: Vietnamese e-invoice XML parser (ND 123/2020 & ND 70/2025/ND-CP), batch duplicate & arithmetic discrepancy (>10 VND) detector, Vault storage, ClosedXML `.xlsx` export with Vietnamese amount-in-words. | `Core.Abstractions`, `Core.Data`, `ClosedXML` |
| `EzCompany.Modules.Legal` | `legal.contract_diff`: Offline `.docx` paragraph + table extraction (`DocumentFormat.OpenXml`), `DiffPlex` Side-by-Side & Inline diff viewer, Prev/Next change navigation, Vault storage with SHA-256, offline `.html` and `.docx` audit reports. | `Core.Abstractions`, `Core.Data`, `DocumentFormat.OpenXml`, `DiffPlex` |
| `EzCompany.Modules.Office` | `office.pdf_studio`: Offline Local PDF Studio (`PDFsharp` + `PdfPig`) with Merge (reorderable), Split (page range parser), Rotate (90/180/270), and diagonal Unicode Watermark + quick Vault save. | `Core.Abstractions`, `Core.Data`, `PDFsharp`, `PdfPig` |
| `EzCompany.App` | WPF executable (`VelopackApp.Build().Run()` entry point, `Microsoft.Extensions.Hosting`, `WPF-UI 4.3.0`, `CommunityToolkit.Mvvm`), `MainWindow`, `WorkspaceWindow`, `ExecutiveDashboardView`, `DbInspectorView`, dynamic `IAppModule` loader. | All Core + Module projects |
| `EzCompany.Tests` | xUnit verification suite covering crypto, `clock.guard`, LAN auth & traversal & `VACUUM INTO`, telemetry sanitizer & ECIES & FIFO, all 3 modules + DB inspector, SemVer gate, migration rollback, and workflow governance. | All projects, `xunit`, `Microsoft.NET.Test.Sdk` |

---

## 3. NuGet Stability & Compatibility Decisions

All packages are pinned to verified `.NET 8.0-windows` compatible releases:
- **`Makaretu.Dns.Multicast.New` (0.38.0):** Chosen over the legacy unmaintained `Makaretu.Dns.Multicast` (`0.27.0`, which only targets `netstandard1.4/2.0` and `net461`). `Makaretu.Dns.Multicast.New` preserves the `Makaretu.Dns` API surface while explicitly targeting `net8.0` and resolving modern socket/dependency compatibility warnings.
- **Microsoft.Extensions.* & EF Core 8 (`8.0.x`):** Pinned explicitly to `8.0.1` / `8.0.34` / `8.0.0` rather than .NET 9/10 packages to match the `net8.0-windows` target framework.
- **`PDFsharp` (6.2.4) + `PdfPig` (0.1.16):** `PDFsharp 6.2.4` is the latest MIT-licensed `.NET 8` release (compatible with Ez Company's MPL-2.0 open-source license; `PDFsharp 6.5.0` switched to AGPLv3/Commercial). It uses a custom thread-safe `WindowsPdfFontResolver` (`IFontResolver`) reading Windows system TrueType fonts (`arial.ttf`, `segoeui.ttf`) for full Vietnamese diacritic support.

---

## 4. Cryptographic Trust Chain & Security Architecture

### 4.1 Key Hierarchy
1. **Root Identity (`Ed25519`):** Generated during Genesis Bootstrap by Director 1; exported inside the password-protected `.recovery` kit (Argon2id `m=65536, t=3, p=2` + AES-256-GCM) so Director 2 shares the exact same Root Key (Approach A). Protected at rest on Director machines via Windows DPAPI (`DataProtectionScope.CurrentUser`).
2. **Company Telemetry Encryption Key (`X25519`):** Deterministically derived from the `Ed25519` Root Private Key via `HKDF-SHA256(ikm: rootPriv, salt: "EzCompany.v1", info: "x25519-telemetry-encryption")`. Both Director 1 and Director 2 derive the identical `X25519` private key without separate key distribution, while employee workstations only receive the pinned `CompanyEncryptionPublicKey`.
3. **Device Identity (`Ed25519`):** Generated locally on every workstation (`device.key.dpapi` + `device.pub`).

### 4.2 Offline Policy & Anti-Rollback (`policy.lic` + `clock.guard`)
- `policy.lic` contains JSON payload + Ed25519 signature (`RootPrivateKey`).
- Offline verification enforces:
  1. Valid Ed25519 signature against locally pinned `RootPublicKey`.
  2. Exact `MachineId` and `DevicePublicKey` binding.
  3. Monotonically non-decreasing `PolicyVersion >= LastAcceptedPolicyVersion`.
  4. Anti-clock-rollback via DPAPI-protected `clock.guard` (`LastRunTimestampUtc`): if current system UTC is more than 5 minutes behind `LastRunTimestampUtc`, or exceeds `ExpiresAtUtc` (default 14 days), modules lock immediately.

### 4.3 Zero-SMB LAN Security & MITM-Resistant Pairing
- **SAS 6-Digit Pairing:** Employee and Director compute `XXX-XXX` from `SHA-256(RootPublicKey || DevicePublicKey || ClientNonce || ServerNonce)` and visually verify parity before Director approval. Passwords are Argon2id-hashed locally on the employee workstation and never cross the network.
- **Signed Admin API Requests:** Every administrative endpoint requires:
  - `X-Ez-Timestamp` (within ±60 seconds of server UTC)
  - `X-Ez-Nonce` (unique within 2-minute sliding replay cache)
  - `X-Ez-Admin-Name`
  - `X-Ez-Signature` = Ed25519 signature over `METHOD|PATH_AND_QUERY|TIMESTAMP|NONCE|SHA256_HEX(BODY)`

---

## 5. Dual-Database SQLite Architecture & Telemetry (Method B)

- **`app.db`:** `CompanyProfiles`, `LocalUsers`, `PairedDevices`, `AuditLogs`, `AccountingInvoices`, `LegalContractReviews`, `OfficeDocumentRecords`.
- **`telemetry.db`:** `PresenceSessions`, `AppUsageRecords`, `EncryptedSnapshots`, `SyncCheckpoints`.
- Every connection executes `PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;`.
- **Snapshot Replication (`VACUUM INTO`):** Allows Directors to pull a transactionally consistent backup of an employee's `app.db` while the employee continues active SQLite writes without file locks.
- **Antivirus-Safe Telemetry (Method B):** Uses only standard `GetLastInputInfo`, `SystemEvents.SessionSwitch`, foreground window title polling + `PrivacySanitizer`, and WPF `RenderTargetBitmap` scoped exclusively to `WorkspaceWindow` (no keyboard hooks, no screen/desktop capture APIs). Plaintext JPEG bytes are immediately encrypted with `EciesCryptoService` and zeroed in memory.

---

## 6. Safe Auto-Update & CI/CD Pipeline

- **Velopack Public Releases:** `GithubSource` fetches from public GitHub Releases (`appsettings.json`, zero PATs). `UpdatePolicyGate` enforces `MaxApprovedVersion` (SemVer 2.0) for employees while allowing Directors to preview newer builds.
- **`SafeMigrationService`:** Checkpoints WAL, clears SQLite pools, archives `app.db` + `telemetry.db` + SHA-256 `manifest.json` under `Backups/PreMigration_.../`, runs EF Core migrations, and automatically restores byte-for-byte on exception.
- **GitHub Actions:**
  - `ci-pr.yml`: Read-only sandbox (`contents: read`) verifying `dotnet build -c Release` and `dotnet test`.
  - `release.yml`: Tag-triggered (`v*.*.*`) Windows release build producing Velopack delta/full packages, `Setup.exe`, portable `.zip`, `SHA256SUMS.txt`, and SLSA build provenance attestation (`actions/attest-build-provenance`).
