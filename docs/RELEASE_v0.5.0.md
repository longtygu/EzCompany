# Ez Company v0.5.0

Bản phát hành **v0.5.0** tập trung nâng cấp trải nghiệm trực quan và thao tác đa nhiệm trên desktop cho **Tổng quan / Lịch trình kế hoạch**, **Cài đặt độ trong suốt theo %**, và **Chat nội bộ tách cửa sổ + thanh hiện diện nhân viên**.

## Điểm mới nổi bật

### 1. Bảng lịch trình & kế hoạch chung dạng đường thẳng song song
- **Timeline luôn hiển thị sẵn** (kể cả khi chưa có kế hoạch nào) với các đường thẳng ngang song song và các chấm đánh dấu ngày tháng, vạch Hôm nay và thanh kế hoạch tô màu theo khoảng thời gian.
- **Tạo & quản lý kế hoạch trong cửa sổ riêng (`TimelineEditorWindow`)**: Gom toàn bộ form tạo, chỉnh sửa, đổi trạng thái và xóa lịch trình ra ngoài qua nút **`+ Tạo kế hoạch / lịch trình`**, giúp màn hình Tổng quan gọn gàng và đồng bộ dữ liệu LAN tức thì.
- **Phóng to / Thu nhỏ & chuyển đổi 3 ↔ 5 đường song song**: Mặc định hiển thị **3 đường song song**, có thể mở rộng lên **5 đường song song**, phóng to/thu nhỏ khung thời gian (`60 ngày` toàn cảnh / `30 ngày` chuẩn / `14 ngày` chi tiết) và trượt theo mốc ngày.

### 2. Tùy chỉnh độ trong suốt cửa sổ & thẻ theo `%`
- Bổ sung thanh kéo, ô nhập trực tiếp `%` và các nút preset nhanh (`0%`, `15%`, `30%`, `50%`, `70%`) cho **độ trong suốt cửa sổ (`0%–70%`)** và **độ trong suốt thẻ (`0%–60%`)**, đồng bộ hai chiều với cài đặt DWM/Opacity hiện có.

### 3. Nâng cấp giao diện Chat nội bộ
- **Tách cửa sổ chat & kéo thả gắn lại**: Mở box chat thành cửa sổ riêng (`DetachedChatWindow`) để vừa trao đổi vừa làm việc trên các tab khác; kéo cửa sổ chat thả vào lại giao diện chính để tự động gắn về vị trí cũ mà không làm gián đoạn hoặc tải lại cuộc trò chuyện.
- **Thanh ảnh đại diện nhân viên + chấm nhấp nháy Online/Offline**: Hiển thị dải ô tròn ảnh đại diện nhân viên phía trên khung chat (tự động dùng icon thay thế nếu chưa có ảnh) kèm chấm xanh nhấp nháy (Online) / đỏ nhấp nháy (Offline); ấn trực tiếp vào nhân viên để chat riêng.
- **Tìm kiếm nhân viên theo tên hoặc chức vụ**: Thay thế ComboBox chọn người chat riêng bằng ô tìm kiếm xổ danh sách (hỗ trợ tiếng Việt không dấu).
- **Cửa sổ tạo nhóm chat riêng (`CreateChatGroupWindow`)**: Tách phần tạo nhóm chat mới ra hộp thoại riêng gọn gàng với bộ lọc thành viên và trạng thái Online/Offline.

## Lưu ý sau khi cập nhật (Máy Giám đốc)
- Nếu dùng cơ chế quản trị phiên bản nâng cấp trong tab **Giám đốc (`Executive`)**, hãy cập nhật `MaxApprovedVersion` lên **`0.5.0`** để các máy nhân viên nhận bản cập nhật mới nhất qua Velopack / GitHub Releases.
