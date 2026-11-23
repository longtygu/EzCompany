# Ez Company v0.1.0 — Phase 1 MVP (Initial Open-Source Release)

> **Mã nguồn mở (MPL-2.0) • Local-First • Zero 24/7 Server • .NET 8 Windows Desktop**

**Ez Company v0.1.0** là bản phát hành MVP đầu tiên của bộ phần mềm quản trị nội bộ Local-First dành cho doanh nghiệp vừa và nhỏ (Giám đốc, Kế toán, Pháp chế, Nhân viên). Toàn bộ dữ liệu nghiệp vụ và telemetry được lưu cục bộ bằng **SQLite (WAL mode)** trên từng máy trạm; nhân viên đăng nhập và làm việc **100% offline** ngay cả khi các máy Giám đốc đang tắt.

---

## 1. Thành phần Gói Phát hành (`Releases/`)

| Tệp phát hành | Mục đích |
|---|---|
| `EzCompany-win-Setup.exe` *(Velopack Installer)* | Trình cài đặt chính thức (win-x64) cho mọi máy trong công ty (1 bản cài đặt duy nhất cho Giám đốc, Kế toán, Pháp chế, Nhân viên). |
| `EzCompany-win-Portable.zip` | Bản giải nén chạy trực tiếp không cần cài đặt (phù hợp kiểm thử nhanh hoặc máy hạn chế quyền cài). |
| `EzCompany-0.1.0-full.nupkg` | Gói cập nhật Velopack chuẩn dùng cho cơ chế auto-update và sinh Delta Patch ở các bản tiếp theo. |
| `releases.win.json` | Manifest phiên bản Velopack dùng cho `GithubSource` kiểm tra cập nhật công khai. |
| `SHA256SUMS.txt` | Bảng băm kiểm tra toàn vẹn SHA-256 cho toàn bộ tệp phát hành. |

### Kiểm tra toàn vẹn SHA-256 trên Windows (PowerShell)
```powershell
Get-FileHash .\EzCompany-win-Setup.exe -Algorithm SHA256
# Đối chiếu giá trị Hash với dòng tương ứng trong SHA256SUMS.txt
Get-Content .\SHA256SUMS.txt
```
Mọi tệp trong bản phát hành GitHub Releases đều đi kèm chứng thực nguồn gốc xây dựng **SLSA Build Provenance** (`actions/attest-build-provenance`).

---

## 2. Điểm nổi bật của Phase 1 MVP (`v0.1.0`)

### Một gói cài đặt duy nhất — 3 nhánh khởi tạo (`MainWindow`)
1. **Giám đốc gốc (Genesis Bootstrap):** Sinh cặp khóa `Ed25519` gốc, dẫn xuất khóa mã hóa công ty `X25519` qua HKDF-SHA256, bắt buộc xuất **Master Recovery Kit (`.recovery`)** mã hóa Argon2id + AES-256-GCM trước khi hoàn tất.
2. **Máy Giám đốc thứ 2 / Khôi phục (Hướng A):** Nhập tệp `.recovery` để dùng chung khóa gốc `Ed25519` và tự động giải mã chung kho ảnh Telemetry với Giám đốc 1.
3. **Máy Nhân viên (Ghép đôi LAN SAS 6 số):** Tự phát hiện máy Giám đốc qua **mDNS (`_ezcompany._tcp.local.`)** + UDP Multicast/Broadcast fallback; đối chiếu mã **SAS 6 số (`XXX-XXX`)** chống MITM; mật khẩu băm Argon2id cục bộ tại máy nhân viên (không truyền qua mạng).

### Kiến trúc Bảo mật & Mạng LAN P2P (Zero-SMB)
- **Offline Policy Engine (`policy.lic` + `clock.guard`):** Giấy phép ký Ed25519 có hạn mặc định 14 ngày, ràng buộc `MachineId` + `DevicePublicKey`, chống hạ `PolicyVersion` và tự khóa module nếu đồng hồ Windows bị tua ngược quá 5 phút.
- **Embedded Kestrel LAN Agent (`port 0`):** Không dùng Windows SMB Share; mọi API quản trị yêu cầu chữ ký Ed25519 (`X-Ez-Timestamp` ±60s, `X-Ez-Nonce` chống Replay 2 phút), `VaultPathGuard` chặn Path Traversal, `VACUUM INTO` sao lưu DB nguyên vẹn không khóa máy nhân viên, Pending Policy Queue và đồng bộ sổ thiết bị giữa 2 Giám đốc.

