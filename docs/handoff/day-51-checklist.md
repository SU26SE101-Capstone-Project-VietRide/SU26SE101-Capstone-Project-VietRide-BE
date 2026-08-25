# Day 51 — Final checklist

> Được tạo bởi `/audit-day 51` sau khi kiểm tra độc lập mã nguồn, source-of-truth và toàn bộ ma trận xác minh. Checklist này không thay thế các finding bằng giả định xanh.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 51 (Jira: timeline chưa ghi mã)
- **Plan**: `docs/handoff/day-51-plan.md`
- **Commit range**: `f63b1ec0..3fb6663d` (5 task commit + 1 verification-script repair commit); focused Tracking smoke support hiện còn ở working tree
- **Status**: ⚠️ **CLOSED-WITH-GAPS — USER ACCEPTED 2026-08-26**

## DoD result

- [x] ✅ Tracking và Trip chỉ còn runtime routing `GOONG|LOCAL`; không còn Google Routes key/base URL trên runtime/deploy path. Google OAuth và SDK hiển thị bản đồ ngoài phạm vi không bị xóa.
- [x] ✅ Goong Directions giữ đúng thứ tự waypoint, chunk mặc định tối đa 10, nối điểm biên giữa các chunk và cộng dồn `distance.value`/`duration.value`.
- [x] ✅ Provider từ chối HTTP 401/403/429/5xx, timeout/cancellation, JSON malformed, route/leg rỗng, giá trị âm, sai leg count và sai endpoint order; Tracking fallback/cooldown và Trip fail-closed khớp contract.
- [x] ✅ Tracking main Trip/Shuttle giữ cache, lock, cooldown và public payload; Goong map `ROUTE_BASED`, Local map `FALLBACK`, không lộ provider/key.
- [x] ✅ Trip lưu `GOONG`; public mapping là `GOOGLE_ROUTES → TRAFFIC_AWARE`, `GOONG → ROUTE_BASED`, `ROUTE_BASELINE → FALLBACK`; Shuttle distance/reposition tiếp tục fail-closed.
- [x] ✅ Migration `AddGoongPlannedEtaSource` apply đủ 39 migration từ DB rỗng, Down về `20260823092407_AddTripBusinessCodesReleaseA`, reapply và `has-pending-model-changes` đều xanh; snapshot/canonical DDL đồng bộ.
- [x] ✅ Fake-provider mới chạy hơn 0 test; self-test live gate 13/13 xanh. Live gate thật chạy 50 tuyến Việt Nam, gồm 5 tuyến nhiều điểm/95 legs, HTTP/parse/order/metrics đạt 100%, p95 442 ms < timeout 5.000 ms.
- [ ] ⚠️ Production-like Docker và business E2E của phạm vi Day 51 đã xanh: image Trip/Tracking mới, `ROUTING_PROVIDER=GOONG`, Gateway/Trip/Tracking/Notification/RabbitMQ healthy, Gateway/Hangfire lưu `GOONG` và GPS thật trả `ROUTE_BASED`. Full 9-service matrix chưa xanh vì RAG vẫn dùng image cũ ngày 11/08 chứa bundle OpenRouter dù source/Compose hiện tại đã chuyển sang ShopAIKey; full Trip solution test vẫn chưa có summary. User chấp nhận hai gap này và đóng Day 51 ngày 2026-08-26.
- [x] ✅ Patch sau audit đã chuyển toàn bộ business verification sang truth Day 51: wrapper legacy Phase 11 chạy fake-Goong spec hiện hữu; incident E2E dùng `EXPECT_GOONG` và assert `GOONG`/`ROUTE_BASED`.

## Tasks completed

- Task 51.0 — Chốt architecture baseline và public contract — ✅ audit static pass.
- Task 51.1 — Thay Tracking ETA provider bằng Goong, giữ Local fallback — ✅ implementation/static/Nx pass; script wrapper cũ được ghi riêng là carry-over.
- Task 51.2 — Thay ba Trip routing client và quality mapping — ⚠️ implementation/build/format pass, nhưng full Trip solution test timeout.
- Task 51.3 — EF migration reversible cho `PlannedEtaSource.GOONG` — ✅ lifecycle scratch DB đầy đủ pass sau một retry Npgsql thoáng qua.
- Task 51.4 — Deployment config, live gate và handoff — ✅ image Trip/Tracking mới, Day 51 scoped health, live Goong business E2E và fake-provider adversarial tests đều pass; full-stack RAG và regression Trip còn là gap ngoài flow Goong.

