# Ez Company (Phase 1 MVP) — Implementation Plan & Checkpoints

Execute milestones strictly in order. After each milestone, run `dotnet build EzCompany.sln` and `dotnet test EzCompany.sln` (0 errors, 100% tests pass) before marking `[x]` and advancing.

## Milestone 0: Solution Structure & Open-Source Governance
- [x] Create root files: `ARCHITECTURE.md`, `IMPLEMENTATION_PLAN.md`, `LICENSE` (MPL-2.0), `CONTRIBUTING.md`, `SECURITY.md`, `.github/CODEOWNERS`, `.gitignore`, `Directory.Build.props`.
- [x] Create `EzCompany.sln` and all 11 projects (`Core.Abstractions`, `Core.Crypto`, `Core.Data`, `Core.LanAgent`, `Core.Telemetry`, `Core.Updater`, `Modules.Accounting`, `Modules.Legal`, `Modules.Office`, `EzCompany.App`, `EzCompany.Tests`).
- [x] Implement `EzCompany.Core.Abstractions` contracts (`IAppModule`, `ModuleMetadata`, `IPermissionService`, `IAuditLogger`, `ILocalStoragePaths`, `IEventBus`, `ISessionContext`).
- [x] Add Milestone 0 tests and verify `dotnet build` & `dotnet test` pass.

## Milestone 1: Core Crypto, Genesis Bootstrap & Local SQLite
- [x] Implement `EzCompany.Core.Crypto` (Ed25519, X25519/HKDF/AES-256-GCM ECIES, Argon2id, DPAPI store, `.recovery` kit service, SAS calculator, `policy.lic` engine, `clock.guard` anti-rollback, `PermissionService`).
- [x] Implement `EzCompany.Core.Data` (SQLite EF Core 8 WAL interceptor, `AppDbContext`, `TelemetryDbContext`, initial migrations, `DatabaseAuditLogger`, `GenesisBootstrapService`).
- [x] Implement initial WPF shell (`Program.cs` with `VelopackApp.Build().Run()` & `--profile`, `App.xaml` with WPF-UI Dark/Light theme, `MainWindow` 3-branch uninitialized + offline login, `WorkspaceWindow` dynamic module shell).
- [x] Write Milestone 1 xUnit tests (`policy.lic` tamper/rollback/expiry, `.recovery` roundtrip, Argon2id, WAL mode & migrations) and verify `dotnet build` & `dotnet test` pass.

## Milestone 2: LAN P2P (mDNS), SAS 6-Digit Pairing & Secure LAN Agent
- [x] Implement `LanDiscoveryService` (`Makaretu.Dns.Multicast.New` `_ezcompany._tcp.local.` + UDP Broadcast/Multicast fallback for `--profile` & LAN).
- [x] Implement SAS 6-digit Pairing protocol (`PairingSessionManager`, `/api/v1/pairing/*`, `LanPairingClient`) and wire Branch 3 of `MainWindow`.
- [x] Implement embedded Kestrel `LanAgentHost` (`port 0`), `DirectorSignatureAuthMiddleware`, `NonceReplayCache`, `VaultPathGuard`, `DatabaseSnapshotService` (`VACUUM INTO`), and `DirectorLanClient`.
- [x] Implement Pending Policy Queue & 2-Director Registry Sync (`DirectorFleetSyncService`) + notice display on `WorkspaceWindow`.
- [x] Write Milestone 2 xUnit tests (SAS, Kestrel auth middleware, replay/skew defense, path traversal block, concurrent `VACUUM INTO`, pairing roundtrip, pending queue & 2-Director sync) and verify `dotnet build` & `dotnet test` pass.

## Milestone 3: Telemetry (Method B) & Executive Dashboard
- [x] Implement `EzCompany.Core.Telemetry` (`PrivacySanitizer`, `Win32ActivityInterop`, `TelemetryCollectorService`, `WindowSnapshotService` using `RenderTargetBitmap` + ECIES, `TelemetryRetentionService` FIFO 7d/200MB).
- [x] Add signed Director delta-pull endpoints (`/api/v1/telemetry/delta`, `/api/v1/telemetry/snapshot/{id}`) and `DirectorTelemetryPullService` with local Director Cache.
- [x] Implement `ExecutiveDashboardView` + ViewModel (5 areas: KPIs, Workstations Grid + 1-click actions, Audit & App Usage, Encrypted Snapshot Timeline, Pairing & Release Governance Center).
- [x] Write Milestone 3 xUnit tests (`PrivacySanitizer`, ECIES encryption/decryption by Director 1 & 2, FIFO retention, telemetry delta & on-demand snapshot endpoints) and verify `dotnet build` & `dotnet test` pass.

## Milestone 4: 3 MVP Business Modules (`IAppModule`) & Read-Only DB Inspector
- [x] Implement `EzCompany.Modules.Accounting` (`accounting.invoice_xml`: NĐ 123/2020 & NĐ 70/2025 XML parsing, duplicate/math validation, Vault + `app.db`, ClosedXML `.xlsx` + Vietnamese number-to-words).
- [x] Implement `EzCompany.Modules.Legal` (`legal.contract_diff`: OpenXml paragraph + table extraction, DiffPlex Side-by-Side/Inline, Prev/Next change navigation, Vault + SHA-256 + `app.db`, offline `.html` & `.docx` reports).
- [x] Implement `EzCompany.Modules.Office` (`office.pdf_studio`: `PDFsharp` + `PdfPig` Merge, Split, Rotate, Unicode Watermark + Vault + `app.db`).
- [x] Implement Director Read-Only DB Inspector (`ReadOnlyDbInspectorService` + `DbInspectorView`).
- [x] Write Milestone 4 xUnit tests with generated synthetic fixtures for all 3 modules and DB Inspector, and verify `dotnet build` & `dotnet test` pass.

## Milestone 5: Velopack Auto-Update & GitHub Actions CI/CD
- [x] Implement `EzCompany.Core.Updater` (`UpdaterOptions`, `SemVerVersion`, `UpdatePolicyGate` enforcing `MaxApprovedVersion`, `VelopackUpdateService`, `SafeMigrationService` with SHA-256 backup & automatic rollback) and wire into `Program.cs` & `WorkspaceWindow`.
- [x] Create `.github/workflows/ci-pr.yml` and `.github/workflows/release.yml` (with `vpk` + `SHA256SUMS.txt` + SLSA attestation).
- [x] Write complete `README.md` in Vietnamese (+ English summary) with multi-profile `--profile` simulation walkthrough.
- [x] Write Milestone 5 xUnit tests (SemVer gate, migration rollback, workflow & zero-placeholder checks), validate YAML syntax, and verify `dotnet build -c Release` & `dotnet test -c Release` pass 100%.
