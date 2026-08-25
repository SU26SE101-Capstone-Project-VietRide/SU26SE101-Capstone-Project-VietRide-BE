# Day 51 — Migration Google Routes sang Goong cho định tuyến Việt Nam

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 51 (Jira: chưa được ghi trong timeline)
- **Prior checklist**: `docs/handoff/day-50-checklist.md` (`not found`)
- **Plan status**: `APPROVED`
  <!-- Replace the value, do not append statuses. Allowed lifecycle:
  DRAFT | REVISION-REQUIRED | REVIEWER-APPROVED — AWAITING HUMAN | APPROVED -->

## Objective

Chuyển toàn bộ runtime định tuyến của Tracking và Trip từ Google Routes sang Goong Directions,
chỉ giữ `GOONG|LOCAL` và không yêu cầu Google API key trên đường chạy production. Giữ nguyên các
contract fallback/fail-closed, thứ tự ETA, cooldown, Shuttle payload và dữ liệu lịch sử; bổ sung
public quality tương thích ngược `ROUTE_BASED`. Hoàn tất migration EF reversible, cấu hình triển
khai, fake-provider coverage, live gate Việt Nam và handoff FE/Mobile để `/audit-day 51` có thể chạy
full regression cùng production-like Docker health/business matrix.

## Success criteria (DoD — binary, verifiable)

- [ ] Tracking và Trip chỉ chọn runtime `ROUTING_PROVIDER=GOONG|LOCAL`; không còn
      `GOOGLE_ROUTES_API_KEY`, `GOOGLE_ROUTES_ENABLED` hoặc Google base URL cần thiết trên runtime path.
- [ ] Goong Directions bảo toàn thứ tự waypoint, chia tối đa
      `GOONG_MAX_DESTINATIONS_PER_REQUEST` (mặc định 10), nối chunk từ đích cuối chunk trước và cộng dồn
      chính xác `routes[0].legs[].distance.value`/`duration.value`.
- [ ] Mọi HTTP `401/403/429/5xx`, timeout/cancellation, JSON malformed, routes/legs rỗng, giá trị âm,
      sai leg count hoặc endpoint order đều bị từ chối; Tracking fallback/cooldown và Trip fail-closed
      đúng contract, không nhận kết quả bán phần.
- [ ] Tracking main Trip và Shuttle giữ recalculate/cache/lock/cooldown hiện hành; public payload
      không lộ provider/key và chỉ bổ sung `estimateQuality=ROUTE_BASED` cho Goong, còn Local là
      `FALLBACK`.
- [ ] Trip planned ETA lưu `GOONG`, map public `ROUTE_BASED`; `GOOGLE_ROUTES` lịch sử vẫn đọc và map
      `TRAFFIC_AWARE`; `ROUTE_BASELINE` map `FALLBACK`. Shuttle distance và reposition vẫn fail-closed
      khi Goong không khả dụng.
- [ ] Migration `AddGoongPlannedEtaSource` apply từ DB rỗng, Down về
      `20260823092407_AddTripBusinessCodesReleaseA`, reapply và
      `has-pending-model-changes` đều xanh; Down chuyển mọi row `GOONG` về `ROUTE_BASELINE` trước khi
      khôi phục enum cũ; canonical DDL/snapshot đồng bộ.
- [ ] Fake-provider tests chạy >0 và bao phủ toàn bộ ma trận lỗi; live gate gọi ít nhất 50 tuyến Việt
      Nam, trong đó ít nhất 5 tuyến có 11–30 điểm, parse hợp lệ 100%, đúng thứ tự, mọi distance/duration
      hợp lệ và p95 nhỏ hơn timeout cấu hình.
- [ ] Compose/env/rendered config không chứa Google Routes runtime secret/config; full production-like
      Docker health matrix và business E2E qua Gateway được `/audit-day 51` chạy xanh.

## Contract changes

- Không thêm/xóa REST endpoint, Gateway route, event routing key hoặc error code.
- Public Tracking/Trip ETA quality mở rộng tương thích ngược từ
  `TRAFFIC_AWARE|FALLBACK` thành `TRAFFIC_AWARE|ROUTE_BASED|FALLBACK`; không thêm provider metadata vào
  Shuttle payload.
- Trip DB enum `vietride_trip.planned_eta_source` mở rộng thành
  `GOOGLE_ROUTES|GOONG|ROUTE_BASELINE`; `GOOGLE_ROUTES` chỉ còn để đọc dữ liệu lịch sử.
- Runtime config chuẩn là `ROUTING_PROVIDER=GOONG|LOCAL`, `GOONG_API_KEY`,
  `GOONG_BASE_URL=https://rsapi.goong.io`, `GOONG_MAX_DESTINATIONS_PER_REQUEST=10` và
  `TRACKING_ROUTING_TIMEOUT_MS`; API key chỉ lấy từ environment và nằm trong query string gửi Goong,
  nên tuyệt đối không log full URI.
- Goong Direction request dùng `GET /Direction` với ordered `origin=lat,lng`,
  `destination=lat,lng;...`, `vehicle=car`, `alternatives=false`, `api_key=<runtime secret>`; response
  chỉ được nhận khi `routes[0].legs` khớp toàn bộ chain yêu cầu. Goong không cung cấp contract
  traffic-aware của Google nên kết quả Goong không bao giờ map thành `TRAFFIC_AWARE`.

## Tasks

