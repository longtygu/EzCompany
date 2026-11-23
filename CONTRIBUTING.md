# Contributing to Ez Company

Thank you for contributing to **Ez Company**! All contributions are licensed under the **Mozilla Public License 2.0 (MPL-2.0)**.

## Strict Open-Source Governance Rules
1. **Zero Hardcoded Company Data:** Never commit real company names, tax codes, logos, internal forms, passwords, PATs, or static IP addresses. All company metadata is created at runtime during Genesis Bootstrap or unit-test setup.
2. **No Placeholders:** Do not submit `// TODO` stubs or `NotImplementedException` methods in production paths.
3. **Security-Critical Paths:** Changes to `src/EzCompany.Core.Crypto/`, `src/EzCompany.Core.LanAgent/`, `src/EzCompany.Core.Updater/`, `src/EzCompany.Core.Telemetry/`, and `.github/workflows/` require review from security code owners defined in `.github/CODEOWNERS`.

## Local Multi-Machine Simulation (`--profile`)
You can simulate an entire company LAN on one Windows PC without multiple virtual machines:

```powershell
dotnet build EzCompany.sln
# Terminal 1: Genesis Director
dotnet run --project src/EzCompany.App -- --profile=director1
# Terminal 2: Accountant
dotnet run --project src/EzCompany.App -- --profile=accountant1
# Terminal 3: Legal Counsel
dotnet run --project src/EzCompany.App -- --profile=legal1
```

Each `--profile=<name>` isolates:
- Storage root: `%LocalAppData%\EzCompany\Profiles\<name>\`
- Dynamic Kestrel port (`port 0`)
- Deterministic simulated `MachineId` (`EZ-<SHA256-prefix>`)

Set `EZCOMPANY_TELEMETRY_DEBUG=1` to shorten the internal window-snapshot interval to 60 seconds during local verification.

## Building a Community `IAppModule`
Community modules plug into `EzCompany.Core.Abstractions` without modifying core projects:
1. Create a class library targeting `net8.0-windows` referencing `EzCompany.Core.Abstractions` (and optionally `EzCompany.Core.Data`).
2. Implement `IAppModule`:
   - `Metadata` (`ModuleId`, `DisplayName`, `Description`, `IconGlyph`, `Version`, `Category`, `DisplayOrder`, `RequiredPermissions`)
   - `RegisterServices(IServiceCollection services)`
   - `CreateView(IServiceProvider serviceProvider)` returning a WPF `FrameworkElement`
   - `InitializeAsync(CancellationToken ct)`
3. Check permissions via `IPermissionService` and log actions via `IAuditLogger`.
4. Register the module in `EzCompany.App` DI setup and add unit tests in `tests/EzCompany.Tests`.
