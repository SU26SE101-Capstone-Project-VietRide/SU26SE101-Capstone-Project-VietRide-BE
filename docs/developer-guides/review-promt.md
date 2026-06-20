Bạn là một Senior Engineer dày dạn kinh nghiệm, chuyên review code production trước khi merge/deploy. Hãy review kỹ service dưới đây mà tôi vừa hoàn thành.

Ngữ cảnh


Service làm gì: [Tracking Service nhận và phát vị trí GPS realtime theo chuyến qua Socket.IO; xác thực JWT và kiểm tra quyền theo dõi; lưu vị trí mới nhất và ETA trong Redis; cung cấp REST API đọc vị trí mới nhất, GPS trail và ETA; flush GPS trail từ Redis xuống PostgreSQL theo batch; tạo và publish Outbox Event cho các cảnh báo tracking. Lưu ý: tính ETA, cảnh báo approaching-stop và off-route hiện chưa hoàn thiện end-to-end vì TripDataProvider, BookingDataProvider và RouteGeometryProvider vẫn đang dùng Noop implementation.]

Tech stack: [Node.js, TypeScript, NestJS, Socket.IO, Prisma ORM, PostgreSQL, Redis/ioredis, BullMQ, RabbitMQ, JOSE/JWT, Zod, Pino, Swagger/OpenAPI, Jest và Nx monorepo.]

Các service/hệ thống liên quan (DB, queue, API ngoài...): [PostgreSQL database vietride_tracking, schema vietride_tracking, lưu GpsTrail và OutboxEvent; Redis lưu GPS mới nhất, ETA, dữ liệu GPS chờ flush, trạng thái dedupe và làm connection cho BullMQ; RabbitMQ topic exchange vietride.events nhận event từ Outbox Publisher với routing key trip.trip.delayed, tracking.gps.off_route và tracking.gps.approaching_stop; Identity Service cung cấp JWKS để xác minh user JWT; Trip Service kiểm tra quyền tracking của DRIVER, ASSISTANT và OPERATOR; Booking Service kiểm tra quyền của chủ booking; Parcel Service kiểm tra quyền của người gửi/người nhận kiện hàng; API Gateway proxy HTTP và WebSocket tới Tracking Service. Không thấy tích hợp trực tiếp API bên thứ ba trong code hiện tại.]

Yêu cầu review (đi qua từng mục, không bỏ sót)


Logic & luồng hoạt động: đi từng bước trong flow, kiểm tra thứ tự xử lý có hợp lý không, có bước nào dư/thiếu/sai vị trí, case nào bị bỏ sót, race condition, retry/rollback khi lỗi giữa chừng.
Lỗi runtime / bug: null/undefined chưa check, type mismatch, off-by-one, exception chưa catch hoặc catch sai chỗ, resource leak (connection, file handle, memory).
Bảo mật cơ bản: input validation, injection (SQL/command), secret bị log/hardcode, thiếu auth/authorization ở endpoint nhạy cảm.
Hiệu năng rõ ràng: N+1 query, loop lồng không cần thiết, block I/O đồng bộ trong context async, gọi API lặp lại không cache khi không cần.
Nhất quán: error handling có theo 1 pattern xuyên suốt không, logging có đủ để debug khi lỗi xảy ra ở production không.
Chuẩn RESTful — kiểm tra theo các tiêu chí cụ thể sau:

Route đặt theo resource (danh từ), không nhúng verb hành động vào URL (vd /complete-profile, /approve, /cancel). Nếu một hành động chỉ là update 1-vài field của resource, phải biểu diễn qua method (PATCH) + resource, không tạo route action riêng. Chỉ chấp nhận dạng sub-action (POST /orders/{id}/cancel) khi hành động đó là nghiệp vụ rời rạc, không map được vào CRUD thông thường.
HTTP method dùng đúng ngữ nghĩa: GET = đọc (không side-effect), POST = tạo mới hoặc hành động non-idempotent, PUT = thay toàn bộ resource (idempotent), PATCH = update một phần (idempotent), DELETE = xoá. Một request chỉ set lại 1-2 field thì phải là PATCH, không phải POST.
Mỗi endpoint phải khai báo đầy đủ status code có thể xảy ra trong thực tế: 200/201 (thành công), 400 (validation), 401 (chưa auth — nếu cần auth), 403/404/409/422 (nếu áp dụng cho nghiệp vụ đó), và bắt buộc luôn có 500 (lỗi hệ thống không lường trước) — thiếu 500 là lỗi, không phải optional.
Response envelope phải nhất quán và đúng thực tế runtime, không chỉ đúng trên schema khai báo: field như success/statusCode phải phản ánh đúng trạng thái thật ở mọi nhánh lỗi (400/401/409/422/500) — nếu code thực tế trả success: true ở response lỗi thì đây là bug Critical.
Không để 2 field trùng ý nghĩa gây mơ hồ (vd message ở top-level và data.message) trừ khi có mục đích phân biệt rõ ràng (system message vs user-facing message); nếu không rõ, đề xuất gộp hoặc đặt tên lại cho rõ.



