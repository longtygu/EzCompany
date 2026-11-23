# Ez Company (Phase 1 MVP)

> **Mã nguồn mở (MPL-2.0) • Local-First • Không cần Máy chủ 24/7 • Không viết cứng dữ liệu doanh nghiệp**

**Ez Company** là bộ phần mềm Windows Desktop (`.NET 8.0-windows`, WPF Fluent Design) dành cho doanh nghiệp vừa và nhỏ (Giám đốc, Kế toán, Pháp chế, Nhân viên). Toàn bộ dữ liệu nghiệp vụ và nhật ký lưu trữ cục bộ bằng **SQLite (WAL mode)** trên từng máy trạm; nhân viên đăng nhập và làm việc **100% offline** ngay cả khi máy Giám đốc đã tắt.

---

## Tính năng cốt lõi

1. **Một bản cài đặt duy nhất (Single Package):** Một tệp cài đặt cho toàn bộ công ty; vai trò *Giám đốc* hoặc *Nhân viên* được quyết định bởi dữ liệu khởi tạo (Genesis Bootstrap / `.recovery`) và giấy phép chữ ký số `policy.lic`.
2. **Bảo mật & Mật mã ứng dụng (`EzCompany.Core.Crypto`):**
   - Khóa gốc **Ed25519**, khóa thiết bị riêng từng máy, bảo vệ khóa bí mật bằng **Windows DPAPI** (`DataProtectionScope.CurrentUser`).
   - **Master Recovery Kit (`.recovery`):** Mã hóa Argon2id + AES-256-GCM để khôi phục sự cố và thiết lập **Máy Giám đốc thứ 2** (Hướng A — dùng chung khóa gốc).
   - **Offline Policy Engine (`policy.lic` + `clock.guard`):** Kiểm tra chữ ký Ed25519, khớp `MachineId` & `DevicePublicKey`, chống hạ `PolicyVersion`, chống tua ngược đồng hồ Windows quá 5 phút và giới hạn hiệu lực 14 ngày.
3. **Mạng nội bộ P2P Không cần SMB (`EzCompany.Core.LanAgent`):**
   - Tự phát hiện qua **mDNS** (`_ezcompany._tcp.local.`) kèm **UDP Broadcast/Multicast fallback**.
   - Ghép đôi chống MITM bằng mã **SAS 6 số (`XXX-XXX`)**; mật khẩu băm Argon2id cục bộ tại máy nhân viên, không bao giờ gửi qua LAN.
   - Kestrel Minimal API nhúng, xác thực bắt buộc chữ ký Giám đốc (`X-Ez-Timestamp`, `X-Ez-Nonce` chống Replay 2 phút, `X-Ez-Signature`), chặn Path Traversal (`VaultPathGuard`), sao lưu nguyên vẹn không khóa máy nhân viên bằng SQLite **`VACUUM INTO`**, **Pending Policy Queue**, và đồng bộ sổ giữa 2 máy Giám đốc.
4. **Telemetry An toàn trước Antivirus (`EzCompany.Core.Telemetry` — Phương án B):**
   - Active/Idle (`GetLastInputInfo` + `SessionSwitch`, ngưỡng 180s), thống kê ứng dụng kèm **`PrivacySanitizer`** tự động che tiêu đề nhạy cảm (ngân hàng, OTP, mật khẩu, Zalo, Messenger, Tab ẩn danh...).
   - Chỉ chụp nội bộ cửa sổ `WorkspaceWindow` bằng WPF `RenderTargetBitmap` (không dùng hook bàn phím hay API chụp toàn Desktop), mã hóa đầu cuối **X25519/ECIES** sao cho máy nhân viên không thể tự giải mã ảnh, dọn dẹp FIFO 7 ngày / 200 MB.
5. **3 Phân hệ Nghiệp vụ MVP (`IAppModule`) & Read-Only DB Inspector:**
   - **Kế toán (`accounting.invoice_xml`):** Đọc hàng loạt XML hóa đơn điện tử Việt Nam (NĐ 123/2020 & NĐ 70/2025), phát hiện trùng số/MST và sai lệch cộng dồn (> 10đ), lưu Vault + `app.db`, xuất Excel `.xlsx` kèm đọc tiền VND bằng chữ tiếng Việt.
   - **Pháp chế (`legal.contract_diff`):** So sánh hợp đồng `.docx` (cả đoạn văn và bảng biểu) offline, hiển thị Side-by-Side & Inline với `Prev/Next Change`, lưu Vault kèm SHA-256, xuất báo cáo `.html` và `.docx`.
   - **Văn phòng (`office.pdf_studio`):** Gộp PDF (kéo thả), tách trang (`1-3, 5`), xoay trang (`90°/180°/270°`), đóng dấu Watermark chéo Unicode offline.
   - **Read-Only DB Inspector:** Giám đốc xem bản chụp `.db` từ máy Kế toán/Pháp chế ở chế độ chỉ đọc (`Mode=ReadOnly`), không trộn vào DB của Giám đốc.
6. **Cập nhật an toàn (`EzCompany.Core.Updater`):** Velopack từ Public GitHub Releases, van kiểm soát `MaxApprovedVersion` theo SemVer, và `SafeMigrationService` tự sao lưu + Rollback tự động khi nâng cấp schema thất bại.

---

## English Summary