### Telemetry Phương án B (An toàn trước Antivirus) & Executive Dashboard
- Đo Active/Idle qua `GetLastInputInfo` + `SessionSwitch` (ngưỡng 180s), thống kê ứng dụng kèm **`PrivacySanitizer`** tự động che tiêu đề nhạy cảm (`ngân hàng`, `OTP`, `mật khẩu`, `Zalo`, `Messenger`, `Incognito`...).
- Chỉ chụp nội bộ cửa sổ `WorkspaceWindow` bằng WPF `RenderTargetBitmap` (không dùng hook bàn phím hay API chụp toàn Desktop), mã hóa đầu cuối `X25519/ECIES` sao cho máy nhân viên không thể tự giải mã, tự dọn FIFO 7 ngày / 200 MB.
- **Executive Dashboard (5 khu vực):** Thẻ KPI, Lưới máy trạm LAN (Phân quyền nóng, Duyệt/Tải Vault, Sao chép DB `VACUUM INTO`, Gửi thông báo LAN), AuditLogs & App Usage, Timeline Ảnh giải mã từ Director Cache (xem lại ngay cả khi nhân viên tắt máy), và Trung tâm duyệt SAS 6 số + `MaxApprovedVersion`.

### 3 Phân hệ Nghiệp vụ MVP (`IAppModule`) & Read-Only DB Inspector
- **Kế toán (`accounting.invoice_xml`):** Kiểm tra hàng loạt hóa đơn XML (NĐ 123/2020 & NĐ 70/2025 bao gồm CCCD người mua), phát hiện trùng số/MST và lệch cộng dồn (>10đ), lưu Vault + `app.db`, xuất Excel `.xlsx` (`ClosedXML`) kèm chuyển tổng tiền VND sang chữ tiếng Việt.
- **Pháp chế (`legal.contract_diff`):** Trích xuất đoạn văn & bảng biểu `.docx` (`OpenXml`), so sánh Side-by-Side & Inline (`DiffPlex`), điều hướng `Prev / Next Change`, lưu Vault kèm SHA-256, xuất báo cáo `.html` và `.docx` offline.
- **Văn phòng (`office.pdf_studio`):** Gộp PDF (kéo thả), tách trang (`1-3, 5`), xoay trang (`90°/180°/270°`), đóng dấu Watermark chéo Unicode (`PDFsharp 6.2.4` MIT + `PdfPig`).
- **Read-Only DB Inspector:** Giám đốc mở bản chụp `.db` của nhân viên ở chế độ chỉ đọc (`Mode=ReadOnly`), không trộn dữ liệu vào DB cục bộ.

### Tự cập nhật an toàn & Nhật ký kiểm thử (`--profile`)
- **Velopack + `MaxApprovedVersion` + `SafeMigrationService`:** Nhân viên chỉ cập nhật tối đa tới `MaxApprovedVersion` do Giám đốc duyệt; trước khi nâng cấp schema tự checkpoint WAL, sao lưu SHA-256 và tự động Rollback nếu migration lỗi.
- **Giả lập đa máy trên 1 PC:** Chạy với `--profile=<tên>` (`director1`, `accountant1`, `legal1`, `director2`) để tách biệt dữ liệu tại `%LocalAppData%\EzCompany\Profiles\<tên>\` và ghi nhật ký runtime hàng ngày vào thư mục `Logs\ezcompany-yyyyMMdd.log`.

---

## English Summary (`v0.1.0`)

**Ez Company v0.1.0** is the initial Phase 1 MVP release of an open-source (**MPL-2.0**), local-first **.NET 8 (`net8.0-windows`) WPF** enterprise desktop suite. Workstations store business records locally in SQLite (WAL mode), authenticate offline with Argon2id + Ed25519 `policy.lic` licenses guarded against clock rollbacks (`clock.guard`), pair over zero-config mDNS/UDP LAN discovery with 6-digit SAS verification, sync non-blocking `VACUUM INTO` backups and ECIES-encrypted telemetry over a signed Kestrel LAN API, and support safe Velopack updates gated by `MaxApprovedVersion` and automatic pre-migration rollback (`SafeMigrationService`).

### Quick Start for Testers
1. Download and run `EzCompany-win-Setup.exe` (or extract the Portable `.zip`).
2. On the first Director PC, choose **1. Khởi tạo Công ty mới** and store the `.recovery` file safely.
3. On a second Director PC (optional), choose **2. Máy Giám đốc 2 / Khôi phục** with the `.recovery` file.
4. On employee workstations, enable **Mở ghép đôi máy mới** on the Director Dashboard, then choose **3. Tham gia Mạng LAN Công ty** and match the 6-digit SAS code.
