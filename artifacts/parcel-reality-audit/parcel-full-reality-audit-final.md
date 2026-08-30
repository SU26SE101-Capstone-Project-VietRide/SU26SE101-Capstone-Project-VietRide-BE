# Parcel Full Reality Audit — Final Report

## 1. Kết luận

Parcel Reliability v2 đã vượt qua bộ kiểm thử live E2E chính qua Gateway với dữ liệu thật được tạo qua API: **43/43 business checks PASS**. Các nhánh quan trọng gồm happy path, QR/location sai, exception hai bước, đối soát bến cuối, tìm thấy trên xe, tìm thấy tại bến sai và forwarding, claim, bồi thường, funding pending, appeal, tenant isolation và concurrency đều có bằng chứng HTTP/DB.

Dữ liệu kiểm thử chính được giữ nguyên trong database theo:

- `runId`: `PCL-E2E-mtf6q2gv2hk`
- Báo cáo đầy đủ ID, HTTP status, error code và `traceId`: `mtf6q2gv2hk.md`
- Raw evidence: `mtf6q2gv2hk.json`

Không nên diễn giải kết quả này thành “mọi tính năng tương lai đều hoàn thiện”. Các giới hạn P2/P3 còn lại được ghi rõ ở mục 8.

## 2. Environment gate cuối

| Hạng mục | Kết quả |
|---|---|
| Docker containers | 13/13 đang `healthy` |
| Gateway `:3000/health` | HTTP 200 |
| Identity `:5001/health` | HTTP 200 |
| Trip `:5002/health` | HTTP 200 |
| Booking `:5003/health` | HTTP 200 |
| Payment `:5004/health` | HTTP 200 |
| Parcel `:5005/health` | HTTP 200 |
| Tracking `:3001/health` | HTTP 200 |
| Notification `:3002/health` | HTTP 200 |
| RAG `:3003/health` | HTTP 200 |
| RabbitMQ | running, ping thành công, không có local alarm |
| Identity outbox | `PUBLISHED=533`, DLQ=0 |
| Trip outbox | `PUBLISHED=1576`, DLQ=0 |
| Booking outbox | `PUBLISHED=247`, DLQ=0 |
| Payment outbox | `PUBLISHED=1483`, DLQ=0 |
| Parcel outbox | `PUBLISHED=2923`, DLQ=0 |
| Tracking outbox | `PUBLISHED=1`, DLQ=0 |

Runtime tạo Trip/ETA hiện dùng **GOONG**, không phải Google Routes. Google/fake GPS không được dùng làm custody proof và không được tự kích hoạt incident Parcel.

## 3. Verification matrix

| Suite/check | Kết quả |
|---|---:|
| Parcel unit tests | 556/556 PASS |
| Parcel integration tests với PostgreSQL thật | 87/87 PASS |
| Parcel Release build | PASS, 0 warning/0 error |
| Parcel `dotnet format --verify-no-changes` | PASS |
| Trip unit tests | 873/873 PASS |
| Trip integration tests | 383/383 PASS |
| Identity unit tests | 403/403 PASS |
| Gateway Jest | 234/234 PASS |
| Gateway lint | PASS |
| Shared contracts build | PASS |
| Shared contracts lint | 0 error, 8 warning có sẵn |
| Live Parcel Reality E2E | 43/43 PASS |
| Route/fare/availability E2E | 70 assertions PASS |
| Parcel settlement E2E | 652 assertions PASS |
| Day 32 cargo recovery E2E | 4/4 scenarios PASS |
| Prettier trên 4 E2E scripts | PASS |
| Node syntax check trên 4 E2E scripts | PASS |
| `git diff --check` | PASS |

## 4. Các lỗi P0/P1 đã sửa và retest

### 4.1. Custody exception hai bước

- Assistant chỉ gửi báo cáo; body không còn nhận `supervisorApprovalUserId`.
- Report trả trạng thái chờ duyệt; chưa ghi custody event và chưa bắt đầu SLA tìm kiếm.
- Driver được phân công duyệt bằng JWT của chính mình qua `POST /v1/crew/parcels/{parcelId}/custody-exception-decision`.
- Operator Staff/Admin duyệt theo incident bằng JWT qua Operator API.
- `searchDeadline` nullable khi chờ duyệt; expiry không xử lý incident chưa bắt đầu search.
- Approve mới chuyển sang tìm kiếm; reject khôi phục trạng thái trước báo cáo.
- Cross-tenant approval trả 404, không lộ sự tồn tại của request.