## Changed files

- **Source-of-truth/contract**: `.env.example`, `BACKEND_SOURCE_OF_TRUTH.md`, `BE_TIMELINE_VU.md`, `SU26SE101_VIETRIDE_technical_context_v7.md`, `VietRide_API_Contract_v1.md`, Tracking timeline, Day 51 plan/handoff và Goong rollout runbook.
- **Tracking**: env schema, ETA module/service/constants/DTO, Shuttle ETA, các fixture/spec liên quan; thay `google-routes-eta.*` và `shuttle-google-routes.e2e-spec.ts` bằng Goong Directions equivalents; thêm typed-parser scope trong `eslint.config.mjs`.
- **Trip**: `PlannedEtaSource`, projection mapper, DI và Shuttle handler; thay ba Google client/test bằng `GoongDirectionsClient` cùng ba adapter và tests.
- **Schema/EF**: migration `20260825121725_AddGoongPlannedEtaSource` + Designer, snapshot, `TripDbContext`, canonical `schema.sql`/README và migration test.
- **Deploy/QA**: bốn Compose file, Goong live gate + 50-route fixture + self-test; xóa Google live stub; verification repair commit `3fb6663d`; working-tree verifier bổ sung `TRACKING_SMOKE_ONLY` để chạy Gateway/Tracking smoke không phụ thuộc Parcel legacy. Không đổi dependency/lockfile/csproj.

## Verification run