**Ez Company** is an open-source (**MPL-2.0**), local-first **.NET 8 (`net8.0-windows`) WPF** enterprise desktop suite built as a **Modular Monolith**. Workstations store business data locally in SQLite (WAL mode) and authenticate 100% offline via Argon2id and Ed25519-signed `policy.lic` licenses protected against clock rollback (`clock.guard`). Administrative orchestration runs over zero-config mDNS/UDP LAN discovery and an embedded Kestrel API with Ed25519 request signatures, 6-digit SAS pairing, non-blocking `VACUUM INTO` database snapshots, E2E X25519-encrypted window telemetry, and Velopack safe releases gated by `MaxApprovedVersion`.

---

## Yêu cầu môi trường, Đóng gói Setup & Lệnh Build / Test

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (hoặc .NET SDK >= 8.0 kèm Windows Desktop Pack)

### Tải bản cài đặt hoặc tự đóng gói `Setup.exe` cục bộ
- Tải trực tiếp bản phát hành chính thức tại [GitHub Releases](https://github.com/longtygu/EzCompany/releases) (mô tả chi tiết xem tại [docs/RELEASE_v0.1.0.md](docs/RELEASE_v0.1.0.md)).
- Hoặc tự tạo bộ cài đặt Velopack (`Setup.exe`, Portable `.zip`, `.nupkg`, `releases.win.json`, `SHA256SUMS.txt`) tại máy Windows của bạn:
```powershell
.\scripts\build-setup.ps1 -Version 0.1.0
```

### Build & Chạy kiểm thử từ mã nguồn
```powershell
dotnet restore EzCompany.sln
dotnet build EzCompany.sln -c Release
dotnet test EzCompany.sln -c Release
```

---

## Hướng dẫn chạy Giả lập Đa máy trên 1 PC (`--profile`)

Nhờ cờ `--profile=<tên>`, bạn có thể mô phỏng đồng thời nhiều máy trạm độc lập trên cùng một máy tính Windows (tự tách thư mục `%LocalAppData%\EzCompany\Profiles\<tên>\`, tự cấp cổng Kestrel riêng và `MachineId` riêng):

### Bước 1: Bật chế độ rút ngắn chu kỳ Telemetry (tùy chọn khi test)
```powershell
$env:EZCOMPANY_TELEMETRY_DEBUG = "1"
```

### Bước 2: Mở 3 (hoặc 4) cửa sổ PowerShell chạy song song

**Cửa sổ 1 — Máy Giám đốc 1 (Genesis Bootstrap):**
```powershell
dotnet run --project src/EzCompany.App -- --profile=director1
```
1. Chọn tab **1. Khởi tạo Công ty mới (Giám đốc gốc)**.
2. Nhập tên công ty, MST, tài khoản giám đốc, mật khẩu khôi phục và lưu tệp `.recovery`.
3. Sau khi vào `WorkspaceWindow`, mở **Bảng Tổng quan Giám đốc** → nhấn **Mở ghép đôi máy mới (tự đóng sau 5 phút)**.

**Cửa sổ 2 — Máy Kế toán (`accountant1`):**
```powershell
dotnet run --project src/EzCompany.App -- --profile=accountant1
```
1. Chọn tab **3. Tham gia Mạng LAN Công ty**.
2. Chọn công ty vừa phát hiện qua LAN (mDNS/UDP), nhập Họ tên, Username, Mật khẩu cục bộ → nhấn **Gửi yêu cầu ghép đôi LAN (SAS 6 số)**.
3. Đối chiếu mã `XXX-XXX` trên cả hai màn hình; tại máy Giám đốc 1 chọn vai trò **Accounting** → nhấn **Duyệt ghép đôi**.

**Cửa sổ 3 — Máy Pháp chế (`legal1`):**
```powershell
dotnet run --project src/EzCompany.App -- --profile=legal1
```
Thực hiện tương tự và được Giám đốc 1 duyệt vai trò **Legal**.

**Cửa sổ 4 (Tùy chọn) — Máy Giám đốc 2 (`director2`):**
```powershell
dotnet run --project src/EzCompany.App -- --profile=director2
```
Chọn tab **2. Máy Giám đốc 2 / Khôi phục**, chọn lại tệp `.recovery` vừa xuất ở Bước 1 để đồng bộ khóa gốc và giải mã chung toàn bộ ảnh Telemetry.

### Hoặc chạy tự động bằng Script kiểm thử & Log cục bộ
```powershell
# Chạy toàn bộ build + test Release và xuất log ra TestResults/local-verify-*.log
.\scripts\run-local-tests.ps1

# Khởi chạy nhanh 3 máy giả lập (kèm log Debug chi tiết và Telemetry 60s)
.\scripts\start-multi-profile-demo.ps1 -IncludeDirector2
```

- **Nhật ký runtime theo từng profile:** `%LocalAppData%\EzCompany\Profiles\<profile>\Logs\ezcompany-yyyyMMdd.log` (hỗ trợ biến môi trường `EZCOMPANY_LOG_LEVEL=Debug`, xem trực tiếp hoặc nhấn **Mở thư mục Logs** trong màn hình Tổng quan Trạm).

---

## Kiến trúc & Đóng góp

- Lịch sử thay đổi xem tại [CHANGELOG.md](CHANGELOG.md)
- Chi tiết kiến trúc xem tại [ARCHITECTURE.md](ARCHITECTURE.md)
- Kế hoạch & mốc kiểm chứng xem tại [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Hướng dẫn tạo module `IAppModule` xem tại [CONTRIBUTING.md](CONTRIBUTING.md)
- Báo cáo lỗ hổng bảo mật xem tại [SECURITY.md](SECURITY.md)
- Giấy phép mã nguồn mở: [LICENSE](LICENSE) (Mozilla Public License 2.0)
