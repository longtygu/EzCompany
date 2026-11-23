# Changelog

Tất cả thay đổi đáng chú ý của dự án **Ez Company** được ghi lại tại đây theo chuẩn [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) và [Semantic Versioning 2.0.0](https://semver.org/).

## [0.1.0] - 2026-11-24 — Phase 1 MVP

### Added
- **Kiến trúc Modular Monolith (`EzCompany.sln`):** 11 project `.NET 8.0-windows` tách biệt `Core.Abstractions`, `Core.Crypto`, `Core.Data`, `Core.LanAgent`, `Core.Telemetry`, `Core.Updater`, 3 module nghiệp vụ (`Accounting`, `Legal`, `Office`), ứng dụng WPF `EzCompany.App` (`WPF-UI 4.3.0`, Fluent Design Dark/Light) và bộ test `EzCompany.Tests`.
- **Quản trị Open-Source MPL-2.0:** `LICENSE`, `ARCHITECTURE.md`, `IMPLEMENTATION_PLAN.md`, `CONTRIBUTING.md`, `SECURITY.md`, `.github/CODEOWNERS`, và `README.md`.
- **Mật mã ứng dụng & Giấy phép Offline (`EzCompany.Core.Crypto`):**
  - Ed25519 Root/Device keypairs, bảo vệ khóa bí mật bằng Windows DPAPI (`DataProtectionScope.CurrentUser`).
  - Dẫn xuất cặp khóa mã hóa công ty X25519 qua HKDF-SHA256 dùng chung cho 2 máy Giám đốc.
  - Master Recovery Kit (`.recovery`) mã hóa Argon2id + AES-256-GCM.
  - `PolicyEngine` ký/xác thực `policy.lic` + `ClockGuardService` (`clock.guard` chống tua ngược đồng hồ Windows quá 5 phút và chống hạ `PolicyVersion`).
- **CSDL Cục bộ Dual-SQLite (`EzCompany.Core.Data`):**
  - `AppDbContext` (`app.db`) và `TelemetryDbContext` (`telemetry.db`) bật `PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;`, khóa chính `Guid`, EF Core 8 Migrations, `DatabaseAuditLogger` và `GenesisBootstrapService`.
- **Mạng LAN P2P Zero-SMB (`EzCompany.Core.LanAgent`):**
  - Tự động phát hiện qua mDNS (`_ezcompany._tcp.local.`, `Makaretu.Dns.Multicast.New 0.38.0`) và UDP Multicast/Broadcast fallback (`45821`) hỗ trợ giả lập `--profile=<tên>` trên 1 PC.
  - Ghép đôi chống MITM bằng mã SAS 6 số (`XXX-XXX`), băm mật khẩu Argon2id hoàn toàn cục bộ.
  - Embedded Kestrel Minimal API (`port 0`), `DirectorSignatureAuthMiddleware` (Ed25519 + ±60s + `NonceReplayCache` 2 phút), `VaultPathGuard`, sao lưu không khóa máy nhân viên via `VACUUM INTO`, Pending Policy Queue và đồng bộ 2 máy Giám đốc.
- **Telemetry Phương án B & Executive Dashboard (`EzCompany.Core.Telemetry`):**
  - Active/Idle (`GetLastInputInfo` + `SessionSwitch`), thống kê ứng dụng kèm `PrivacySanitizer` che tiêu đề nhạy cảm.
  - Chụp riêng cửa sổ `WorkspaceWindow` (`RenderTargetBitmap`) + mã hóa đầu cuối X25519/ECIES + dọn dẹp FIFO (7 ngày / 200 MB).
  - Bảng tổng quan Giám đốc (`ExecutiveDashboardView`) với 5 khu vực quản trị và Director Cache xem lại ảnh offline.
- **3 Phân hệ Nghiệp vụ MVP & Read-Only DB Inspector:**
  - `accounting.invoice_xml`: đọc XML NĐ 123/2020 & NĐ 70/2025, phát hiện trùng số/MST & lệch cộng dồn (>10đ), xuất Excel (`ClosedXML`) kèm chuyển số tiền VND sang chữ tiếng Việt.
  - `legal.contract_diff`: trích xuất đoạn văn và bảng biểu `.docx` (`DocumentFormat.OpenXml`), so sánh Side-by-Side/Inline (`DiffPlex`), lưu Vault + SHA-256, xuất báo cáo `.html` và `.docx`.
  - `office.pdf_studio`: gộp, tách, xoay và đóng dấu Watermark chéo Unicode (`PDFsharp 6.2.4` MIT + `PdfPig`).
  - `ReadOnlyDbInspectorService`: mở bản chụp `.db` ở chế độ `Mode=ReadOnly` không trộn vào DB Giám đốc.
- **Tự cập nhật & CI/CD (`EzCompany.Core.Updater`, `.github/workflows/`):**
  - Velopack `GithubSource` (`https://github.com/longtygu/EzCompany`), kiểm soát `MaxApprovedVersion` theo SemVer 2.0, `SafeMigrationService` tự sao lưu SHA-256 và Rollback tự động khi lỗi migration.
  - Workflows `ci-pr.yml` và `release.yml` (Velopack pack + `SHA256SUMS.txt` + SLSA provenance).
- **Runtime File Logging & Bộ script test cục bộ:**
  - `RollingFileLoggerProvider` ghi nhật ký chi tiết hàng ngày vào `%LocalAppData%\EzCompany\Profiles\<profile>\Logs\ezcompany-yyyyMMdd.log` (kèm HTTP request log của Kestrel LAN Agent và nút mở nhanh thư mục Logs trong app).
  - Script `scripts/run-local-tests.ps1` và `scripts/start-multi-profile-demo.ps1`.
