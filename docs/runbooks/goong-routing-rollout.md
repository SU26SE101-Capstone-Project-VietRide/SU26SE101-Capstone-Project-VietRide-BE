# Runbook triển khai Goong cho định tuyến

## Mục tiêu và phạm vi

Runbook này áp dụng cho runtime định tuyến của Trip và Tracking. Trạng thái mục tiêu là
`ROUTING_PROVIDER=GOONG`; trạng thái rollback an toàn là `ROUTING_PROVIDER=LOCAL`.
Goong Directions v2 là contract hiện hành và sử dụng dữ liệu địa giới hành chính mới sau sáp nhập.

Việc chuyển provider không đổi endpoint, event, Gateway route, cổng, network hoặc credential của
các service khác. `GOOGLE_MAPS_API_KEY` dùng để hiển thị bản đồ và các biến Google OAuth vẫn độc lập,
không được xóa trong đợt rollout này. `GOOGLE_ROUTES` chỉ còn là giá trị nguồn ETA lịch sử trong cơ
sở dữ liệu Trip, không phải provider runtime.

## Cấu hình chuẩn

Trip và Tracking phải nhận cùng năm biến sau:

| Biến | Giá trị triển khai | Ý nghĩa |
|---|---|---|
| `ROUTING_PROVIDER` | `GOONG` hoặc `LOCAL` | Chọn Goong hoặc thuật toán Local. Production rollout dùng `GOONG`. |
| `GOONG_API_KEY` | secret do môi trường inject | Bắt buộc khi provider là `GOONG`; không ghi vào Git, log hoặc URI được in ra. |
| `GOONG_BASE_URL` | `https://rsapi.goong.io` | Origin của Goong; runtime ghép endpoint Directions v2 `/v2/direction`. |
| `GOONG_MAX_DESTINATIONS_PER_REQUEST` | `10` | Số đích tối đa trong mỗi request; không đặt lớn hơn contract. |
| `TRACKING_ROUTING_TIMEOUT_MS` | `1500` theo mặc định | Timeout của request Goong trong Tracking. Live gate Day 51 dùng ngưỡng riêng `5000`. |

Các timeout chuyên biệt của Trip (`TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS`,
`TRIP_PLANNED_ETA_TIMEOUT_MS`, `RESOURCE_TRAVEL_TIME_TIMEOUT_MS`) vẫn giữ nguyên.

## Inject và bảo vệ secret

- Ở production/staging, lưu `GOONG_API_KEY` trong secret manager và inject thành biến môi trường cho
  cả container Trip và Tracking. Không dùng hoặc phân phối file `.env` tại các môi trường này.
- Chỉ khi chạy live gate trên máy phát triển, có thể đặt key trong `.env` local đã được gitignore
  hoặc trong process environment. Tuyệt đối không commit `.env`, sao chép key vào `.env.example`,
  Compose, fixture, ticket hoặc ảnh chụp log.
- Không truyền key trên command line. Node native chỉ nạp `.env` local qua `--env-file=.env`; live
  gate không in giá trị key hoặc request URL đầy đủ.
- Không log request URI đầy đủ, query `api_key`, `origin` hoặc `destination`. Khi điều tra lỗi chỉ ghi
  status, loại lỗi, chỉ số route/chunk và latency đã làm sạch.
- Khi nghi ngờ key bị lộ, revoke/rotate tại Goong, cập nhật secret manager, restart tuần tự Trip và
  Tracking, sau đó chạy lại live gate và health gate.

## Tiền kiểm trước rollout

1. Xác nhận có key từ process environment hoặc `.env` local đã gitignore mà không đọc/in giá trị
   trong PowerShell và không sửa file:

   ```powershell
   $keyCheckArgs=@()
   if(Test-Path -LiteralPath '.env'){$keyCheckArgs+='--env-file=.env'}
   $keyCheckArgs+=@('-e','process.exit(process.env.GOONG_API_KEY?.trim()?0:1)')
   node @keyCheckArgs
   if($LASTEXITCODE -ne 0){throw 'GOONG_API_KEY is missing from process env and local .env'}
   ```

   Trên production/staging, không có nhánh `.env`: key phải đến từ secret manager đã inject vào
   process environment.

