# Ez Company v0.4.0 — Giao diện DWM thực, Chat 1s + Popup + Sticker, Sidebar Khay & Nhân sự/Chấm công/Tính lương

## Điểm nổi bật trong bản phát hành `v0.4.0`

### 1. Sửa triệt để lỗi Giao diện, Độ trong suốt & Màu thẻ (Windows 10/11)
- **Độ trong suốt & Chất liệu nền thực (`EzFluentWindow` + `WindowTransparencyHelper`):** Giữ khung kính DWM (`GlassFrameThickness = -1`), hỗ trợ Blur/Acrylic chuẩn trên Windows 10 và Mica/Acrylic/Tabbed trên Windows 11.
- **Màu thẻ & Cỡ chữ đồng bộ:** Thêm thanh trượt **Độ đậm nền cửa sổ (30–100%)**, **Độ đậm của thẻ (40–100%)** và **Pha màu chủ đề vào thẻ (0–40%)** áp dụng cho toàn bộ màn hình ứng dụng.

### 2. Chat nội bộ P2P thế hệ mới (1s Realtime, Popup nổi & Custom Sticker)
- **Cập nhật liên tục 1s tối ưu hiệu năng:** Kiểm tra dấu mốc hoạt động nhẹ và chỉ chèn tin nhắn delta mới (`GetMessagesAfterAsync`), không làm giật giao diện hay tải nặng DB/mạng.
- **Popup nổi thông báo tin nhắn mới (`ChatToastPopupWindow`):** Hiển thị góc dưới-phải màn hình khi có tin nhắn từ đồng nghiệp; bấm vào popup để mở ngay đúng cuộc trò chuyện.
- **Sticker tùy chỉnh (`CustomStickerService`):** Tích hợp bộ sticker mẫu + cho phép thêm sticker từ ảnh cá nhân (PNG/JPG/GIF/WebP/BMP), xóa, gửi trực tiếp và đồng bộ P2P sang máy người nhận.
- **Trải nghiệm chat chuẩn:** Bong bóng tin nhắn trái/phải kèm avatar chữ cái đầu, badge số tin chưa đọc, phím `Enter` gửi nhanh.

### 3. Sidebar thu gọn / mở rộng dạng khay theo Chức vụ
- Gom nhóm công cụ theo từng vị trí công việc dạng `Expander`: **Không gian chung**, **Manager (Giám đốc)**, **HR (Nhân sự)** (`Hồ sơ nhân viên`, `Chấm công`, `Tính lương tự động`), **Accounting (Kế toán)**, **Legal (Pháp chế)**, **Staff & Office (Văn phòng)**.

### 4. Hồ sơ Nhân viên, Chấm công tùy chỉnh, Cửa sổ chi tiết & Tính lương Tự động
- **Hồ sơ nhân viên (`HrAttendanceView`):** Danh sách mở rộng dạng khay khi bấm vào, hỗ trợ tìm kiếm và đầy đủ nút **+ Thêm**, **Sửa**, **Xóa** (có xác nhận), **Chấm công nhanh** cùng thông tin lương cơ bản/phụ cấp/công chuẩn/BHXH.
- **Chấm công giờ tùy chỉnh & Cửa sổ chi tiết riêng (`AttendanceDetailWindow`):** Cho phép nhập chính xác giờ vào/ra `HH:mm` (hỗ trợ cả ca qua đêm), số công, tăng ca; mở cửa sổ độc lập xem **Bảng Ma trận Chấm công full theo ngày (Ngày 1 → cuối tháng)**, sửa trực tiếp từng dòng và xuất Excel `.xlsx`.
- **Tính lương tự động:** Tự tổng hợp công chuẩn, công thực tế, giờ tăng ca (hệ số 1.5), cho phép điều chỉnh thưởng/khấu trừ trực tiếp và xuất **Bảng lương Excel (`.xlsx`)**.

---

## Lưu ý nâng cấp từ `v0.3.0` lên `v0.4.0`
- **Tự động nâng cấp CSDL SQLite (v3 → v4):** Khi khởi động `v0.4.0`, `SafeMigrationService` tự tạo bản sao lưu trước migration và áp dụng `20261127000001_AddChatStickerAndPayrollFields` (giữ nguyên toàn bộ dữ liệu cũ).
- **Quyền phê duyệt phiên bản (Fleet Governance):** Trên máy **Manager (Giám đốc)**, đảm bảo mục `MaxApprovedVersion` được đặt thành **`0.4.0`** (giá trị mặc định mới) để các máy nhân viên nhận bản cập nhật qua Velopack.

## Tệp phát hành & Kiểm chứng
- `EzCompany-win-Setup.exe`: Bộ cài đặt Windows đầy đủ (`v0.4.0`).
- `EzCompany-0.4.0-delta.nupkg`: Gói cập nhật vi sai (Delta Update) cho máy đang chạy `v0.3.0`.
- `EzCompany-0.4.0-full.nupkg` & `releases.win.json`: Gói đầy đủ và metadata Velopack.
- `SHA256SUMS.txt` & chứng thực nguồn gốc build (SLSA Build Provenance).