| Command / gate | Result | Evidence |
|---|---|---|
| SOT/static assertions + supported-parser Prettier + `git diff --check` | ✅ PASS | Contract/runtime keywords, parser-supported ledger và whitespace đều xanh; `.env.example` không đưa sai vào Prettier parser. |
| `node --test scripts/goong-directions-live-gate.spec.mjs` | ✅ PASS | 13/13 self-test pass, gồm lỗi HTTP, timeout, malformed/count/order và redaction. |
| `node --env-file=.env scripts/goong-directions-live-gate.mjs --fixture scripts/fixtures/goong-vietnam-routes.json --minimum-routes 50 --minimum-multipoint-routes 5 --timeout-ms 5000` | ✅ PASS | 50 routes, 5 multipoint routes, 95 legs; HTTP/parse/order/metrics 100%; p95 442 ms. Không ghi/log API key. |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | 0 warning, 0 error. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | Exit 0. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release` | ❌ FAIL/TIMEOUT | Hai lần chạy không cho summary; retry hữu hạn 20 phút hết timeout. Testhost vẫn dùng CPU (user 260,656 giây + kernel 34,172 giây), không bị Docker/Postgres/sandbox block; đã dừng đúng process tree, không retry lần ba. Unit/integration count từ full command không có. |
| `dotnet test apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj -c Release` | ✅ PASS | 852/852 pass, 0 fail/skip, test duration 7 giây (wall 125,1 giây). Trong đó architecture/NetArch filter 4/4 pass; 848 test unit còn lại pass. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | 10 projects + 3 dependencies; exit 0; 66 warning dependency/source-map, 0 error. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | 14/14 projects; 0 error, 12 warning. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | 158 suites, 1.297 tests pass; 0 fail/skip/todo. Ba shared project không có test theo `--passWithNoTests`; Jest báo bốn force-exit/open-handle warnings. |
| EF scratch DB `vietride_trip_day51_migration`: empty apply → Down prior → reapply → pending-model | ✅ PASS | Drop exact DB; apply đủ 39 migration từ rỗng; Down về `20260823092407_AddTripBusinessCodesReleaseA`; readiness-gated reapply `20260825121725_AddGoongPlannedEtaSource`; pending-model output `No changes have been made to the model since the last migration.` Seeded proof xác nhận `GOOGLE_ROUTES` giữ nguyên, `GOONG → ROUTE_BASELINE`, `ROUTE_BASELINE` giữ nguyên; reapply khôi phục đủ ba enum. Reapply đầu gặp Npgsql read-timeout thoáng qua, retry duy nhất pass; seed đã dọn và DB kết thúc sạch ở migration hiện tại. |
| Bốn lệnh `docker compose ... config --quiet` | ✅ PASS | Dev, prod, tracking-sharing E2E và Day 36 E2E config render hợp lệ; không còn Google Routes runtime secret/config. |
| Docker Engine restart + build Trip/Tracking images + `ROUTING_PROVIDER=GOONG` | ✅ PASS | Engine `29.7.2`; Tracking image `sha256:b9e7f874...` tạo `2026-08-25T15:46:41Z`; Trip image `sha256:71dc7760...` tạo `2026-08-25T16:11:13Z`; container inspect xác nhận cả Trip và Tracking nhận `ROUTING_PROVIDER=GOONG`. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml ps -a ...` | ⚠️ PARTIAL | Gateway, Identity, Trip, Booking, Payment, Parcel, Tracking, Notification và RabbitMQ healthy. RAG image `sha256:114e6fde...` tạo ngày 11/08, trước commit `1b680f01` chuyển sang ShopAIKey; bundle cũ vẫn validate `OPENROUTER_API_KEY`. Rebuild được user dừng và RAG container đã stop để tránh crash-loop. |
| Direct + Gateway `/health` matrix | ⚠️ PARTIAL | Direct HTTP 200: gateway, identity, trip, booking, payment, parcel, tracking, notification; Gateway passthrough Identity/Trip/Booking/Payment/Parcel đều 200. RAG không trả health vì image cũ chưa rebuild; Gateway không định nghĩa public health passthrough cho Tracking/Notification/RAG. |
| RabbitMQ management + `rabbitmqctl list_exchanges` | ✅ PASS | Management `:15672` trả 200; `vietride.events` tồn tại với type `topic`, durable `true`. |
| `node scripts/test-tracking-phase11-shuttle-google.js` (patch rerun) | ✅ PASS | Wrapper giữ tên legacy để không phá caller, chạy `shuttle-goong-directions.e2e-spec.ts`; 1 suite/1 test pass, 0 fail. |
| Review artifact `scripts/run-incident-parcel-eta-lifecycle-e2e.mjs` | ✅ PASS | `EXPECT_GOONG=true` assert Trip `plannedEtaSource=GOONG` và Tracking `estimateQuality=ROUTE_BASED`; `TRACKING_SMOKE_ONLY=true` dùng quyền DRIVER, dừng sau GPS/ETA đầu tiên và luôn cleanup. `node --check`, Prettier và `git diff --check` pass. |
| `npx nx run tracking:build` (patch rerun) | ⚠️ TIMEOUT | Không trả output trước giới hạn 5 phút trên máy đang bất ổn; audit run trước patch đã build Tracking/toàn TS xanh và patch chỉ đổi scripts ngoài app source. Không claim rerun pass. |
| `$env:EXPECT_GOONG='true'; node scripts/run-incident-parcel-eta-lifecycle-e2e.mjs` | ⚠️ PARTIAL | Qua Gateway/Hangfire đã chứng minh generated Trip lưu `GOONG`; full cumulative flow sau đó fail ở Parcel quote vì response thiếu token/expiry, trước bước GPS. Fixture cleanup pass; đây không phải lỗi Goong. |
| `$env:EXPECT_GOONG='true'; $env:TRACKING_SMOKE_ONLY='true'; node scripts/run-incident-parcel-eta-lifecycle-e2e.mjs` | ✅ PASS | 44 assertions, 0 audit failure; Trip lưu `GOONG`; GPS qua Tracking socket trả target Station, distance 1.975 m, ETA khoảng 8 phút, `estimateQuality=ROUTE_BASED`; fixture/Redis cleanup pass. |
| Tracking fallback suites: `eta.service.spec.ts` + `shuttle-eta.service.spec.ts` | ✅ PASS | 2 suites, 31/31 tests. Goong failure rơi về Local `FALLBACK`; ba lỗi mở cooldown; provider/trip-data exception không làm crash realtime. |
| `dotnet test ...VietRide.Trip.UnitTests.csproj --filter "FullyQualifiedName~ExternalClients.GoongDirections"` | ✅ PASS | 48/48 tests, 0 fail/skip; ba Trip Goong adapter giữ fail-closed trên HTTP/timeout/malformed/partial response. |
| `$env:DAY36_E2E_USE_DEV_STACK='1'; node scripts/run-day36-shuttle-e2e.mjs` (rerun) | ❌ FAIL | Health đã qua nhưng seed harness cũ dùng cột `stations.province` không còn trong schema; dừng trước business flow. Không sửa lan sang Day 36 trong audit này. |
| Day 51 Review bullet overall | ⚠️ CLOSED-WITH-GAPS — USER ACCEPTED | Review chức năng Day 51 đã xanh: live 50-route gate, production-like scoped services, Gateway `GOONG → ROUTE_BASED`, Tracking fallback/cooldown và Trip fail-closed. Full Trip solution test và rebuild/health RAG không hoàn tất; user quyết định không tiếp tục hai gate này và đóng Day 51 ngày 2026-08-26. |
| Hard invariants | ✅ PASS | 66 changed paths; 0 dependency/csproj path; 0 csproj `Version=` drift; 0 `Co-Authored-By`; EOL đúng; `.env` không tracked và đã gitignore; `git diff --check` xanh. |