### 4.2. Đối soát bến cuối trước khi complete Trip

- `destination/arrive` chỉ xác nhận xe đã tới và mở quyền unload; không tự tạo `MISSING`.
- Bổ sung `POST /v1/assistant/trips/{tripId}/destination/reconcile`.
- Bổ sung internal clearance `GET /internal/v1/parcels/trips/{tripId}/completion-clearance`.
- Cả manual complete và automatic complete dùng chung completion guard.
- Complete trước reconciliation bị chặn bằng `409 PARCEL_DESTINATION_RECONCILIATION_REQUIRED`.
- Parcel unresolved tạo `UNSCANNED_HANDOFF/SEARCHING`, không bị kết luận mất ngay.
- Consumer `trip.completed` không tạo incident trùng khi đã có active reconciliation incident.

### 4.3. Custody scan an toàn với vận hành thật

- `custody-scan` chỉ ghi dấu vết vật lý; không thay business status.
- `ACCEPTED` chỉ hợp lệ tại origin station trước load.
- `ARRIVED_AT_STOP` phải trùng `currentOperationalLocation` đang `ARRIVED`.
- `HANDOFF` phải ở stop/station hiện hành.
- Vehicle lấy từ Trip hoặc phải khớp vehicle của Trip.
- Vị trí tùy ý bị từ chối bằng `409 PARCEL_CUSTODY_LOCATION_MISMATCH`.
- `check-in`, `load`, `unload`, `deliver`, `confirm-found-on-vehicle` tự ghi custody; FE không gọi thêm `custody-scan` sau các action này.

### 4.4. Passenger không tạo incident vận hành giả

Passenger chỉ được báo `DELIVERY_NOT_RECEIVED`, `DAMAGED`, `PARTIAL_LOSS`. Tự báo `MISSING` hoặc incident nội bộ bị từ chối `422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE`.

### 4.5. Tìm thấy và đưa hàng trở lại luồng giao

- Tìm thấy trên chính xe: Assistant gọi `confirm-found-on-vehicle`; incident được resolve, Parcel trở lại `LOADED/IN_TRANSIT`, rồi giao bình thường.
- Tìm thấy tại bến sai: mark found, lấy forwarding options, reserve cargo, tạo transit leg mới, `FORWARDED_OUT`/`FORWARDED_IN`, unload tại đúng stop và recipient confirm.
- Lịch sử leg cũ không bị sửa; forwarding không tạo duplicate cargo/payment.

### 4.6. Claim và tiền

- Claim 12 triệu, policy 50%/30 triệu: cargo award 6 triệu; freight refund 6 triệu; total payout 12 triệu.
- Không có chứng từ: `4 × 6 triệu` cước = 24 triệu cargo award; cộng refund 6 triệu; total 30 triệu.
- Thiệt hại 80 triệu: cargo award bị cap 30 triệu và chuyển `FUNDING_PENDING` khi operator thiếu nguồn.
- Concurrent decision chỉ một request thắng; retry không tạo double payout.
- Wallet không âm; tenant khác không đọc được claim.
- Paid claim appeal submit/replay/queue/decision đã PASS.

## 5. Dữ liệu live quan trọng được giữ lại

| Resource | ID |
|---|---|
| Primary operator | `dd72c2a3-ff0e-4e89-8421-4219cd5f0f21` |
| Foreign operator | `4791f710-70bb-4abb-9897-d6dc85d481aa` |
| Source Trip | `1f57c66a-730f-4b3a-ae4a-6c9027f6d97e` |
| Forwarding Trip | `2c7e87b0-6418-4fcf-a067-3931158ad032` |
| Booking-linked Booking | `62de9f30-2858-4da5-a711-53dc944b1e17` |
| Happy Parcel | `0d5ee6d0-3828-480e-88b4-8f5a38de06b4` |
| Wrong-stop Parcel | `fc4e4489-444c-4d3d-a586-7138c650e807` |
| Recovered-on-vehicle Parcel | `7da3bf87-2de6-432e-8900-485c10ed25be` |
| Destination-unresolved Parcel | `2c0215ff-e0e9-4f92-b930-59efa4246279` |
| Claim 12m | `ace50d1f-ae89-47fe-b7ab-948e36348c8c` |
| No-proof claim | `b98283b6-b35e-449b-87f4-f49b8d713c92` |
| Claim 80m | `72a09532-ae6a-42e8-bf92-5ed9494ac6c6` |

