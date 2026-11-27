# Changelog

Tất cả thay đổi đáng chú ý của dự án **Ez Company** được ghi lại tại đây theo chuẩn [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) và [Semantic Versioning 2.0.0](https://semver.org/).

## [0.6.0] - 2026-11-27 — Enterprise Suite (Tray/Startup, Avatars, Chat Upgrades, Tasks, Leave/Expense Approvals, Payslip PDF, Accounting Cashbook, Legal Contracts, Shared Vault, PDF Studio, KPI Charts & Auto-Backup)

### Added — Enterprise Suite 10 Upgrades & Fixes
- **System Tray & Khởi động cùng Windows (`SystemTrayService`, `WindowsStartupService`, `AppearanceSettings`, `AccountAndAppearanceView`)**:
  - Đóng cửa sổ chính sẽ thu nhỏ ứng dụng xuống khay hệ thống (System Tray Win32 thuần, không kéo WinForms) kèm menu chuột phải `Mở Ez Company` / `Thoát ứng dụng hoàn toàn`.
  - Tùy chọn tự động khởi động cùng Windows trong Registry người dùng (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Ảnh đại diện cá nhân & Nâng cấp Chat nội bộ (`UserAvatarService`, `ChatWorkspaceView`, `ManageChatGroupWindow`)**:
  - Cho phép từng nhân viên tải ảnh đại diện cá nhân (tối đa 512 KB), đồng bộ P2P sang toàn công ty và hiển thị trên thanh nhân viên Chat.
  - Thêm tìm kiếm tin nhắn theo nội dung/người gửi/tệp, xem trước ảnh trực tiếp trong bong bóng chat & nhấn để phóng to (`SnapshotViewerWindow`), trả lời trích dẫn (`ReplyToMessageId` / `ReplyPreviewText`), và cửa sổ Quản lý nhóm chat (đổi tên nhóm, thêm/bớt thành viên đồng bộ qua LAN).
- **Quản lý công việc nâng cao (`WorkTaskService`, `TaskDeadlineReminderService`, `TaskManagementView`)**:
  - Nhắc hạn công việc sắp tới/quá hạn qua popup thông báo (`TaskDeadlineReminderService`), thanh tìm kiếm & lọc trạng thái/ưu tiên, giao việc cho nhiều nhân viên cùng lúc, checklist công việc con (`SubtasksJson`), và công việc lặp lại định kỳ (`Daily` / `Weekly` / `Monthly`) tự sinh việc kế tiếp khi nghiệm thu hoàn thành.
- **Quy trình Đơn Xin nghỉ phép / Nửa ngày / Đi muộn / Tăng ca (`LeaveRequestService`, `EmployeeRequestsView`, `HrAttendanceView`)**:
  - Nhân viên gửi đơn trực tiếp, đồng bộ qua LAN; khi Giám đốc hoặc HR phê duyệt, hệ thống tự động cập nhật bản ghi chấm công/tăng ca tương ứng.
- **Xuất & Gửi Phiếu lương PDF + Đồng bộ xóa chấm công + Gợi ý T7/CN/Lễ (`PayslipPdfExporter`, `HrAttendanceService`, `HrAttendanceView`, `AttendanceDetailViewModel`)**:
  - Xuất Phiếu lương PDF từng nhân viên và gửi trực tiếp vào hộp chat riêng 1-1.
  - Xóa ngày công theo cơ chế soft-delete tombstone (`IsDeleted`) đồng bộ LAN; bổ sung đánh dấu `T7`, `CN`, `Lễ` trên ma trận chấm công.
- **Kế toán & Pháp chế nâng cao (`AccountingLedgerService`, `AccountingLedgerExcelExporter`, `AccountingInvoiceView`, `ContractRegistryService`, `ContractRegistryExcelExporter`, `LegalContractDiffView`)**:
  - Tìm kiếm, lọc, cảnh báo và xóa hóa đơn XML; bổ sung Sổ quỹ Thu/Chi kèm xuất Excel và quy trình Đề xuất Tạm ứng / Thanh toán / Hoàn ứng (chi tiền tự ghi vào Sổ quỹ).
  - Bổ sung Sổ theo dõi Hợp đồng & hạn hiệu lực (`Active` / `ExpiringSoon` / `Expired`), đẩy mốc nhắc hết hạn sang Bảng Lịch trình chung và xuất Excel Danh mục Hợp đồng.
- **Kho Tài liệu Dùng chung Công ty & Studio PDF nâng cao (`SharedDocumentService`, `SharedDocumentsView`, `LocalPdfStudioService`, `OfficePdfStudioView`)**:
  - Kho tài liệu nội bộ đồng bộ metadata LAN và tải tệp ngược P2P kèm kiểm tra SHA-256.
  - Nâng cấp Văn phòng PDF với **Ảnh → PDF** nhiều trang và **Đóng dấu PNG Chữ ký / Con dấu** theo tọa độ % lên trang PDF.
- **Trung tâm Phê duyệt tập trung, Biểu đồ KPI & Tự động Sao lưu Giám đốc (`ExecutiveDashboardView`, `ExecutiveDashboardViewModel`, `DirectorAutoBackupService`)**:
  - Trung tâm duyệt tập trung Đơn nghỉ phép/OT và Đề xuất chi phí kèm badge thông báo trên thanh điều hướng.
  - Biểu đồ thanh ngang trực quan cho tỷ lệ hoàn thành việc và giờ làm việc/đi muộn theo tháng.
  - Lịch tự động sao lưu CSDL các máy trạm đang Online định kỳ kèm giữ lại N bản mới nhất/máy (`PruneBackupFiles`).

### Fixed & Improved (Milestone 10)
- **Sửa lỗi cửa sổ vẫn trong suốt ở mức `0% (Đặc)` (`AppearancePalette`, `WindowTransparencyHelper`, `AppearanceSettingsService`, `AccountAndAppearanceView`)**:
  - Khi độ trong suốt đặt về `0%`, cửa sổ tự ép `WindowSurface` và `Panel` về alpha `255` (đặc 100%), đồng thời tắt tạm hiệu ứng DWM (`Mica`/`Acrylic`/`Blur`) và đặt `CompositionTarget.BackgroundColor` thành nền đặc thay vì trong suốt.
- **Nâng cấp giao diện Chat nội bộ (`ChatWorkspaceView`, `ChatWorkspaceViewModel`, `ChatService`)**:
  - Thay nhãn `● Tự động cập nhật 1s` bằng trạng thái kết nối mạng/LAN thực tế: `Đã kết nối LAN` (chữ xanh lá `#22C55E`) hoặc `Mất kết nối` (chữ đỏ `#EF4444` nhấp nháy).
  - Thêm nút **Bỏ ghim** (`PinOff24`) ngay trên thẻ công việc đang ghim trong box chat và hỗ trợ đồng bộ gỡ ghim qua LAN (`UnpinTaskFromConversationAsync`).
  - Thay nút chữ `Làm mới` bằng biểu tượng mũi tên xoay vòng (`ArrowSync24`) với ToolTip `Làm mới`.
- **Nâng cấp mục Giám đốc (`ExecutiveDashboardView`, `ExecutiveDashboardViewModel`, `SnapshotViewerWindow`, `SnapshotViewerViewModel`)**:
  - Thiết kế lại mục số 2 từ lưới bảng sang danh sách thẻ mở rộng dạng khay (giống Hồ sơ nhân viên) kèm tìm kiếm; bấm từng máy để xem thông số chi tiết và cấu hình phân quyền / gửi thông báo / sao chép DB / duyệt Vault ngay bên dưới.
  - Bổ sung chọn, phóng to/thu nhỏ (Zoom `10%–800%`, vừa khung, lăn chuột, kéo rê ảnh, ảnh trước/sau), lưu ảnh và mở bằng ứng dụng xem ảnh mặc định cho mục số 4 (`SnapshotViewerWindow`).
- **Chấm công sửa linh hoạt mọi ngày (`HrAttendanceService`, `AttendanceDetailWindow`, `AttendanceDetailViewModel`, `HrAttendanceViewModel`)**:
  - Cho phép bấm trực tiếp vào bất kỳ ô ngày nào trong Bảng ma trận chấm công (kể cả ngày đã chấm công trước đó), tự tải dữ liệu hiện có lên khung chỉnh sửa để cập nhật hoặc xóa ngày công đó, đồng thời tự động đồng bộ trạng thái đã chấm / chưa chấm trên tab Chấm công tùy chỉnh.

## [0.5.0] - 2026-11-27 — Parallel Zoomable Timeline & Editor Window, Window Transparency %, Detachable Drag-to-Dock Chat & Presence Bar

### Added & Changed
- **Bảng lịch trình & kế hoạch chung (`ParallelTimelineControl` + `TimelineEditorWindow`)**:
  - Gom toàn bộ cụm tạo/sửa/cập nhật trạng thái/xóa kế hoạch vào cửa sổ riêng `TimelineEditorWindow`, mở bằng nút `+ Tạo kế hoạch / lịch trình`.
  - Cụm timeline hiển thị sẵn kể cả khi đang trống, thiết kế dạng các đường thẳng ngang song song với các chấm đánh dấu ngày tháng ở trên, đường nét đứt đánh dấu Hôm nay và các thanh kế hoạch kéo dài được tô màu.
  - Hỗ trợ phóng to/thu nhỏ (`Overview60Days` / `Normal30Days` / `Detailed14Days`), điều hướng thời gian (Lùi / Hôm nay / Tiến) và chuyển đổi nhanh giữa chế độ mặc định **3 đường song song** và mở rộng **5 đường song song**.
- **Tùy chỉnh độ trong suốt theo `%` (`AccountAndAppearanceView`)**:
  - Đồng bộ hai chiều phần trăm trong suốt cửa sổ (`0%–70%`) và thẻ (`0%–60%`) qua slider, ô nhập `%` và các nút preset nhanh (`0% / 15% / 30% / 50% / 70%`).
- **Chat nội bộ (`DetachedChatWindow`, `CreateChatGroupWindow`, `ChatWorkspaceView`)**:
  - Thêm nút tách cửa sổ chat thành cửa sổ riêng (`DetachedChatWindow`) và tự động thu hồi/gắn lại về cửa sổ chính khi kéo thả vào vùng giao diện chính (`ChatDockGeometry` + `ChatDockCoordinator`, giữ nguyên trạng thái phiên chat trực tiếp).
  - Đưa luồng tạo nhóm chat mới sang cửa sổ riêng `CreateChatGroupWindow` với bộ lọc tìm thành viên và trạng thái Online/Offline.
  - Thêm thanh nhân viên phía trên cùng của khung chat với ảnh đại diện tròn (fallback icon `Person24`), chấm sáng xanh nhấp nháy khi Online / đỏ nhấp nháy khi Offline; bấm trực tiếp để mở chat riêng 1-1.
  - Thay ComboBox chọn người chat riêng bằng ô tìm kiếm nhân viên xổ xuống theo tên hoặc chức vụ (hỗ trợ tìm không dấu tiếng Việt).

## [0.4.0] - 2026-11-27 — DWM Transparency & Card Tinting Fix, Realtime Chat + Stickers + Popups, Role Sidebar & HR/Payroll Upgrade

### Sửa lỗi (Fixed)
- **Độ trong suốt, Chất liệu cửa sổ & Màu thẻ trên Windows 10/11:** Loại bỏ cơ chế `WS_EX_LAYERED` gây lỗi, tạo `EzFluentWindow` giữ khung kính DWM (`GlassFrameThickness = -1`), nâng cấp `WindowTransparencyHelper` hỗ trợ Blur/Acrylic trên Windows 10 và Mica/Acrylic/Tabbed trên Windows 11, tạo `AppearancePaletteBuilder` pha màu chủ đề động vào nền thẻ/panel/viền trên toàn bộ XAML views, tắt ép màu accent hệ thống Windows và đồng bộ `ControlContentThemeFontSize`.

### Thêm mới & Nâng cấp (Added & Changed)
- **Chat nội bộ Realtime 1s, Popup nổi & Sticker tùy chỉnh:**
  - Nâng cấp đồng bộ LAN (`PeerCollabSyncService`) và UI (`ChatWorkspaceView`) lên nhịp 1 giây tối ưu băng thông/DB bằng truy vấn dấu mốc delta (`GetGlobalLatestChatActivityUtcAsync`, `GetMessagesAfterAsync`), không nạp lại toàn bộ tin nhắn.
  - Bổ sung cửa sổ popup nổi góc màn hình (`ChatToastPopupWindow` + `ChatNotificationPopupService`) khi có tin nhắn đến, bấm vào mở đúng hội thoại.
  - Bổ sung `CustomStickerService` cho phép tạo, thêm ảnh sticker tùy chỉnh (PNG/JPG/GIF/WebP/BMP), xóa, gửi ngay trên khay sticker và đồng bộ P2P sang máy nhận; giao diện bong bóng chat trái/phải, avatar chữ cái đầu, badge chưa đọc và phím `Enter` gửi nhanh.
- **Sidebar thu gọn/mở rộng dạng khay theo vị trí công việc:** Nhóm các công cụ theo khay `Expander` (`Không gian chung`, `Manager (Giám đốc)`, `HR (Nhân sự)`, `Accounting (Kế toán)`, `Legal (Pháp chế)`, `Staff & Office (Văn phòng)`).
- **Nhân sự, Chấm công tùy chỉnh, Cửa sổ chi tiết riêng & Tính lương Tự động:**
  - Danh sách hồ sơ nhân viên dạng thẻ `Expander` mở rộng khi bấm vào, hỗ trợ tìm kiếm, nút Thêm/Sửa/Xóa (có xác nhận) và cấu hình lương cơ bản/phụ cấp/công chuẩn/BHXH.
  - Hỗ trợ chấm công theo giờ tùy chỉnh (`HH:mm`), ca qua đêm, số công, giờ tăng ca và mở **cửa sổ chi tiết riêng** (`AttendanceDetailWindow`) với bảng ma trận full ngày 1→cuối tháng + chỉnh sửa trực tiếp.
  - Bổ sung phân hệ **Tính lương Tự động** theo công thực tế/tăng ca/BHXH và xuất Bảng lương Excel (`.xlsx`).
  - Nâng cấp CSDL SQLite lên Migration v4 (`20261127000001_AddChatStickerAndPayrollFields`).

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
