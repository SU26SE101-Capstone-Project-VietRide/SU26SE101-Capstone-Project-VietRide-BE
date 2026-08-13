# Ma trận bao phủ tri thức VietRide

Ma trận này là completion gate của bộ knowledge base. Dấu “Có” nghĩa tài liệu role tương ứng phải
giải thích hành vi bằng ngôn ngữ người dùng; dấu “Theo phạm vi” nghĩa chỉ giải thích phần người đó
được nhìn thấy hoặc cần phối hợp. Chi tiết kỹ thuật toàn hệ thống thuộc tài liệu System Admin.

| Domain | Hành khách | Tài xế | Phụ xe | Nhà xe | System Admin |
|---|---|---|---|---|---|
| Tài khoản, đăng nhập, hồ sơ, khóa/tạm ngưng | Có | Có | Có | Có | Có |
| Nhà xe, nhân sự, thuê bao, quota, hóa đơn | Theo phạm vi | Theo phạm vi | Theo phạm vi | Có | Có |
| Bến, điểm dừng, tuyến, phụ thu | Có | Theo phạm vi | Theo phạm vi | Có | Có |
| Xe, sơ đồ ghế, lịch crew, sinh chuyến | Theo phạm vi | Có | Có | Có | Có |
| Vòng đời chuyến, stop, hủy, đổi giờ/tuyến/xe | Có | Có | Có | Có | Có |
| Manifest, QR boarding, no-show | Có | Có | Có | Có | Có |
| Shuttle: bố trí, pickup/dropoff, GPS, ETA | Có | Có | Theo phạm vi | Có | Có |
| Booking một chiều/khứ hồi, voucher | Có | Theo phạm vi | Theo phạm vi | Có | Có |
| Payment, VNPay, Ví VietRide, refund | Có | Theo phạm vi | Theo phạm vi | Có | Có |
| Parcel: tạo, cọc, check-in, cân, load/unload | Có | Theo phạm vi | Có | Có | Có |
| Parcel: bàn giao, từ chối, chuyển, hoàn | Có | Theo phạm vi | Có | Có | Có |
| GPS, ETA, trễ trên 30 phút, lệch tuyến | Có | Có | Có | Có | Có |
| Thông báo: inbox, push, email, realtime, deep-link | Có | Có | Có | Có | Có |
| Báo cáo, doanh thu, settlement, điều chỉnh ví | Theo phạm vi | Không | Không | Có | Có |
| RAG chat, tài liệu, ingest, config, feedback | Theo phạm vi | Theo phạm vi | Theo phạm vi | Theo phạm vi | Có |
| Idempotency, retry, race và deadline biên | Theo phạm vi | Theo phạm vi | Có | Có | Có |

## Quy tắc chứng minh coverage

- Mỗi domain phải có ít nhất một section trong mọi tài liệu được đánh dấu “Có”.
- Mỗi domain phải có câu hỏi regression cho các role chịu tác động trực tiếp.
- Câu hỏi cần dữ liệu hiện tại phải trả lời phần quy tắc xác định được, nêu rõ giới hạn và chỉ nơi người dùng tự kiểm tra. Không yêu cầu người dùng gửi mã hoặc dữ liệu để trợ lý “kiểm tra giúp”, vì tài liệu tĩnh không có khả năng tra cứu trực tiếp.
- Câu hỏi xuyên service phải mô tả kết quả người dùng nhìn thấy; không kể tên service/event mặc định.
- Rule chưa có implementation hoặc còn mâu thuẫn phải được ghi là chưa đủ thông tin.
- Câu gợi ý UI là regression bắt buộc, không được phát hành UI trước knowledge tương ứng.
- `OPERATOR_STAFF` và `OPERATOR_ADMIN` cùng ánh xạ vào một cột “Nhà xe”, không có ma trận quyền riêng.

## Nguồn kiểm chứng

Coverage được audit từ runtime source và test của Identity, Trip, Booking, Payment, Parcel,
Tracking, Notification, Gateway và RAG; sau đó đối chiếu `VietRide_API_Contract_v1.md`,
`SU26SE101_VIETRIDE_technical_context_v7.md`, `BACKEND_SOURCE_OF_TRUTH.md` và schema/migration.