Độ đủ của API so với luồng nghiệp vụ: API hiện tại có đủ để client thực hiện trọn vẹn luồng không, có bước nào trong flow chưa có endpoint tương ứng. Nếu thiếu, đề xuất cụ thể endpoint cần thêm (method, route, mục đích, input/output sơ bộ).
Swagger/OpenAPI (theo convention của @nestjs/swagger, KHÔNG áp theo .NET) — kiểm tra:

Mỗi action có @ApiOperation (summary/description rõ ràng) và @ApiTags theo module/resource.
Mỗi status code có thể trả về được khai báo qua @ApiResponse({status, type/schema}) đầy đủ, không để Swagger tự generate mặc định rời rạc.
Example value là dữ liệu thực tế hợp lý, không phải placeholder mặc định ("string", 0, true cho mọi field) — nếu toàn bộ example đều là default do thiếu khai báo, coi là thiếu cấu hình, cần bổ sung example cụ thể cho từng field quan trọng, đặc biệt ở response lỗi.
Riêng với stack dùng Zod: vì @nestjs/swagger mặc định lấy schema qua reflection từ class DTO, KHÔNG tự đọc được Zod schema thuần. Kiểm tra DTO có được convert đúng sang OpenAPI schema qua cầu nối (nestjs-zod, zod-to-openapi, hoặc tương đương) không — nếu không, Swagger doc sẽ thiếu field hoặc sai type so với validation thật. Đây phải coi là lỗi Critical vì gây sai lệch giữa tài liệu và hành vi thật.
Field nhạy cảm (token, password, secret) không xuất hiện trong example hoặc schema response.
Version API (v1, v2...) thể hiện rõ trong tag/group Swagger để dễ phân biệt khi có version mới.
9. **Phụ thuộc API từ service khác**: rà theo từng bước trong luồng, xác định bước nào cần gọi API/nhận event từ service khác (đã liệt kê ở phần Ngữ cảnh) để hoàn chỉnh luồng. Với mỗi phụ thuộc, chỉ rõ: cần API/event gì, từ service nào, mục đích dùng để làm gì, field nào bắt buộc phải có trong response/payload đó. Nếu hiện tại service đang giả định một API/field tồn tại mà chưa chắc đã có (hoặc chưa từng confirm với team kia), phải nêu rõ để xác nhận lại — không tự suy đoán là "chắc có sẵn".
10. **Hardcode cấu hình hệ thống**: rà toàn bộ code tìm các giá trị đang bị viết cứng (URL service khác, timeout, retry limit, port, API key/secret, tên queue/topic, ngưỡng số lượng, nội dung template...) mà đáng lẽ phải lấy từ env/config. Với mỗi giá trị tìm được, phân loại:
   - Chỉ khác theo môi trường (dev/staging/prod) → đề xuất chuyển vào env var/ConfigService, không cần thêm API.
   - Là giá trị nghiệp vụ mà vận hành có thể cần đổi thường xuyên mà không muốn deploy lại code → đề xuất hướng làm API config cụ thể (vd: method/route, lưu ở đâu — DB hay cache, ai có quyền gọi, có cần audit log khi thay đổi không).
   Không tự ý liệt kê mọi constant trong code là "cần config" — chỉ flag những giá trị thực sự có khả năng cần thay đổi theo môi trường hoặc theo vận hành.





Giới hạn — đọc kỹ trước khi trả lời


KHÔNG đề xuất refactor/redesign kiến trúc nếu code hiện tại chạy đúng và đủ tốt cho scope hiện tại.
KHÔNG over-engineer: không gợi ý thêm design pattern, abstraction layer, generic hóa nếu không thực sự cần để fix một lỗi cụ thể.
Chỉ tập trung vào: đúng/sai, chạy được/không chạy được, an toàn/không an toàn — không phải "có thể đẹp hơn".
Nếu thấy điểm nào "có thể tốt hơn" nhưng KHÔNG phải lỗi, tách riêng vào mục "Optional improvement" ở cuối, đừng trộn vào phần lỗi để tránh đánh giá sai mức độ nghiêm trọng.
Chỉ đề xuất thêm endpoint khi nó THỰC SỰ cần để hoàn thiện luồng nghiệp vụ đang có (client không thể thực hiện hết flow nếu thiếu). Không đề xuất endpoint "cho đầy đủ" hoặc "phòng khi cần sau này".


Output mong muốn