2. Kiểm tra Compose:

   ```powershell
   docker compose -f infra/docker/docker-compose.yml config --quiet
   docker compose -f infra/docker/docker-compose.prod.yml config --quiet
   ```

3. Chạy self-test fake server; bước này không gọi Goong thật:

   ```powershell
   node --test scripts/goong-directions-live-gate.spec.mjs
   ```

4. Chạy live gate từ repo root. Script gọi tuần tự, giới hạn theo fixture, không in URL hoặc key và
   phải đạt 100% HTTP 200/parse/order/metric hợp lệ cùng `p95 < 5000 ms`:

   ```powershell
   $nodeArgs=@()
   if(Test-Path -LiteralPath '.env'){$nodeArgs+='--env-file=.env'}
   $nodeArgs+='scripts/goong-directions-live-gate.mjs'
   $nodeArgs+=@('--fixture','scripts/fixtures/goong-vietnam-routes.json','--minimum-routes','50','--minimum-multipoint-routes','5','--timeout-ms','5000')
   node @nodeArgs
   ```

Thiếu key, có bất kỳ status khác 200, timeout, JSON sai, sai số leg/order, metric âm hoặc p95 chạm
ngưỡng đều là gate failure. Không được skip hoặc thay bằng key hardcode.

## Rollout

1. Giữ `ROUTING_PROVIDER=LOCAL`, inject bốn biến Goong còn lại cho Trip và Tracking.
2. Chạy toàn bộ tiền kiểm, đặc biệt live gate với key của đúng môi trường.
3. Chuyển `ROUTING_PROVIDER=GOONG` cho Trip và Tracking trong cùng release configuration.
4. Restart tuần tự và chờ health check của từng service xanh trước khi chuyển service tiếp theo.
5. Chạy `/audit-day 51`. Audit này sở hữu production-like Docker boot/health matrix qua Gateway và
   direct service health, cùng business E2E; Task 51.4 không tự thay thế full audit.
6. Chỉ đóng rollout sau khi audit xanh và không có finding tránh được.

## Theo dõi sau rollout

Theo dõi theo từng cửa sổ 5–15 phút và theo quota dashboard của Goong:

- tỷ lệ HTTP `401/403` để phát hiện key sai, hết quyền hoặc secret inject lỗi;
- tỷ lệ `429`, quota còn lại và tốc độ request; không retry dồn dập khi đã chạm quota;
- HTTP `5xx`, timeout/cancellation, response malformed, sai leg count/order hoặc metric;
- p50/p95 latency so với timeout cấu hình;
- Tracking fallback/cooldown: ba lần lỗi và cooldown 300 giây phải giữ đúng contract, không trộn kết
  quả Goong bán phần với Local;
- Trip fail-closed cho khoảng cách Shuttle/reposition và tỷ lệ planned ETA rơi về Route baseline;
- public quality chỉ là `TRAFFIC_AWARE|ROUTE_BASED|FALLBACK`, không lộ provider hoặc key.

## Rollback

Rollback không cần đổi schema và không xóa dữ liệu lịch sử:

1. Đặt `ROUTING_PROVIDER=LOCAL` cho cả Trip và Tracking.
2. Restart tuần tự, chờ health check xanh và kiểm tra Tracking trả `FALLBACK` khi tính bằng Local.
3. Giữ secret Goong trong secret manager để phục vụ điều tra/roll-forward, hoặc rotate nếu có dấu
   hiệu lộ key; không ghi key vào log.
4. Chạy lại `/audit-day 51` production-like smoke trước khi xác nhận rollback hoàn tất.
5. Không rollback migration chỉ để tắt Goong. Nếu một rollback migration được phê duyệt riêng,
   `GOONG` sẽ được chuyển về `ROUTE_BASELINE` theo migration reversible của Day 51.

## Tiêu chí quyết định

- **Tiếp tục rollout:** live gate và audit xanh, không có `401/403`, `429` trong ngưỡng quota dự kiến,
  p95 thấp hơn timeout và fallback/fail-closed đúng contract.
- **Rollback `LOCAL`:** live gate lỗi, quota/429 không kiểm soát, p95 chạm/vượt timeout, response sai
  contract hoặc business E2E không xanh.
- **Không được tiếp tục:** thiếu `GOONG_API_KEY`, key xuất hiện trong output, hoặc phải hardcode secret
  để vượt gate.
