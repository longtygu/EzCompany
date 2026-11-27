# Ez Company v0.6.0

Bản phát hành **v0.6.0** bổ sung trọn bộ **10 nâng cấp Enterprise Suite** đồng bộ hóa quy trình nội bộ (Nhân sự, Kế toán, Pháp chế, Văn phòng PDF, Kho tài liệu, Chat/Việc làm & Trung tâm điều hành Giám đốc) cùng các bản sửa lỗi giao diện và đồng bộ LAN từ Milestone 10.

## Điểm mới nổi bật

### 1. Khay hệ thống (System Tray) & Khởi động cùng Windows
- Đóng cửa sổ chính sẽ thu nhỏ ứng dụng xuống khay hệ thống (Win32 `Shell_NotifyIconW`) kèm menu chuột phải **Mở Ez Company** / **Thoát ứng dụng hoàn toàn**.
- Bổ sung tùy chọn tự động khởi động cùng Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) trong mục Cài đặt.

### 2. Ảnh đại diện cá nhân & Nâng cấp Chat nội bộ
- Nhân viên và Giám đốc có thể tải ảnh đại diện cá nhân (tối đa 512 KB), đồng bộ P2P sang các máy trạm LAN và hiển thị trực tiếp trên thanh hiện diện Chat.
- Hỗ trợ **trả lời trích dẫn** (`ReplyToMessageId`), **tìm kiếm tin nhắn** theo nội dung/người gửi/tệp, **xem trước ảnh trong bong bóng chat & nhấn phóng to**, và cửa sổ **Quản lý nhóm chat** (`ManageChatGroupWindow`) để đổi tên nhóm hoặc thêm/bớt thành viên.

### 3. Quản lý công việc nâng cao (Nhắc hạn, Lọc, Giao nhiều người, Subtasks & Lặp định kỳ)
- Thanh tìm kiếm và lọc trạng thái/ưu tiên cho cả Việc được giao & Việc đã giao.
- Giao việc cùng lúc cho nhiều nhân viên, quản lý **Checklist công việc con (`Subtasks`)**, cấu hình lặp lại **Daily / Weekly / Monthly** tự sinh việc mới khi nghiệm thu xong, và **nhắc hạn deadline** 1 lần/ngày.

### 4. Đơn Xin nghỉ phép / Nửa ngày / Đi muộn / Tăng ca & Chấm công / Phiếu lương PDF
- Nhân viên gửi đơn trực tiếp (`EmployeeRequestsView`), đồng bộ LAN sang HR & Giám đốc duyệt (`HrAttendanceView` Tab 4 & `ExecutiveDashboardView`), tự động cập nhật bảng chấm công tương ứng.
- Hỗ trợ **xuất Phiếu lương PDF** cho từng nhân viên (`PayslipPdfExporter`) và **gửi thẳng vào Chat riêng 1-1** của nhân viên.
- Cơ chế xóa chấm công **soft-delete tombstone (`IsDeleted`)** đồng bộ lên Giám đốc, cho phép chỉnh sửa bất kỳ ngày nào trong ma trận chấm công, và đánh dấu **`T7` / `CN` / `Lễ`** trên các ô trống.

### 5. Kế toán, Pháp chế, Kho Tài liệu Dùng chung & Văn phòng PDF nâng cao
- **Kế toán (`AccountingInvoiceView`)**: Tìm kiếm/lọc/cảnh báo/xóa hóa đơn XML, **Sổ quỹ Thu/Chi** kèm xuất Excel (`AccountingLedgerExcelExporter`) và luồng **Đề xuất Tạm ứng / Thanh toán / Hoàn ứng**.
- **Pháp chế (`LegalContractDiffView`)**: **Sổ theo dõi Hợp đồng** (`Active` / `ExpiringSoon` / `Expired` / `Terminated`), đẩy mốc nhắc hết hạn sang Timeline chung và xuất Excel Danh mục Hợp đồng (`ContractRegistryExcelExporter`).
- **Kho Tài liệu Dùng chung (`SharedDocumentsView`)**: Lưu trữ & đồng bộ danh mục tài liệu nội bộ qua LAN, tải tệp P2P kèm xác thực SHA-256.
- **Văn phòng PDF (`OfficePdfStudioView`)**: Bổ sung **Ảnh → PDF** nhiều trang và **Đóng dấu PNG Chữ ký / Con dấu** theo tọa độ `%`.

### 6. Trung tâm Phê duyệt tập trung, Biểu đồ KPI & Tự động Sao lưu Giám đốc
- **Approval Hub** duyệt tập trung Đơn nghỉ phép/OT và Đề xuất chi phí ngay trên bảng điều hành Giám đốc kèm badge thông báo trên thanh điều hướng.
- **Biểu đồ KPI trực quan** cho tỷ lệ hoàn thành công việc và giờ làm việc / đi muộn theo tháng.
- **Director Auto-Backup** cấu hình lịch sao lưu tự động các máy Online và giữ lại N bản mới nhất mỗi máy.

### 7. Sửa lỗi & Cải thiện UX (Milestone 10)
- Sửa triệt để cửa sổ ở mức trong suốt **`0% (Đặc)`** không còn xuyên thấu trên Windows 10/11.
- Hiển thị trạng thái kết nối LAN thực tế (`Đã kết nối LAN` xanh / `Mất kết nối` đỏ nhấp nháy), nút **Bỏ ghim công việc**, biểu tượng làm mới dạng mũi tên xoay vòng, danh sách máy trạm mở rộng dạng khay và trình xem/phóng to ảnh chụp `SnapshotViewerWindow`.

## Lưu ý sau khi cập nhật (Máy Giám đốc)
- Nếu dùng cơ chế quản trị phiên bản nâng cấp trong tab **Giám đốc (`Executive`)**, hãy cập nhật `MaxApprovedVersion` lên **`0.6.0`** để các máy nhân viên nhận bản cập nhật mới nhất qua Velopack / GitHub Releases.
