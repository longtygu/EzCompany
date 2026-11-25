# Changelog

Tất cả thay đổi đáng chú ý của dự án **Ez Company** được ghi lại tại đây theo chuẩn [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) và [Semantic Versioning 2.0.0](https://semver.org/).

## [0.3.0] - 2026-11-26 — Appearance & Backdrop Fix, App Icon, Multi-Role Overview, P2P Chat, Timeline, Diagnostics & HR

### Sửa lỗi (Fixed)
- **Giao diện & Chất liệu cửa sổ:** Sửa lỗi các thiết lập giao diện không áp dụng ngay và lỗi màu ứng dụng khi đổi chất liệu (`Mica` / `Acrylic` / `None`) trong `WindowTransparencyHelper`, `AppearanceSettingsService` và `App.xaml`.
- **Hiển thị song ngữ chức danh:** Bổ sung nhãn tiếng Việt cạnh chức danh trên toàn ứng dụng (`Manager (Giám đốc)`, `Accounting (Kế toán)`, `Legal (Pháp chế)`, `HR (Nhân sự)`, `Staff (Nhân viên)`).

### Thêm mới (Added)
- **Biểu tượng ứng dụng & Logo công ty:** Tạo bộ icon ứng dụng mới (`EzCompany.png` / `EzCompany.ico`) và `CompanyBrandingService` cho phép tải lên/thay đổi logo công ty, đồng bộ hiển thị tại màn hình Đăng nhập và Tổng quan trên tất cả máy.
- **Tab Tổng quan (`Tổng quan`) & Đa chức vụ:** Đổi tên tab Chào mừng thành `Tổng quan`, gộp trung tâm thiết lập thành tab `Cài đặt` ở dưới cùng, hỗ trợ gán nhiều chức vụ cho một nhân viên (ví dụ Kế toán + Pháp chế) với các tab chức vụ con và công cụ tương ứng.
- **Chat nội bộ P2P & Ghim việc:** Bổ sung `ChatWorkspaceView` và `ChatService` (chat nhóm & chat riêng), gửi kèm tệp tin lưu trữ cục bộ và đồng bộ P2P giữa các máy, ghim công việc được giao ngay trong nhóm chat.
- **Lịch trình kế hoạch dùng chung & Giao việc nâng cao:** Thêm `TimelinePlanService` cho phép bất kỳ thành viên nào bổ sung/theo dõi kế hoạch dùng chung trong tab `Tổng quan`; bổ sung đính kèm tệp khi giao việc và thông báo tới người nhận (`TaskAssignedNotificationEvent`).
- **Chẩn đoán, Báo lỗi & Tự sửa lỗi:** Bổ sung tab xem log, xuất gói báo lỗi `.zip` và công cụ quét tự sửa lỗi mạng/hiệu năng/DB (`AppDiagnosticsAndRepairService`) trong `Cài đặt`.
- **Quản lý Hồ sơ Nhân sự & Chấm công:** Bổ sung phân hệ `HrAttendanceView` (`HrAttendanceService`) và xuất báo cáo chấm công (`AttendanceExcelExporter`), đồng thời nâng cấp schema SQLite lên v3 (`20261126000001_AddChatTimelineHrAndLogo`).

## [0.2.0] - 2026-11-25 — UI Customization, Welcome Dashboard, P2P Tasks & Account Recovery

### Added & Changed
- **Giao diện chung tùy biến (`AppearanceSettingsService` + `AccountAndAppearanceView`):** Tùy chỉnh trực tiếp cỡ chữ (`11–20 pt`), độ rộng thẻ nội dung (`640–1600 px`), bo góc & khoảng đệm thẻ, độ rộng thanh điều hướng, màu chủ đề (Accent Color presets + mã Hex tùy chọn), chế độ Sáng/Tối, hiệu ứng nền (`Mica`/`Acrylic`/`Tabbed`/`None`) và độ trong suốt cửa sổ (`70%–100%` qua Win32 layered window không ảnh hưởng resize/backdrop), lưu bền vững theo từng `--profile`.
- **Giao diện Chào mừng sau đăng nhập (`WelcomeDashboardView` + `VietnameseLunarCalendar`):**
  - Lời chào `"Xin chào [Tên nhân viên]"` đặt tại góc trên bên trái cùng lịch **Dương lịch** và **Âm lịch Việt Nam (UTC+7, Can Chi)** tính toán 100% offline.
  - Theo dõi **Task hôm nay (tự tạo)**, **Task được giao**, **Tiến độ hoàn thành (%)**, và **Lịch sử hoàn thành các task**.
- **Đổi tên, Đổi mật khẩu & Giao việc P2P + Nhận bàn giao (`WorkTaskService` + `TaskManagementView` + EF Core Migration v2):**
  - Cả Giám đốc lẫn Nhân viên đều có thể đổi họ tên hiển thị, tên đăng nhập và mật khẩu cục bộ (băm lại Argon2id + cập nhật `policy.lic`/đồng bộ tên tới Giám đốc).
  - Bổ sung giao việc & nhận bàn giao kết quả (kèm tệp đính kèm Vault + kiểm chứng SHA-256 + lịch sử giao việc & nghiệm thu) hỗ trợ cả **Giám đốc ↔ Nhân viên** và **Nhân viên ↔ Nhân viên** qua P2P LAN có ký xác thực thiết bị + hàng đợi offline.
- **Màn hình Đăng nhập căn giữa & Khôi phục / Cấp lại tài khoản (`MainWindow` + `ExecutiveDashboardView` + `AccountRecoveryManager`):**
  - Căn giữa thẻ đăng nhập cho cả Giám đốc và Nhân viên.
  - Nhân viên có nút **Yêu cầu cấp lại tài khoản / mật khẩu** gửi yêu cầu ký số thiết bị lên máy Giám đốc và nhận vé `PasswordResetTicket` ký số Ed25519 để đặt lại mật khẩu cục bộ (không truyền mật khẩu qua mạng LAN).
  - Giám đốc có nút **Khôi phục tài khoản** bằng tệp `.recovery` Master Recovery Kit ngay trên màn hình đăng nhập.

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