Liệt kê lỗi theo mức độ: Critical / High / Medium / Low.
Mỗi lỗi gồm: vị trí (file/function/dòng), mô tả lỗi, lý do nó là lỗi, hướng fix đề xuất (ngắn gọn, không viết lại cả file nếu không cần).
Mục riêng "RESTful & API coverage": các điểm chưa chuẩn REST (nếu có), và endpoint đề xuất bổ sung (nếu thực sự thiếu) kèm lý do tại sao thiếu nó thì luồng không chạy được.
Mục riêng "Swagger config": liệt kê những gì chưa đúng convention chuẩn của @nestjs/swagger (hoặc framework Swagger tương ứng với tech stack ở phần Ngữ cảnh).
Cuối cùng: 1 đoạn tóm tắt — service này đã sẵn sàng production chưa, và nếu chưa thì cần fix gì trước tiên.
- Mục riêng "External dependency cần xác nhận": liệt kê API/event từ service khác mà luồng này phụ thuộc, kèm trạng thái (đã chắc chắn có / chưa rõ cần hỏi lại team liên quan / chưa có cần đề xuất họ bổ sung).
- Mục riêng "Hardcode cần xử lý": liệt kê giá trị hardcode tìm được, vị trí (file/dòng), phân loại (env config / cần API config), và hướng xử lý đề xuất tương ứng.



Bạn là một QA Engineer/SDET dày dạn kinh nghiệm, chuyên viết test plan và Postman test script cho API service trước khi release.

Ngữ cảnh


Service: [...]
Tech stack: [...]
Danh sách endpoint hiện có (method + route + mục đích, copy từ Swagger hoặc liệt kê tay): [...]
Luồng nghiệp vụ chính cần test end-to-end (thứ tự các bước, API nào gọi trước/sau, bước sau phụ thuộc dữ liệu gì từ bước trước): [...]
Service/API bên ngoài mà luồng phụ thuộc (để biết cần mock hay test thật, có sandbox/test mode không): [...]
Auth method (JWT, API key...) và cách lấy token để test: [...]
Base URL/environment dùng để test (dev/staging): [...]
Ưu tiên output dạng nào: Postman collection JSON import được thẳng / hay mô tả test case + script rời để tự ráp: [...]


Yêu cầu


Test coverage cho từng endpoint: với mỗi API, lập test case cho happy path (input hợp lệ), validation lỗi (thiếu field, sai type, sai format), auth (thiếu token/token sai/hết hạn/không đủ quyền), business error (conflict, not found, đã tồn tại, vượt giới hạn...) — chỉ áp dụng các loại thực sự liên quan tới endpoint đó, không bắt đủ mọi loại cho mọi API.
Test theo luồng (end-to-end): dựng 1 chuỗi request theo đúng thứ tự nghiệp vụ thật (không test rời rạc từng API riêng lẻ), dùng response của bước trước làm input cho bước sau (vd lưu id/token vào biến môi trường Postman để bước sau dùng lại).
Idempotency & side-effect: API tạo mới (POST, non-idempotent) → test gọi lại để kiểm tra không tạo trùng dữ liệu nếu nghiệp vụ không cho phép duplicate. API idempotent (PUT/PATCH/DELETE) → test gọi lại nhiều lần phải ra kết quả nhất quán.
Test phụ thuộc external service: chỉ rõ API nào cần mock service ngoài để test độc lập, API nào nên test thật (nếu có sandbox).
Postman script cụ thể: viết pm.test() cho từng request, kiểm tra status code đúng, response có đủ field bắt buộc, giá trị field quan trọng đúng logic (vd success phải là false khi response lỗi), thời gian phản hồi nằm trong ngưỡng hợp lý.
Setup/teardown dữ liệu test: nêu rõ cần seed data gì trước khi test, và cần dọn gì sau khi test xong để không để lại rác hoặc ảnh hưởng lần test kế tiếp.


Giới hạn — đọc kỹ trước khi trả lời


KHÔNG viết test case dư thừa, trùng lặp, hoặc edge case không có khả năng xảy ra với nghiệp vụ thực tế hiện tại.
KHÔNG tự đổi qua công cụ/framework khác (Cypress, k6, Jest...) nếu không được yêu cầu — bám đúng Postman.
Ưu tiên theo độ rủi ro: luồng chính + lỗi hay gặp viết trước; edge case hiếm gặp ghi riêng vào mục "Nice to have", không trộn vào test case chính.
KHÔNG tự đoán hành vi của service ngoài khi viết mock nếu chưa xác nhận — nếu chưa rõ, ghi chú "cần xác nhận với team liên quan" thay vì giả định.


Output mong muốn


Bảng/tóm tắt test plan: tên test case, endpoint liên quan, loại (happy / validation / auth / business error / idempotency), kết quả mong đợi.
Test thực thi: theo lựa chọn ở phần Ngữ cảnh — Postman collection dạng JSON import được, hoặc mô tả từng request kèm script pm.test() tương ứng.
Danh sách biến môi trường Postman cần thiết (baseUrl, token, các id truyền giữa request...).
Mục riêng "Cần mock/cần xác nhận": liệt kê phần phụ thuộc external service chưa rõ hành vi, cần hỏi lại team liên quan trước khi viết test thật.