Toàn bộ user ID, parcel ID, incident ID, appeal ID, payout reference và HTTP evidence nằm trong báo cáo run-specific.

## 6. Phân loại kết quả cho FE

### PASS

- Passenger tracking/detail có screen-ready response; recipient không thấy claim.
- Driver manifest dùng được trước chuyến khi `currentOperationalLocation = null`.
- Check-in/load không phụ thuộc Trip đã start và không cần gọi thêm custody-scan.
- Mutation trả state mới để FE cập nhật card mà không bắt buộc refetch.
- Incident/claim/forwarding/custody đã giữ tenant isolation.

### FE_MISUSE cần tránh

- Không gửi `supervisorApprovalUserId`, `reviewerUserId` hoặc actor UUID trong request; actor/reviewer lấy từ JWT.
- Không gọi `custody-scan` sau check-in/load/unload/deliver.
- Không dùng `currentOperationalLocation = null` trước chuyến để chặn check-in/load.
- Không coi GPS hoặc xe đi ngang stop là bằng chứng hàng đã tới.
- Không tự tạo `MISSING` từ Passenger UI.
- Không tự suy diễn `availableActions`; dùng danh sách backend trả về.

### TEST_HARNESS_FIXED

Ba harness cũ bị lệch schema/auth/response contract và đã được sửa, không phải lỗi runtime sau cùng:

- Route/fare: station columns, seat-layout snapshot, `operatorStatus` JWT.
- Settlement: `cycle_price_amount`, response `parcelState.status`.
- Cargo recovery: `seat_layout_snapshot_json`, `operatorStatus` JWT.

## 7. Sự cố môi trường không thuộc Parcel run

Booking Hangfire còn log lỗi lặp từ seed lịch sử tham chiếu Trip đã bị xóa: `00000000-0000-4000-8000-000000000013`. Đây là dữ liệu cũ đã được ghi nhận trong runbook, không phát sinh từ `PCL-E2E-mtf6q2gv2hk` và không làm Parcel outbox/DLQ lỗi. Không tự xóa dữ liệu này trong audit để tránh mutation ngoài phạm vi.

## 8. P2/P3 và giới hạn bằng chứng

- `CreateParcelResponse`/`ParcelDetailResponse` hiện chưa expose `bookingId` dù DB đã lưu liên kết. Đây là read-model gap P2 nếu FE cần hiển thị liên kết Booking trực tiếp.
- `DAMAGED` và `PARTIAL_LOSS` đã có incident reporting, nhưng claim handler hiện tập trung vào incident `LOST_CONFIRMED`; quy trình thẩm định/chi trả hư hỏng và mất một phần vẫn thuộc hardening Phase 4.
- Live 43-check run xác nhận nhánh chuyển `FUNDING_PENDING`; chưa chứng minh bằng chính run này việc settlement tương lai tự offset rồi chuyển claim sang `PAID`.
- Cancel/return/vehicle substitution không được chạy thành từng happy scenario độc lập trong live 43-check run. Cargo recovery suite xác nhận transfer/return race và crash recovery ở mức cơ chế; không nên dùng kết quả đó để tuyên bố mọi UI flow liên quan đã được kiểm thử thủ công.
- Search SLA 72 giờ được kiểm tra bằng dữ liệu/time manipulation trong môi trường test; không chờ 72 giờ theo thời gian thật.

## 9. Kết luận phát hành

Các luồng Parcel v1 cốt lõi trong phạm vi audit hiện đủ ổn định để FE tích hợp và demo có kiểm soát. Demo không cần fake GPS: Trip arrival/departure, QR scan, reconciliation và supervisor decision là các hành động vận hành có chủ đích. Không có đường nào được phép tự kết luận `LOST_CONFIRMED`; chỉ Operator declare-lost sau search expiry mới mở điều kiện claim mất hàng.
