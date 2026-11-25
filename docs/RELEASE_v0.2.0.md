# Ez Company v0.2.0 — UI Customization, Welcome Dashboard, P2P Tasks & Account Recovery

Phiên bản **v0.2.0** nâng cấp trải nghiệm người dùng, bổ sung quản lý công việc P2P giữa các máy trong mạng LAN và quy trình khôi phục/cấp lại tài khoản an toàn không truyền mật khẩu qua mạng.

## Các tính năng & nâng cấp chính

### 1. Cài đặt Giao diện tùy biến (`AccountAndAppearanceView`)
- Tuỳ chỉnh trực tiếp **cỡ chữ (`11–20 pt`)**, **độ rộng tối đa các thẻ (`640–1600 px`)**, **bo góc (`4–20 px`)**, **khoảng đệm thẻ**, **độ rộng thanh điều hướng (`210–340 px`)**.
- Chuyển đổi **Dark / Light Mode**, **8 mẫu màu chủ đề (Accent Color)** + nhập mã Hex tùy ý.
- Tùy chỉnh **hiệu ứng nền cửa sổ** (`Mica`, `Acrylic`, `Tabbed`, `None`) và **độ trong suốt cửa sổ (`70%–100%`)** thông qua Win32 layered window mà không ảnh hưởng Mica hay thao tác phóng to/thu nhỏ cửa sổ.
- Lưu cài đặt bền vững theo từng `--profile` (`appearance.json`).

### 2. Giao diện Chào mừng sau khi đăng nhập (`WelcomeDashboardView`)
- Hiển thị lời chào **`"Xin chào [Tên nhân viên]"`** tại góc trên bên trái cùng **ngày tháng Dương lịch & Âm lịch Việt Nam** (tính toán UTC+7 offline 100%, hiển thị Can Chi và tháng nhuận).
- Theo dõi **Task hôm nay** (tự tạo nhanh, đánh dấu hoàn thành/xóa), **Task được giao**, **Tiến độ hoàn thành (`%` + thống kê)** và **Lịch sử hoàn thành các task**.

### 3. Đổi tên/mật khẩu & Giao việc / Nhận bàn giao P2P (`WorkTaskService` + `TaskManagementView`)
- **Đổi tên hiển thị, tên đăng nhập và mật khẩu cục bộ (Argon2id)** cho cả **Giám đốc** lẫn **Nhân viên**, tự động cập nhật `policy.lic` hoặc đồng bộ thông tin hiển thị tới máy Giám đốc.
- **Giao việc & Nhận bàn giao kết quả giữa các máy LAN** (cả Giám đốc ↔ Nhân viên và Nhân viên ↔ Nhân viên), kèm đính kèm tệp kết quả vào `Vault\Tasks\{TaskId}\` với kiểm chứng **SHA-256**, nghiệm thu hoàn thành / yêu cầu làm lại và lưu toàn bộ **Lịch sử giao việc & bàn giao**.
- Tự động xếp hàng đồng bộ khi máy nhận ngoại tuyến (Local-First).

### 4. Màn hình Đăng nhập căn giữa & Khôi phục / Cấp lại tài khoản
- Căn giữa thẻ đăng nhập gọn gàng cho cả Giám đốc và Nhân viên.
- **Nhân viên:** Nút **`Yêu cầu cấp lại tài khoản / mật khẩu`** gửi yêu cầu ký số thiết bị lên máy Giám đốc; Giám đốc duyệt trên **Executive Dashboard (Mục 6)** để cấp vé `PasswordResetTicket` ký số Ed25519 giúp nhân viên đặt lại mật khẩu cục bộ (không truyền mật khẩu qua mạng LAN).
- **Giám đốc:** Nút **`Khôi phục tài khoản (bằng tệp .recovery)`** ngay trên màn hình đăng nhập.

---

## Lưu ý khi cập nhật từ v0.1.0
1. **Sao lưu & nâng cấp CSDL tự động (`SafeMigrationService`):** Khi khởi chạy `v0.2.0` lần đầu, ứng dụng tự động sao lưu `app.db` & `telemetry.db` kèm mã kiểm chứng SHA-256 vào thư mục `Backups/` trước khi áp dụng EF Core Migration v2 (`20261125000001_AddTasksAndAccountRecovery`).
2. **Cập nhật cho máy Nhân viên:** Máy Giám đốc được phép cập nhật lên `v0.2.0` trực tiếp. Sau khi Giám đốc lên `v0.2.0`, hãy mở **Bảng tổng quan Giám đốc -> Quản trị Phát hành (`MaxApprovedVersion`)** và đặt thành `0.2.0` (mặc định mới) để các máy Nhân viên tải bản vá delta/cài đặt mới qua Velopack.

## Tệp phát hành & Kiểm chứng toàn vẹn
- `EzCompany-win-Setup.exe`: Bộ cài đặt Windows Desktop `.NET 8` (`win-x64`, self-contained).
- `EzCompany-0.2.0-win-full.nupkg` & `EzCompany-0.2.0-win-delta.nupkg` & `releases.win.json`: Gói cập nhật Velopack + Delta Patch.
- `EzCompany-0.2.0-win-Portable.zip`: Bản Portable chạy trực tiếp không cần cài đặt.
- `SHA256SUMS.txt` & SLSA Build Provenance: Dùng để đối chiếu toàn vẹn tệp tải về.