### Task 51.0 — Chốt pre-reqs / architecture baseline và public contract

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `SU26SE101_VIETRIDE_technical_context_v7.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `VietRide_API_Contract_v1.md`; `docs/developer-guides/nest/tracking-service-timeline.md` |
| auto-expand scope | Chỉ các mục lục/changelog/version ngay trong bốn file trên cần đồng bộ do thay đổi cùng contract; không mở rộng sang runtime, schema hoặc file generated. |
| forbidden scope | Application code; migrations/snapshot/canonical DDL; env/Compose; API/event/error/Gateway mới; `.env`; secret; dependency mới; Booking/Payment/Parcel/Identity/Notification/RAG/FE-Mobile code; tài liệu lịch sử không liên quan; destructive operations; git branch/push/PR; file untracked của user. |
| depends on | None; phải hoàn tất trước mọi task feature. |
| parallel-safe | no — đây là baseline contract cho 51.1–51.4. |
| verification tier | `DOCS` |
| verification commands | `npx prettier --check SU26SE101_VIETRIDE_technical_context_v7.md BACKEND_SOURCE_OF_TRUTH.md VietRide_API_Contract_v1.md docs/developer-guides/nest/tracking-service-timeline.md docs/handoff/day-51-plan.md`<br>`node -e "const fs=require('node:fs');const files=['SU26SE101_VIETRIDE_technical_context_v7.md','BACKEND_SOURCE_OF_TRUTH.md','VietRide_API_Contract_v1.md','docs/developer-guides/nest/tracking-service-timeline.md'];const text=files.map(f=>fs.readFileSync(f,'utf8')).join('\n');for(const v of ['ROUTING_PROVIDER','GOONG_API_KEY','GOONG_BASE_URL','GOONG_MAX_DESTINATIONS_PER_REQUEST','ROUTE_BASED','GOOGLE_ROUTES','ROUTE_BASELINE'])if(!text.includes(v))throw Error('missing '+v);for(const v of ['401','403','429','timeout','malformed','wrong leg count','cooldown'])if(!text.toLowerCase().includes(v.toLowerCase()))throw Error('missing behavior '+v);const api=fs.readFileSync('VietRide_API_Contract_v1.md','utf8');if(!/TRAFFIC_AWARE\|ROUTE_BASED\|FALLBACK/.test(api))throw Error('public ETA quality set missing');if(!/GOOGLE_ROUTES[^\n]*(histor|lịch sử)/i.test(text))throw Error('historical Google read rule missing');"`<br>`git diff --check -- SU26SE101_VIETRIDE_technical_context_v7.md BACKEND_SOURCE_OF_TRUTH.md VietRide_API_Contract_v1.md docs/developer-guides/nest/tracking-service-timeline.md docs/handoff/day-51-plan.md` |
| full regression owner | `audit-day` |
| invariant flags | LF `.md`; tài liệu `docs/**` bằng tiếng Việt có dấu; BSOT version/changelog bump; không endpoint/event/error/Gateway mới; không secret; Google OAuth/Google Maps display SDK ngoài định tuyến không bị đổi; lịch sử `GOOGLE_ROUTES` không bị xóa. |
| acceptance | Bốn nguồn ghi cùng một quyết định: Goong Directions là provider runtime duy nhất ngoài Local; ordered chain/chunk mặc định 10/cộng dồn/strict validation và secret-safe logging là bắt buộc; Tracking fallback/cooldown và Trip fail-closed giữ nguyên; quality public là đúng bộ ba; `GOOGLE_ROUTES` chỉ còn làm persisted historical source. Mọi câu mô tả Google là runtime ETA/distance/reposition hiện hành được thay hoặc gắn nhãn lịch sử rõ ràng, trong khi Google OAuth và map display SDK ngoài scope không bị đổi. |
| source citations | `BE_TIMELINE_VU.md` §Day 51; `SU26SE101_VIETRIDE_technical_context_v7.md` §3.7 và `TripStop — entity requirements`; `VietRide_API_Contract_v1.md` §Tracking Phase 10 invariants, §Shuttle tracking, `GET /internal/v1/trips/{tripId}/shuttle-road-distance`, §Shuttle fields trong Booking, §DriverSchedule availability; `BACKEND_SOURCE_OF_TRUTH.md` ETA/Trip/Tracking registries; Goong Directions request/response contract được human khóa ngày 2026-08-25. |

### Task 51.1 — Thay provider ETA Tracking bằng Goong và giữ Local fallback

| Field | Value |
|---|---|
| stack/owner | nest / Tracking |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files (base write set) | `apps/tracking/src/config/env.schema.ts`; `apps/tracking/src/config/env.schema.spec.ts`; `apps/tracking/src/eta/eta.constants.ts`; `apps/tracking/src/eta/eta.module.ts`; `apps/tracking/src/eta/eta.service.ts`; `apps/tracking/src/eta/eta.service.spec.ts`; `apps/tracking/src/eta/google-routes-eta.provider.ts` → `apps/tracking/src/eta/goong-directions-eta.provider.ts`; `apps/tracking/src/eta/google-routes-eta.e2e-spec.ts` → `apps/tracking/src/eta/goong-directions-eta.e2e-spec.ts`; `apps/tracking/src/shuttle/shuttle-eta.service.ts`; `apps/tracking/src/shuttle/shuttle-eta.service.spec.ts`; `apps/tracking/src/shuttle/shuttle-google-routes.e2e-spec.ts` → `apps/tracking/src/shuttle/shuttle-goong-directions.e2e-spec.ts`; `apps/tracking/src/tracking-data/dto/eta-response.dto.ts`; `eslint.config.mjs` |
| auto-expand scope | Các Tracking-only test/env fixtures hiện đang khởi tạo `GOOGLE_ROUTES_*` dưới `apps/tracking/src/**/*.{spec.ts,e2e-spec.ts}`; provider interface/DI import cùng feature; fake HTTP fixture/helper trong các spec trên. Rename/remove đúng ba Google-specific source/spec đã liệt kê được human phê duyệt; không xóa file khác. Trong `eslint.config.mjs`, chỉ được thêm glob `apps/tracking/**/*.ts` và hai project `./apps/tracking/tsconfig.app.json`, `./apps/tracking/tsconfig.spec.json` vào typed parser block hiện hữu. |
| forbidden scope | Trip/.NET; Prisma/schema/migration; Gateway/Notification/RAG; public endpoint/socket/event shape ngoài additive `ROUTE_BASED`; Redis key/TTL, ETA recalculate thresholds, delay/off-route flow; new dependency; `.env`/real key; log full URI/query/API key; Booking/Payment/Parcel/FE-Mobile code; mọi thay đổi/disable ESLint rule, parser option khác hoặc config project ngoài đúng ba entry Tracking được cho phép; destructive operations ngoài ba rename/remove đã duyệt; git branch/push/PR; user untracked file. |
| depends on | 51.0 |
| parallel-safe | yes — write envelope chỉ ở `apps/tracking/**`, disjoint với 51.2. |
| verification tier | `PROJECT` — env schema và `EtaModule` là global wiring của Tracking, còn provider phục vụ cả main Trip và Shuttle; ngoài exact unit/E2E filters cần một build của riêng Nx project `tracking` để phát hiện DI/import drift. |
| verification commands | `npx nx test tracking --runInBand --passWithNoTests=false --runTestsByPath src/config/env.schema.spec.ts src/eta/eta.service.spec.ts src/shuttle/shuttle-eta.service.spec.ts; if($LASTEXITCODE -ne 0){throw 'Tracking Goong unit tests failed'}`<br>`npx nx run tracking:test:e2e --runInBand --runTestsByPath apps/tracking/src/eta/goong-directions-eta.e2e-spec.ts --passWithNoTests=false; if($LASTEXITCODE -ne 0){throw 'Tracking Goong ETA fake HTTP E2E failed'}`<br>`npx nx run tracking:test:e2e --runInBand --runTestsByPath apps/tracking/src/shuttle/shuttle-goong-directions.e2e-spec.ts --passWithNoTests=false; if($LASTEXITCODE -ne 0){throw 'Tracking Goong Shuttle fake HTTP E2E failed'}`<br>`npx nx run tracking:build; if($LASTEXITCODE -ne 0){throw 'Tracking project build failed'}`<br>`$scope=@('apps/tracking','eslint.config.mjs'); $tracked=@(git diff --name-only --diff-filter=ACMR -- $scope); $untracked=@(git ls-files --others --exclude-standard -- $scope); $changed=@($tracked+$untracked|Where-Object{(Test-Path -LiteralPath $_) -and (($_ -match '\.(ts|json)$') -or $_ -eq 'eslint.config.mjs')}|Sort-Object -Unique); if($changed.Count -eq 0){throw 'Tracking changed-file ledger empty'}; if($changed -notcontains 'eslint.config.mjs'){throw 'Tracking typed-parser config is absent from changed ledger'}; & npx eslint @changed; if($LASTEXITCODE -ne 0){throw 'Tracking changed-file lint with type information failed'}; $trackingFormat=@($changed|Where-Object{$_ -ne 'eslint.config.mjs' -and $_ -match '\.(ts|json)$'}); if($trackingFormat.Count -eq 0){throw 'Tracking Prettier ledger empty'}; & npx prettier --check @trackingFormat; if($LASTEXITCODE -ne 0){throw 'Tracking TS/JSON changed-file format failed'}; if($tracked.Count -gt 0){& git diff --check -- @tracked; if($LASTEXITCODE -ne 0){throw 'Tracking tracked-file diff hygiene failed'}}; foreach($file in $untracked){$check=@(& git diff --no-index --check -- NUL $file 2>&1);$exit=$LASTEXITCODE;$text=($check -join "`n").Trim();if($exit -notin @(0,1)){throw "Tracking untracked diff check failed for $file with exit $exit"};if($text.Length -gt 0){throw "Tracking untracked whitespace error in $file`: $text"}}`<br>`if(rg -n 'GOOGLE_ROUTES|GoogleRoutes|routes\.googleapis\.com' apps/tracking/src apps/tracking/project.json){throw 'Google routing runtime/test residue remains in Tracking'}` |
| full regression owner | `audit-day` |
| invariant flags | LF `.ts/.json`; no dependency mới; Zod env validation; pino/Nest logger nhưng không log URL query/key; `ROUTING_PROVIDER` chỉ `GOONG|LOCAL`; mặc định chunk 10; Redis cache 60s, cooldown ba lỗi/300s và thresholds 500m/ETA<15m giữ nguyên; Shuttle public payload giữ nguyên ngoài quality dùng chung; cancellation phải propagate; `eslint.config.mjs` giữ nguyên baseline formatting/rules ngoài đúng ba entry additive Tracking và không được chạy Prettier trên legacy root config. |
| acceptance | Provider phát đúng ordered Goong GET chain, chunk tối đa cấu hình và dùng đích cuối chunk trước làm origin chunk kế; chỉ trả kết quả khi từng leg có distance/duration không âm, count và start/end order khớp request, rồi cộng dồn ETA đúng qua mọi chunk. `GOONG` thành công trả `ROUTE_BASED`; `LOCAL` hoặc mọi lỗi/timeout/malformed/partial trả Local `FALLBACK` theo flow hiện hành; ba lỗi provider liên tiếp kích cooldown 300 giây và request bị cancellation không bị tính như kết quả thành công. Main Trip/Shuttle không đổi lock/cache/recalculate/monotonic target behavior, không chờ provider trên GPS acknowledgement và không lộ provider/key. Env từ chối provider ngoài `GOONG|LOCAL`, yêu cầu nonblank key khi chọn GOONG và không yêu cầu key khi chọn LOCAL. Changed-file ESLint chạy với type information từ đúng Tracking app/spec tsconfig, không disable hoặc nới lỏng rule. Diff `eslint.config.mjs` chỉ thêm `apps/tracking/**/*.ts`, `./apps/tracking/tsconfig.app.json` và `./apps/tracking/tsconfig.spec.json` vào typed parser block hiện hữu; mọi dòng/config formatting baseline khác được giữ nguyên. |
| source citations | `BE_TIMELINE_VU.md` §Day 51; Task 51.0 baseline; `VietRide_API_Contract_v1.md` §Tracking Phase 10 invariants và §Shuttle tracking; `docs/developer-guides/nest/tracking-service-timeline.md` Phase 4, Phase 10, Phase 11; `AGENTS_NESTJS.md` env/logging/testing rules; Goong Direction `routes[0].legs[].distance.value`/`duration.value` contract được human khóa ngày 2026-08-25. |

### Task 51.2 — Thay ba Trip routing client bằng Goong và map public quality

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (base write set) | `apps/trip/src/VietRide.Trip.Domain/Entities/PlannedEtaSource.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/TripProjectionMapper.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/GoogleRoutesTripEtaPlanner.cs` → `GoongDirectionsTripEtaPlanner.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/GoogleRoutesShuttleDistanceClient.cs` → `GoongDirectionsShuttleDistanceClient.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/GoogleRoutesRepositionTravelTimeClient.cs` → `GoongDirectionsRepositionTravelTimeClient.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/ExternalClients/GoogleRoutesTripEtaPlannerTests.cs` → `GoongDirectionsTripEtaPlannerTests.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/ExternalClients/GoogleRoutesRepositionTravelTimeClientTests.cs` → `GoongDirectionsRepositionTravelTimeClientTests.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/ExternalClients/GoongDirectionsShuttleDistanceClientTests.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/Trips/TripHandlerProjectionTests.cs` |
| auto-expand scope | Trip-only provider abstractions/helpers, DI wiring tests và focused resource-availability/shuttle-distance tests trực tiếp cần để chứng minh cùng ba client; rename/remove đúng các Google-specific source/test đã liệt kê được human phê duyệt. Không mở rộng vào migration/snapshot/canonical DDL của 51.3. |
| forbidden scope | Tracking/Nest; EF migrations/snapshot/canonical DDL; endpoint/Gateway/event/error changes; Booking/Payment/Parcel/Identity; availability turnaround/business thresholds; Haversine fallback cho Shuttle eligibility/reposition; new NuGet/dependency; `.env`/real key; logging URI/query/key; destructive operations ngoài các rename/remove đã duyệt; git branch/push/PR; user untracked file. |
| depends on | 51.0 |
| parallel-safe | yes — `apps/trip/**` envelope disjoint với 51.1; phải land trước 51.3. |
| verification tier | `FOCUSED` |
| verification commands | `function Invoke-Day51DotnetTest([string]$Project,[string]$Filter,[string]$Tag){$dir='TestResults/day51';New-Item -ItemType Directory -Force $dir|Out-Null;$name="$Tag-$([guid]::NewGuid()).trx";dotnet test $Project -c Release --filter $Filter --results-directory $dir --logger "trx;LogFileName=$name";if($LASTEXITCODE -ne 0){throw "$Tag test command failed"};[xml]$trx=Get-Content -Raw (Join-Path $dir $name);$c=$trx.TestRun.ResultSummary.Counters;if([int]$c.executed -lt 1 -or [int]$c.failed -ne 0){throw "$Tag must execute at least one test with zero failures"}};$unit='apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj';Invoke-Day51DotnetTest $unit 'FullyQualifiedName~GoongDirectionsTripEtaPlannerTests|FullyQualifiedName~GoongDirectionsShuttleDistanceClientTests|FullyQualifiedName~GoongDirectionsRepositionTravelTimeClientTests' 'trip-goong-clients'`<br>`function Invoke-Day51DotnetTest([string]$Project,[string]$Filter,[string]$Tag){$dir='TestResults/day51';New-Item -ItemType Directory -Force $dir|Out-Null;$name="$Tag-$([guid]::NewGuid()).trx";dotnet test $Project -c Release --filter $Filter --results-directory $dir --logger "trx;LogFileName=$name";if($LASTEXITCODE -ne 0){throw "$Tag test command failed"};[xml]$trx=Get-Content -Raw (Join-Path $dir $name);$c=$trx.TestRun.ResultSummary.Counters;if([int]$c.executed -lt 1 -or [int]$c.failed -ne 0){throw "$Tag must execute at least one test with zero failures"}};Invoke-Day51DotnetTest 'apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj' 'FullyQualifiedName~TripHandlerProjectionTests' 'trip-eta-projection'`<br>`$changed=(git diff --name-only --diff-filter=ACMR -- apps/trip | Where-Object { $_ -match '\.cs$' -and $_ -notmatch '/Migrations/' }); if(-not $changed){throw 'Trip runtime changed-file ledger empty'}; dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include $changed; if($LASTEXITCODE -ne 0){throw 'Trip runtime changed-file format failed'}; git diff --check -- $changed; if($LASTEXITCODE -ne 0){throw 'Trip runtime diff hygiene failed'}`<br>`if(rg -n 'GOOGLE_ROUTES_ENABLED|GOOGLE_ROUTES_API_KEY|GOOGLE_ROUTES_BASE_URL|GoogleRoutes' apps/trip/src --glob '!**/Migrations/**'){throw 'Google routing runtime residue remains in Trip'}` |
| full regression owner | `audit-day` |
| invariant flags | CRLF `.cs`; MediatR v11/CPM unchanged; no dependency mới; typed `HttpClient`; cancellation propagated; no secret/full URI logging; Trip planned Local fallback giữ dwell/cumulative metrics; Shuttle distance và reposition tuyệt đối không dùng Haversine hoặc kết quả bán phần; historical enum `GOOGLE_ROUTES` giữ nguyên. |
| acceptance | Planned ETA gọi Goong theo ordered stops/destination, chunk/cộng dồn và strict leg validation giống Task 51.1; thành công ghi source `GOONG`, còn provider LOCAL/missing/unavailable/malformed trả exact `ROUTE_BASELINE` plan hiện hành. Shuttle road distance chỉ trả một distance hợp lệ từ complete response; mọi lỗi trả unavailable để endpoint giữ `503 SHUTTLE_DISTANCE_UNAVAILABLE`. Reposition chỉ trả thời gian hợp lệ từ complete response; mọi lỗi giữ `503 RESOURCE_TRAVEL_TIME_UNAVAILABLE` và không tạo reservation/assignment bán phần. Projection map đúng `GOOGLE_ROUTES→TRAFFIC_AWARE`, `GOONG→ROUTE_BASED`, `ROUTE_BASELINE→FALLBACK`; không nhánh nào map Goong thành traffic-aware. |
| source citations | `BE_TIMELINE_VU.md` §Day 51; Task 51.0 baseline; `VietRide_API_Contract_v1.md` `GET /internal/v1/trips/{tripId}/shuttle-road-distance`, §Shuttle fields trong Booking và §DriverSchedule availability; `db-schema/trip-route-vehicle/README.md` Planned ETA; `AGENTS_DOTNET.md` HTTP/DI/result/testing invariants; Goong Direction leg contract được human khóa ngày 2026-08-25. |

### Task 51.3 — Tạo EF migration reversible cho `PlannedEtaSource.GOONG`

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip DB |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files (base write set) | `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/TripDbContextModelSnapshot.cs`; `db-schema/trip-route-vehicle/schema.sql`; `db-schema/trip-route-vehicle/README.md`; `apps/trip/tests/VietRide.Trip.UnitTests/Infrastructure/GoongPlannedEtaSourceMigrationTests.cs` |
| auto-expand scope | Exact generated `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/*_AddGoongPlannedEtaSource.cs` và matching `.Designer.cs`; enum/model/config file của 51.2 chỉ được patch khi EF model generation chứng minh thiếu mapping trực tiếp cho migration acceptance. |
| forbidden scope | Sửa bất kỳ migration đã merge nào; bảng/cột/index/FK khác; cross-DB FK; data rewrite ngoài `GOONG→ROUTE_BASELINE` trong Down; runtime clients/Tracking/env/Compose; new NuGet/dependency; `.env`/production DB/secret; destructive database operation ngoài scratch DB tên `vietride_trip_day51_migration`; git branch/push/PR; user untracked file. |
| depends on | 51.2 |
| parallel-safe | no — dùng enum/model từ 51.2 và Trip migration/snapshot là write set độc quyền. |
| verification tier | `FOCUSED` |
| verification commands | `dotnet ef migrations add AddGoongPlannedEtaSource -p apps/trip/src/VietRide.Trip.Infrastructure -s apps/trip/src/VietRide.Trip.Api -o Migrations; if($LASTEXITCODE -ne 0){throw 'Trip Goong migration generation failed'}` (chạy đúng một lần khi implement; không chạy lại trong verify)<br>`$migrations=@(Get-ChildItem apps/trip/src/VietRide.Trip.Infrastructure/Migrations -Filter '*_AddGoongPlannedEtaSource.cs' | Where-Object Name -notmatch 'Designer'); if($migrations.Count -ne 1){throw 'exactly one AddGoong migration required'}; $migration=$migrations[0]; $migrationId=$migration.BaseName; $prior='20260823092407_AddTripBusinessCodesReleaseA'; $project='apps/trip/src/VietRide.Trip.Infrastructure'; $startup='apps/trip/src/VietRide.Trip.Api'; docker compose -f infra/docker/docker-compose.yml up -d postgres; if($LASTEXITCODE -ne 0){throw 'Postgres startup failed'}; $ready=$false; 1..20|ForEach-Object{if(-not $ready){docker compose -f infra/docker/docker-compose.yml exec -T postgres pg_isready -U vietride -d postgres; if($LASTEXITCODE -eq 0){$ready=$true}else{Start-Sleep -Seconds 1}}}; if(-not $ready){throw 'Postgres readiness failed'}; $env:TRIP_DESIGN_CONNECTION='Host=localhost;Port=5432;Database=vietride_trip_day51_migration;Username=vietride;Password=vietride_dev'; dotnet ef database drop --force -p $project -s $startup; if($LASTEXITCODE -ne 0){throw 'scratch DB drop failed'}; dotnet ef database update $migrationId -p $project -s $startup; if($LASTEXITCODE -ne 0){throw 'empty apply failed'}; dotnet ef database update $prior -p $project -s $startup; if($LASTEXITCODE -ne 0){throw 'Down-to-prior failed'}; dotnet ef database update $migrationId -p $project -s $startup; if($LASTEXITCODE -ne 0){throw 'reapply failed'}; dotnet ef migrations has-pending-model-changes -p $project -s $startup; if($LASTEXITCODE -ne 0){throw 'pending model changes remain'}`<br>`$up=(dotnet ef migrations script 20260823092407_AddTripBusinessCodesReleaseA $migrationId -p $project -s $startup) -join "`n"; if($LASTEXITCODE -ne 0){throw 'Up SQL generation failed'}; $down=(dotnet ef migrations script $migrationId 20260823092407_AddTripBusinessCodesReleaseA -p $project -s $startup) -join "`n"; if($LASTEXITCODE -ne 0){throw 'Down SQL generation failed'}; foreach($sql in @($up,$down)){if($sql -notmatch '(?is)"?vietride_trip"?\s*\.\s*"?planned_eta_source"?'){throw 'SQL must target snake_case vietride_trip.planned_eta_source'};if($sql -match '(?is)\bCREATE\s+TABLE\b|\bDROP\s+TABLE\b|\bADD\s+COLUMN\b|\bDROP\s+COLUMN\b|\bFOREIGN\s+KEY\b|\bREFERENCES\b'){throw 'enum migration SQL contains forbidden table/column/FK change'}}; if($up -notmatch 'GOONG'){throw 'Up SQL missing GOONG'}; $remap=[regex]::Match($down,'(?is)UPDATE\b[^;]*planned_eta_source[^;]*GOONG[^;]*ROUTE_BASELINE[^;]*;'); if(-not $remap.Success){throw 'Down SQL missing GOONG to ROUTE_BASELINE remap'}; $firstDrop=$down.IndexOf('DROP TYPE',[System.StringComparison]::OrdinalIgnoreCase); if($firstDrop -lt 0 -or $remap.Index -ge $firstDrop){throw 'Down remap must precede the first DROP TYPE'}; $ddl=Get-Content -Raw db-schema/trip-route-vehicle/schema.sql; if($ddl -notmatch "planned_eta_source AS ENUM \('GOOGLE_ROUTES', 'GOONG', 'ROUTE_BASELINE'\)"){throw 'canonical DDL enum mismatch'}`<br>`function Invoke-Day51DotnetTest([string]$Project,[string]$Filter,[string]$Tag){$dir='TestResults/day51';New-Item -ItemType Directory -Force $dir|Out-Null;$name="$Tag-$([guid]::NewGuid()).trx";dotnet test $Project -c Release --filter $Filter --results-directory $dir --logger "trx;LogFileName=$name";if($LASTEXITCODE -ne 0){throw "$Tag test command failed"};[xml]$trx=Get-Content -Raw (Join-Path $dir $name);$c=$trx.TestRun.ResultSummary.Counters;if([int]$c.executed -lt 1 -or [int]$c.failed -ne 0){throw "$Tag must execute at least one test with zero failures"}};Invoke-Day51DotnetTest 'apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj' 'FullyQualifiedName~GoongPlannedEtaSourceMigrationTests' 'trip-goong-migration'`<br>`$root=(Get-Location).Path; $generated=@(Get-ChildItem apps/trip/src/VietRide.Trip.Infrastructure/Migrations -Filter '*AddGoongPlannedEtaSource*.cs' | ForEach-Object { $_.FullName.Substring($root.Length+1) }); if($generated.Count -ne 2){throw 'migration and designer paths required'}; $base=@('apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs','apps/trip/src/VietRide.Trip.Infrastructure/Migrations/TripDbContextModelSnapshot.cs','apps/trip/tests/VietRide.Trip.UnitTests/Infrastructure/GoongPlannedEtaSourceMigrationTests.cs','db-schema/trip-route-vehicle/schema.sql','db-schema/trip-route-vehicle/README.md'); $optionalCandidates=@('apps/trip/src/VietRide.Trip.Domain/Entities/PlannedEtaSource.cs','apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/TripConfiguration.cs'); $changed=@(git diff --name-only --diff-filter=ACMR -- $optionalCandidates); $optional=@($optionalCandidates|Where-Object{$changed -contains $_}); $taskLedger=@($base+$generated+$optional|Sort-Object -Unique); $csLedger=@($taskLedger|Where-Object{$_ -match '\.cs$'}); if($csLedger.Count -lt 5){throw 'migration C# ledger incomplete'}; dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include $csLedger; if($LASTEXITCODE -ne 0){throw 'migration changed-file format failed'}; git diff --check -- $taskLedger; if($LASTEXITCODE -ne 0){throw 'migration task-ledger diff hygiene failed'}` |
| full regression owner | `audit-day` |
| invariant flags | CRLF generated `.cs`; LF SQL/README; design-time factory, không host boot; snake_case; enum final `GOOGLE_ROUTES,GOONG,ROUTE_BASELINE`; real reversible Down; historical rows preserved; no cross-DB FK/table/column/index; prior migration exact `20260823092407_AddTripBusinessCodesReleaseA`; canonical DDL/snapshot đồng bộ. |
| acceptance | Up mở rộng đúng enum và không sửa dữ liệu lịch sử; model/snapshot nhận `GOONG`. Sau khi chèn một Trip source `GOONG` trong migration test, Down chuyển row đó thành `ROUTE_BASELINE` trước khi rebuild enum cũ chỉ còn `GOOGLE_ROUTES|ROUTE_BASELINE`; row historical `GOOGLE_ROUTES` và `ROUTE_BASELINE` giữ nguyên. Empty apply, down-to-prior, reapply và pending-model lifecycle chạy trên scratch DB; generated SQL chỉ thay enum cần thiết và không tạo thay đổi schema ngoài scope. |
| source citations | `BE_TIMELINE_VU.md` §Day 51; Task 51.0 baseline; `db-schema/trip-route-vehicle/schema.sql` `planned_eta_source`; `db-schema/trip-route-vehicle/README.md` `Trip.planned_eta_source`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/20260823092407_AddTripBusinessCodesReleaseA.cs`; `.agents/skills/ef-migration/SKILL.md`; `AGENTS_DOTNET.md` EF/design-time/reversible migration rules. |

### Task 51.4 — Đồng bộ deployment config, live gate Việt Nam và handoff FE/Mobile

| Field | Value |
|---|---|
| stack/owner | cross-cutting / infra + QA + docs |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `.env.example`; `infra/docker/docker-compose.yml`; `infra/docker/docker-compose.prod.yml`; `infra/docker/docker-compose.tracking-sharing-e2e.yml`; `infra/docker/docker-compose.day36-e2e.yml`; `scripts/google-routes-live-e2e-stub.mjs` → `scripts/goong-directions-live-gate.mjs`; `scripts/goong-directions-live-gate.spec.mjs`; `scripts/fixtures/goong-vietnam-routes.json`; `docs/runbooks/goong-routing-rollout.md`; `docs/handoff/day-51-goong-routing-fe-mobile-handoff.md` |
| auto-expand scope | Các Compose/E2E env fixtures hoặc scripts dưới `infra/docker/**` và `scripts/**` còn truyền Google routing config cho Tracking/Trip; package script trong `package.json` chỉ khi cần expose exact Day-51 live/self-test command, không thêm dependency. Rename/remove đúng Google live stub đã liệt kê được human phê duyệt. |
| forbidden scope | Runtime code của 51.1–51.3; schema/migrations; endpoint/Gateway/event/error; Booking/Payment/Parcel/Identity/Notification/RAG/FE-Mobile code; sửa hoặc commit real `.env` (chỉ được đọc bằng Node native `--env-file=.env` cho live gate); hardcode/commit/log `GOONG_API_KEY`; production deploy/push/PR; dependency mới; thay đổi ports/network/credentials ngoài routing env; destructive operations ngoài rename stub đã duyệt; user untracked file. |
| depends on | 51.1, 51.2, 51.3 |
| parallel-safe | no — đây là integration/handoff gate sau khi cả hai stack và migration đã land. |
| verification tier | `FOCUSED` |
| verification commands | `node --test scripts/goong-directions-live-gate.spec.mjs; if($LASTEXITCODE -ne 0){throw 'Goong live-gate self tests failed'}`<br>`docker compose -f infra/docker/docker-compose.yml config --quiet; if($LASTEXITCODE -ne 0){throw 'dev compose config invalid'}; docker compose -f infra/docker/docker-compose.prod.yml config --quiet; if($LASTEXITCODE -ne 0){throw 'prod compose config invalid'}; docker compose -f infra/docker/docker-compose.tracking-sharing-e2e.yml config --quiet; if($LASTEXITCODE -ne 0){throw 'tracking E2E compose invalid'}; docker compose -f infra/docker/docker-compose.day36-e2e.yml config --quiet; if($LASTEXITCODE -ne 0){throw 'day36 E2E compose invalid'}`<br>`$nodeArgs=@();if(Test-Path -LiteralPath '.env'){$nodeArgs+='--env-file=.env'};$nodeArgs+='scripts/goong-directions-live-gate.mjs';$nodeArgs+=@('--fixture','scripts/fixtures/goong-vietnam-routes.json','--minimum-routes','50','--minimum-multipoint-routes','5','--timeout-ms','5000');node @nodeArgs;if($LASTEXITCODE -ne 0){throw 'Goong Vietnam live gate failed (the script must fail fast when GOONG_API_KEY is absent)'}`<br>`node -e "const fs=require('node:fs');const x=JSON.parse(fs.readFileSync('scripts/fixtures/goong-vietnam-routes.json','utf8'));if(!Array.isArray(x.routes)||x.routes.length<50)throw Error('need >=50 routes');const multi=x.routes.filter(r=>Array.isArray(r.points)&&r.points.length>=11&&r.points.length<=30);if(multi.length<5)throw Error('need >=5 routes with 11-30 points');for(const r of x.routes){if(!r.name||!Array.isArray(r.points)||r.points.length<2)throw Error('bad fixture '+(r.name||'?'));for(const p of r.points)if(!Number.isFinite(p.lat)||!Number.isFinite(p.lng)||p.lat<8||p.lat>24||p.lng<102||p.lng>110)throw Error('point outside Vietnam gate '+r.name)}"`<br>`$base=@('.env.example','infra/docker/docker-compose.yml','infra/docker/docker-compose.prod.yml','infra/docker/docker-compose.tracking-sharing-e2e.yml','infra/docker/docker-compose.day36-e2e.yml','scripts/google-routes-live-e2e-stub.mjs','scripts/goong-directions-live-gate.mjs','scripts/goong-directions-live-gate.spec.mjs','scripts/fixtures/goong-vietnam-routes.json','docs/runbooks/goong-routing-rollout.md','docs/handoff/day-51-goong-routing-fe-mobile-handoff.md'); $changed=@(git diff --name-only --diff-filter=ACMRD -- infra/docker scripts package.json); $untracked=@(git ls-files --others --exclude-standard -- infra/docker scripts package.json); $taskLedger=@($base+$changed+$untracked|Sort-Object -Unique); if((git diff --name-only -- package.json) -or (git ls-files --others --exclude-standard -- package.json)){$taskLedger+=@('package.json')}; $taskLedger=@($taskLedger|Sort-Object -Unique); $prettierLedger=@($taskLedger|Where-Object{(Test-Path -LiteralPath $_) -and $_ -match '\.(?:ya?ml|m?js|json|md)$'}); if($prettierLedger.Count -lt 9){throw 'Day 51 Prettier ledger incomplete'}; npx prettier --check $prettierLedger; if($LASTEXITCODE -ne 0){throw 'Day 51 supported-parser ledger format failed'}; git diff --check -- $taskLedger; if($LASTEXITCODE -ne 0){throw 'Day 51 full task-ledger diff hygiene failed'}`<br>`$runtime=@('.env.example','infra/docker/docker-compose.yml','infra/docker/docker-compose.prod.yml','infra/docker/docker-compose.tracking-sharing-e2e.yml','infra/docker/docker-compose.day36-e2e.yml'); if(rg -n 'GOOGLE_ROUTES_API_KEY|GOOGLE_ROUTES_ENABLED|GOOGLE_ROUTES_BASE_URL|routes\.googleapis\.com' $runtime scripts/goong-directions-live-gate.mjs){throw 'Google routing config remains in Day 51 runtime/deploy path'}; $joined=($runtime|ForEach-Object{Get-Content -Raw $_})-join "`n"; foreach($v in @('ROUTING_PROVIDER','GOONG_API_KEY','GOONG_BASE_URL','GOONG_MAX_DESTINATIONS_PER_REQUEST','TRACKING_ROUTING_TIMEOUT_MS')){if(-not $joined.Contains($v)){throw "missing deploy config $v"}}` |
| full regression owner | `audit-day` |
| invariant flags | LF env/YAML/JS/JSON/MD; docs tiếng Việt có dấu; không dependency mới; không secret committed/logged; live script không in full request URI; fixture chỉ có tọa độ/tên công khai; sequential/bounded calls; Google OAuth/map display vars ngoài routing không bị xóa; production-like boot/health/business full matrix chỉ do `audit-day 51` chạy. |
| acceptance | Mọi deploy/E2E config truyền cùng năm biến routing chuẩn cho Tracking/Trip và không còn phụ thuộc Google routing key. Live script fail-fast nếu thiếu key, không in key/query/full URL, chạy bounded/sequential, xác nhận ít nhất 50 route Việt Nam và ≥5 route 11–30 điểm, 100% request HTTP 200/parse hợp lệ/order đúng/distance-duration không âm, rồi tính p95 và fail nếu p95 không nhỏ hơn timeout. Self-test dùng fake server bao phủ 401/403/429/5xx, timeout, malformed, wrong count/order và chứng minh output redaction. Handoff nói rõ FE/Mobile chỉ cần thêm enum additive `ROUTE_BASED`, không đổi endpoint/payload khác; runbook ghi rollout `GOONG`, rollback `LOCAL`, secret injection, quota/429 monitoring và lệnh audit-day production-like smoke. |
| source citations | `BE_TIMELINE_VU.md` §Day 51 Review/DoD; Task 51.0 baseline; `.env.example`; `infra/docker/docker-compose.yml` và `.prod.yml`; `scripts/google-routes-live-e2e-stub.mjs` hiện hành; `VietRide_API_Contract_v1.md` public ETA quality/Shuttle payload; Goong Direction contract được human khóa ngày 2026-08-25. |

## Dispatch order

1. Task 51.0 — chốt architecture/contract baseline.
2. Sau 51.0, dispatch song song Task 51.1 (Tracking) và Task 51.2 (Trip runtime); write sets disjoint.
3. Task 51.3 — tạo/verify Trip EF migration sau khi enum/model của 51.2 đã land.
4. Task 51.4 — đồng bộ deployment/live gate/handoff sau 51.1–51.3.
5. Reviewer độc lập review toàn bộ Day 51; sau khi mọi targeted gate xanh, chạy `/audit-day 51` để sở hữu full touched-project regression, production-like Docker Gateway/direct health matrix và business E2E. Finding audit phải thành patch task có targeted verify rồi audit lại; không đóng Day nếu còn avoidable skip.

## Progress tracker

> Orchestrator bookkeeping — main thread cập nhật sau mỗi `/implement-task` hoặc task hoàn tất bởi
> `/execute-day`. Bảng này không phải bằng chứng audit; `/audit-day 51` phải tự chạy lại verification
> theo source-of-truth.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 51.0 | ✅ done | APPROVE | 2026-08-25 | 3 patch rounds; no scope expansion; DOCS gate green. |
| 51.1 | ✅ done | APPROVE | 2026-08-25 | Command corrections + 1 reviewer patch; Tracking fixtures and typed-ESLint config expanded in-scope; 40 unit and 109 E2E tests green. |
| 51.2 | ✅ done | APPROVE | 2026-08-25 | 1 reviewer patch round; 3 in-envelope support files; 48/48 client + 32/32 projection tests green. |
| 51.3 | ⬜ todo | — | — | — |
| 51.4 | ⬜ todo | — | — | — |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + targeted verification green) · ⚠️ done-with-carryover · ❌ blocked

## Open questions

- Không có câu hỏi contract/behavior blocking: endpoint, chunk mặc định 10, quality mapping,
  fallback/fail-closed và phạm vi rename/remove đã được human khóa ngày 2026-08-25.
- Timeline chưa ghi Jira key cho Day 51; cần bổ sung để traceability nhưng không chặn implementation.
- Giá trị `GOONG_API_KEY` và cơ chế inject secret cụ thể của staging không được ghi trong repo/plan.
  Người vận hành phải expose biến này cho live gate và container; thiếu key là gate failure, không phải
  lý do skip hay hardcode.
