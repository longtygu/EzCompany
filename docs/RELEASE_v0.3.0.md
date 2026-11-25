# Ez Company v0.3.0 — Giao diện mới, Chat P2P, Đa chức vụ, Lịch trình & HR

## Điểm nổi bật trong bản phát hành `v0.3.0`

### 1. Sửa lỗi giao diện & Chất liệu cửa sổ
- Khắc phục lỗi thay đổi cỡ chữ, độ rộng thẻ, màu chủ đề và độ trong suốt không áp dụng đồng bộ.
- Sửa lỗi xung đột màu nền khi chuyển đổi chất liệu cửa sổ (`Mica` / `Acrylic` / `None`).
- Cập nhật biểu tượng ứng dụng chính thức (`EzCompany.ico`) và bổ sung hiển thị chức danh song ngữ Việt–Anh (ví dụ `Manager (Giám đốc)`, `Accounting (Kế toán)`, `Legal (Pháp chế)`, `HR (Nhân sự)`, `Staff (Nhân viên)`).

### 2. Tổng quan (`Tổng quan`), Đa chức vụ & Logo công ty
- Đổi tên tab Chào mừng thành **Tổng quan** và gom toàn bộ thiết lập (Giao diện, Tài khoản, Cập nhật, Log & Tự sửa lỗi) vào tab **Cài đặt** ở cuối thanh điều hướng.
- Hỗ trợ gán **nhiều chức vụ** cho cùng một nhân viên (ví dụ `Accounting,Legal`); màn hình Tổng quan tự sinh các tab chức vụ con và công cụ tương ứng theo quyền hạn.
- Cho phép tải lên và đồng bộ **Logo công ty** qua LAN tới màn hình Đăng nhập và Tổng quan của tất cả máy trong công ty.

### 3. Chat nội bộ P2P, Lịch trình kế hoạch & Giao việc đính kèm tệp
- **Chat nội bộ (`ChatWorkspaceView`):** Hỗ trợ chat nhóm và chat riêng, đính kèm/tải tệp tin cục bộ, đồng bộ hai chiều qua LAN (`PeerCollabSyncService`) và ghim việc được giao ngay trong nhóm chat.
- **Bảng lịch trình kế hoạch (`Timeline`):** Hiển thị trong tab Tổng quan cho cả Giám đốc và Nhân viên; bất kỳ thành viên nào cũng có thể bổ sung mốc kế hoạch.
- **Giao việc nâng cao:** Hỗ trợ đính kèm tệp khi giao việc và hiển thị thông báo tới người nhận (`TaskAssignedNotificationEvent`).

### 4. Hồ sơ Nhân sự, Chấm công & Tự chẩn đoán lỗi
- **Nhân sự & Chấm công (`HrAttendanceView`):** Quản lý hồ sơ nhân viên, ghi nhận chấm công hàng ngày và xuất báo cáo chấm công (`AttendanceExcelExporter`).
- **Chẩn đoán & Tự sửa lỗi:** Tích hợp trình xem log, xuất gói báo lỗi `.zip` và công cụ quét tự sửa lỗi mạng LAN, cấu hình và database ngay trong tab **Cài đặt**.

---

## Lưu ý nâng cấp từ `v0.2.0` lên `v0.3.0`
- **Tự động nâng cấp CSDL SQLite (v2 → v3):** Khi khởi động `v0.3.0`, ứng dụng tự sao lưu (`SafeMigrationService`) và áp dụng migration `20261126000001_AddChatTimelineHrAndLogo` (giữ nguyên toàn bộ dữ liệu công việc, tài khoản và cấu hình cũ).
- **Quyền phê duyệt phiên bản (Fleet Governance):** Trên máy **Giám đốc (Manager)**, đảm bảo mục `MaxApprovedVersion` được đặt thành **`0.3.0`** (giá trị mặc định mới trong bản này) để các máy nhân viên nhận bản cập nhật qua Velopack.

## Tệp phát hành & Kiểm chứng
- `EzCompany-win-Setup.exe`: Bộ cài đặt Windows đầy đủ (`v0.3.0`).
- `EzCompany-0.3.0-delta.nupkg`: Gói cập nhật vi sai tự động (Delta Update) cho máy đang chạy `v0.2.0`.
- `EzCompany-0.3.0-full.nupkg` & `releases.win.json`: Metadata cập nhật cho Velopack.
- `EzCompany-win-Setup.exe.sha256` & chứng thực nguồn gốc build (SLSA Build Provenance).