## Contract / event / schema changes shipped

- Không thêm/xóa REST endpoint, Gateway route, event routing key hoặc error code.
- Public ETA quality mở rộng tương thích ngược thành `TRAFFIC_AWARE|ROUTE_BASED|FALLBACK`; không thêm provider metadata vào Shuttle payload.
- Trip DB enum `vietride_trip.planned_eta_source` thành `GOOGLE_ROUTES|GOONG|ROUTE_BASELINE`; `GOOGLE_ROUTES` chỉ dùng đọc dữ liệu lịch sử.
- Migration `20260825121725_AddGoongPlannedEtaSource` remap `GOONG → ROUTE_BASELINE` trước khi khôi phục enum cũ trong `Down()`; snapshot và canonical DDL đồng bộ.
- Runtime config chuẩn: `ROUTING_PROVIDER=GOONG|LOCAL`, `GOONG_API_KEY`, `GOONG_BASE_URL`, `GOONG_MAX_DESTINATIONS_PER_REQUEST`, `TRACKING_ROUTING_TIMEOUT_MS`.
- BSOT/contract/timeline/changelog liên quan đã cập nhật; không có event/error registry entry mới cần thêm.

## Known gaps & carry-over cho Day 51 patch audit

- Trip IntegrationTests được chạy riêng với `--blame-hang-timeout 5m`: testhost tăng CPU liên tục và không có test đơn lẻ treo 5 phút, nhưng suite vẫn chưa hoàn tất trước khi user yêu cầu chuyển gate. Cần chạy lại với cửa sổ dài phù hợp để lấy exact integration count; chưa có bằng chứng cần sửa .NET teardown/runtime.
- Full 9-service health còn thiếu RAG. Source/env schema/provider/Compose đã dùng `SHOPAIKEY_*` và `.env` có đủ ShopAIKey/Cloudinary key, nhưng image RAG vẫn là bản 11/08 chứa bundle OpenRouter cũ. Rebuild RAG đã được bắt đầu rồi dừng theo quyết định user; container RAG hiện stopped.
- Full cumulative incident/Parcel E2E dừng ở `Quote token/expiry missing`; Day 36 harness dừng vì cột `stations.province` đã bị xóa. Focused Day 51 Gateway/Tracking smoke đã thay thế để chứng minh Goong, nhưng hai harness legacy cần task sửa riêng nếu muốn full cumulative evidence.
- Điều tra bốn Jest force-exit/open-handle warnings; hiện không làm fail Nx nhưng là dấu hiệu teardown chưa sạch.

## Notes cho lần audit lại

- Hai gate user yêu cầu đã hoàn tất: production-like core Day 51 services healthy và business flow thật trả `GOONG → ROUTE_BASED`; adversarial fallback/fail-closed cũng xanh bằng fake-provider suites.
- DB `vietride_trip` local được reset/reseed đúng ghi chú migration sau khi backup phục hồi tại `C:\tmp\vietride_trip_pre_day51_20260825_2324.dump` (599.039 byte).
- User chấp nhận không chạy tiếp full Trip solution test và không rebuild/health RAG; Day 51 được đóng ở trạng thái `CLOSED-WITH-GAPS` ngày 2026-08-26. Chỉ mở lại hai gate này nếu user tạo task mới; timeline vẫn thiếu Jira key để traceability.
