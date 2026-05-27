# Day 1-2 Full Completeness Audit Prompt

> **Mục tiêu**: verify source code đã setup hoàn thiện 100% theo Jira Day 1 (SCV-8/9) + Day 2 (SCV-69/70).
> Dev khác clone repo về phải code Day 3 được NGAY mà không phải setup thêm bất cứ thứ gì.
>
> **Cách dùng**: mở session Claude Code mới, `cd` vào repo, gõ:
> ```
> @docs/internal/day-1-2-review-prompt.md
> ```

---

## PROMPT (paste từ đây trở xuống)

Bạn là **independent backend reviewer** cho VietRide capstone (SU26SE101). Working directory: `D:\Source Code\Source C#\SU26SE101-Capstone-Project-VietRide-BE`.

## Bối cảnh
Project là Nx 22 monorepo polyglot: 5 .NET 8 microservice (Clean Architecture) + NestJS Gateway + 3 NestJS worker + 6 .NET shared libs + 6 TS shared libs + Postgres/Redis/RabbitMQ + observability stack. Session trước đó đã scaffold Day 1-2 nhưng **có agent bị hit session limit giữa chừng** → KHÔNG TIN báo cáo cũ, audit từ đầu.

## Mục tiêu audit
Trả lời 2 câu hỏi:

1. **"Source code có ĐÚNG kiến trúc + contract theo source-of-truth không?"** — đối chiếu mọi file với 4 doc gốc: `SU26SE101_VIETRIDE_technical_context_v7.md`, `VietRide_API_Contract_v1.md`, `BACKEND_SOURCE_OF_TRUTH.md`, các ADR. Sai truth = bug kiến trúc, dù file có tồn tại đầy đủ.

2. **"Dev khác (BE/FE) clone repo về ngay bây giờ, có code Day 3 được luôn không, hay phải setup thêm gì?"** — đối chiếu với DoD trong `BE_TIMELINE_VU.md` Day 1-2. Setup thêm = FAIL delivery scope.

**Cả 2 câu phải trả ✅ mới gọi là "Day 1-2 hoàn thiện 100%".** Nếu chỉ trả ✅ câu 2 (timeline xong) mà câu 1 sai (kiến trúc lệch truth) → vẫn là FAIL — vì dev sau sẽ code sai pattern.

## Phương pháp BẮT BUỘC (đừng skip bước nào)

### Bước 1 — Đọc TOÀN BỘ source-of-truth (KHÔNG CHỈ timeline)

**⚠️ Quan trọng**: timeline (`BE_TIMELINE_VU.md`) chỉ là lịch giao hàng — nó nói "Day 2 phải có gì xong" nhưng KHÔNG nói "có gì là đúng kiến trúc". Source-of-truth thực sự nằm ở các doc kiến trúc + business + API contract. Phải đọc theo thứ tự ưu tiên dưới đây, không được skip:

#### 1.1. Truth tier 0 — Business/Technical context (top authority)
- `SU26SE101_VIETRIDE_technical_context_v7.md` (ROOT) — **business + tech context gốc từ giảng viên/PO**. Đây là spec gốc nhất, mọi thứ khác phải nhất quán với nó. Đọc TOÀN BỘ, ghi chú: business domain, vai trò user, các bounded context, ràng buộc kỹ thuật, NFR.
- `VietRide_API_Contract_v1.md` (ROOT) — **API contract gốc**. Mọi endpoint Gateway proxy + service expose phải khớp contract này: route, method, request/response shape, status code, error envelope.

#### 1.2. Truth tier 1 — Backend architecture truth
- `BACKEND_SOURCE_OF_TRUTH.md` (ROOT) — **kiến trúc backend chi tiết**, derived từ tier 0. Đọc TOÀN BỘ, đặc biệt:
  - Section monorepo layout (apps/libs/tests/infra/docs)
  - Section service breakdown (5 .NET service + 4 NestJS app, bounded context của mỗi service)
  - Section Gateway design (thin proxy, route table, public whitelist)
  - Section Internal JWT (HS256, TTL 120s, issuer/audience, claims schema)
  - Section User Access Token (RS256, JWKS, issuer/audience)
  - Section persistence (EF Core, schema per service, snake_case naming, Money BIGINT, Outbox)
  - Section messaging (RabbitMQ topic exchange, event envelope, integration event naming)
  - Section observability (Serilog, OpenTelemetry traces + metrics, /metrics endpoint, structured log fields)
  - Section error handling (RFC 7807 ProblemDetails, error code naming convention, status code mapping)
  - Section anti-patterns (Mapster vs AutoMapper, MediatR pinned, no commercial deps, no FE-specific in service)
  - Section build/CI (TreatWarningsAsErrors, ignoreDeprecations, Nx targets)
  - Changelog version mới nhất (xác định ngày update gần nhất)

#### 1.3. Truth tier 2 — ADR (kiến trúc đã chốt)
- `docs/adr/0001-monorepo-layout.md` — lý do chọn layout apps/libs/tests
- `docs/adr/0002-gateway-thin-proxy-vs-bff.md` — quyết định thin-proxy + ngưỡng kích hoạt selective BFF
- (List bất kỳ ADR khác nếu có)

#### 1.4. Truth tier 3 — Operational + security
- `docs/SECURITY.md` — dev defaults + rotation checklist trước prod
- `docs/runbooks/*.md` — vận hành (nếu có)
- `docs/api/openapi/*` — OpenAPI spec (nếu có)
- `docs/deliverables/*` — Day-end deliverables (nếu có)

#### 1.5. Schedule layer (LAST, không phải first)
- `BE_TIMELINE_VU.md` (ROOT) — section Day 1 (SCV-8, SCV-9) + Day 2 (SCV-69, SCV-70). Đây là **delivery schedule**, dùng để biết "Day 2 phải bao gồm những gì". KHÔNG được dùng làm spec kiến trúc.

#### 1.6. Onboarding layer
- `README.md` (ROOT) — clone-and-go instruction cho dev mới.

#### Output của Bước 1
Viết ra **3 bảng**:

**Bảng A — Truth matrix**: liệt kê các quy tắc kiến trúc/contract gốc rút ra từ tier 0 + tier 1 + tier 2. Cột: (Quy tắc | Nguồn | Section/page). Ví dụ: "Internal JWT HS256 issuer=vietride-gateway audience=vietride-internal TTL 120s — BACKEND_SOURCE_OF_TRUTH §5.3" | "Money BIGINT floor 1000 VND — BACKEND_SOURCE_OF_TRUTH §4.4" | "Route /v1/auth/login POST returns AuthTokenResponse — VietRide_API_Contract_v1 §2.1" | v.v.

**Bảng B — Day 1-2 delivery scope**: từ `BE_TIMELINE_VU.md`, liệt kê DoD strict của SCV-8/9/69/70. Cột: (Ticket | Deliverable | DoD).

**Bảng C — Truth ↔ Delivery cross-check**: với mỗi quy tắc ở Bảng A, đánh dấu xem nó có rơi vào scope Day 1-2 không (Y/N/PARTIAL). Mục đích: không miss quy tắc nào tưởng là "Day 3+" nhưng thực ra Day 1-2 đã phải implement.

### Bước 2 — Khám phá hiện trạng (không sửa, chỉ đọc)
- `npx nx show projects` → liệt kê tất cả project Nx detect được
- Tree thư mục depth 4 cho `apps/`, `libs/`, `tests/`, `infra/`, `.github/`, `docs/`
- Đếm file `.cs` thật (loại bỏ obj/bin): `find apps libs -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | wc -l`
- Đếm file `.ts` thật (loại bỏ node_modules/dist): `find apps libs -name "*.ts" -not -path "*/node_modules/*" -not -path "*/dist/*" | wc -l`

### Bước 3 — Chạy **clone-and-go smoke test** (simulate dev mới)
Đây là test quan trọng nhất. Chạy đúng thứ tự, ghi exit code + output cuối mỗi lệnh:

1. **Restore dependencies**:
   ```
   npm ci --prefer-offline   (hoặc pnpm install --frozen-lockfile)
   ```
   → exit 0, không lỗi peer dep, không warning critical.

2. **Build toàn bộ TS**:
   ```
   npx nx run-many --target=build --all --skip-nx-cache
   ```
   → tất cả project pass, không error.

3. **Build toàn bộ .NET**:
   - Tìm tất cả `.sln` ở root + `apps/*/`: `find . -name "*.sln" -not -path "*/node_modules/*"`
   - Mỗi sln chạy `dotnet build <sln> --nologo --configuration Release`
   → 0 warnings (do TreatWarningsAsErrors), 0 errors.

4. **Test toàn bộ**:
   - `npx nx run-many --target=test --all` → ≥ 1 test pass, không có suite fail
   - `dotnet test <each test csproj>` → tất cả pass
   - Ghi: total tests | passed | failed | skipped

5. **Lint toàn bộ**:
   ```
   npx nx run-many --target=lint --all
   ```
   → 0 error (warning OK nếu < 10).

6. **Validate docker-compose**:
   ```
   docker compose -f infra/docker/docker-compose.yml config --quiet
   docker compose -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.observability.yml config --quiet
   ```
   → exit 0 cả 2.

7. **EF Core migration smoke test** (Day 2 DoD strict):
   Cho 1 service (Identity):
   ```
   dotnet ef migrations add SmokeTest -p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api -o Migrations
   ```
   → tạo file migration thành công. Sau đó **xóa migration vừa tạo** để không pollute repo. Nếu fail → DbContext hoặc EF Design package chưa wire.

8. **Internal JWT roundtrip test** (Day 2 DoD strict):
   - Đọc `apps/gateway/src/auth/internal-jwt.signer.ts` + `libs/dotnet/VietRide.Shared.Web/Authentication/InternalJwtAuthenticationHandler.cs`
   - Verify: cùng secret, cùng issuer (`vietride-gateway`), cùng audience (`vietride-internal`), cùng alg HS256
   - Nếu không khớp → roundtrip sẽ fail thực tế.

### Bước 4 — Đọc nội dung file kỹ (không trust filename)
Mở từng file dưới đây và verify **nội dung có thật và đầy đủ**, KHÔNG phải stub `export function x() { return 'x' }`:

#### Root config (clone-and-go phải work)
- [ ] `package.json` — scripts `build`, `test`, `lint`, `dev`, `format` đầy đủ?
- [ ] `pnpm-lock.yaml` hoặc `package-lock.json` được commit?
- [ ] `nx.json` — plugins `@nx/nest`, `@nx-dotnet/core`, `@nx/jest` đăng ký? targetDefaults có cache config?
- [ ] `tsconfig.base.json` — paths cho 6 TS lib + strict + `ignoreDeprecations: "6.0"`?
- [ ] `global.json` — pin .NET SDK 8.0.421?
- [ ] `Directory.Build.props` — `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel`?
- [ ] `Directory.Packages.props` (CPM central package management) — có hay không, nếu có thì version đầy đủ?
- [ ] `.editorconfig` — root, charset utf-8, end_of_line lf, csharp_*?
- [ ] `.prettierrc` + `.prettierignore`?
- [ ] `.eslintrc.json` hoặc `eslint.config.mjs`?
- [ ] `.gitignore` — loại trừ node_modules/bin/obj/dist/.nx/.env/secrets/?
- [ ] `.gitattributes` — line ending normalize?
- [ ] `.dockerignore` — loại trừ node_modules/bin/obj/.git/docs/tests?
- [ ] `.env.example` — đủ biến cho mọi service (Postgres conn × 5 service + Redis + RabbitMQ + JWT secret + VNPay + SendGrid + Firebase)?
- [ ] `README.md` — có hướng dẫn cụ thể: prerequisites + clone + install + `docker compose up` + chạy service + chạy test + troubleshooting?
- [ ] `CONTRIBUTING.md` (optional nhưng nice-to-have)?
- [ ] `LICENSE`?
- [ ] `.vscode/settings.json` + `.vscode/extensions.json` shared, có trong `!.vscode/...` whitelist của .gitignore?

#### Day 1 ticket SCV-8/9 deliverables
Đọc spec ticket trong `BE_TIMELINE_VU.md`, mọi item ticket yêu cầu phải tick được.

#### Day 2 ticket SCV-69/70 deliverables (strict)
- [ ] Per .NET service: `Program.cs` minimal API hay MVC? Wire đầy đủ?
- [ ] Per service: Serilog config qua appsettings (Console sink + structured)?
- [ ] Per service: correlationId middleware (request enricher) trong Serilog?
- [ ] Per service: EF Core DbContext stub kế thừa base, schema riêng?
- [ ] Per service: `appsettings.json` + `appsettings.Development.json` đủ?
- [ ] Per service: Dockerfile multi-stage (.NET sdk 8.0-alpine → aspnet 8.0-alpine), HEALTHCHECK, EXPOSE đúng port?
- [ ] Per service: `launchSettings.json` port 5001-5005 đúng theo timeline?
- [ ] Per service: `Properties/launchSettings.json` ENV `INTERNAL_JWT_SECRET` placeholder?
- [ ] Gateway: nestjs-pino logger thay vì console.log?
- [ ] Gateway: routes.config.ts có ≥18 route?
- [ ] Gateway: User JWT RS256 + JWKS verify thật?
- [ ] Gateway: Internal JWT mint HS256 TTL 120s, claims `{sub, role, operatorId, reqId}`?
- [ ] Gateway: rate-limit per IP 100req/60s?
- [ ] Gateway: health passthrough `/v1/<svc>/health` rewriteTo `/health`?
- [ ] `infra/docker/docker-compose.yml`: Postgres 16 + Redis 7 + RabbitMQ 3.13-management?
- [ ] Healthcheck cho **infra LẪN 9 app container**?

#### Shared libs — verify từng file có code thật
**`libs/dotnet/VietRide.Shared.Kernel/`**:
- [ ] `ValueObjects/Money.cs` — Floor 1000 VND
- [ ] `ValueObjects/PhoneNumber.cs` — E.164 VN regex
- [ ] `Primitives/Result.cs` — Result<T> + Error
- [ ] `Primitives/BaseEntity.cs`
- [ ] `Abstractions/IClock.cs` + `IInternalJwtTokenProvider.cs`
- [ ] `Exceptions/DomainException.cs`
- [ ] Markers `IAuditable`, `ISoftDeletable`

**`libs/dotnet/VietRide.Shared.Application/`**:
- [ ] `IRepository<TEntity, TId>`, `IReadRepository`, `IUnitOfWork`
- [ ] `PagedResult<T>`
- [ ] Exception hierarchy: ValidationException/NotFoundException/ConflictException/ForbiddenException
- [ ] `IApplicationService` marker

**`libs/dotnet/VietRide.Shared.Web/`**:
- [ ] `Authentication/InternalJwtAuthenticationHandler.cs` (HS256 verify từ `X-Internal-Auth`)
- [ ] `Filters/ProblemDetailsExceptionFilter.cs` (RFC 7807 map exception)
- [ ] `Middleware/RequestLoggingMiddleware.cs` (correlationId)
- [ ] `Health/HealthCheckBuilderExtensions.cs` (probe Postgres+Redis+RabbitMQ tag "ready", `/health` liveness + `/ready` runs ready probes)
- [ ] `Swagger/SwaggerExtensions.cs`
- [ ] `Observability/OpenTelemetryServiceCollectionExtensions.cs`
- [ ] `DependencyInjection/SharedWebServiceCollectionExtensions.cs::AddVietRideSharedWeb`

**`libs/dotnet/VietRide.Shared.Persistence/`**:
- [ ] `VietRideDbContextBase.cs` — snake_case naming, SaveChanges auto-audit
- [ ] `Outbox/OutboxMessage.cs` + `IOutboxStore.cs` + `OutboxStore.cs`
- [ ] `DependencyInjection/PersistenceServiceCollectionExtensions.cs::AddVietRideDbContext`

**`libs/dotnet/VietRide.Shared.Messaging/`**:
- [ ] `Abstractions/IIntegrationEvent.cs` + `IEventPublisher.cs`
- [ ] `RabbitMq/RabbitMqEventPublisher.cs`
- [ ] `Outbox/OutboxBackgroundService.cs`
- [ ] `DependencyInjection/MessagingServiceCollectionExtensions.cs::AddVietRideMessaging`

**`libs/dotnet/VietRide.Shared.Http/`**:
- [ ] `Handlers/InternalJwtDelegatingHandler.cs`
- [ ] `Handlers/CorrelationIdDelegatingHandler.cs`
- [ ] `Resilience/HttpResiliencePolicies.cs` (Polly retry + circuit breaker)
- [ ] `DependencyInjection/HttpServiceCollectionExtensions.cs::AddVietRideServiceClient`

**`libs/shared/contracts/src/`**:
- [ ] Events × ≥4 (UserRegistered/BookingCreated/TripCompleted/PaymentSucceeded) với Zod schema
- [ ] DTO chung: ProblemDetails, PageResult
- [ ] `index.ts` export đầy đủ — KHÔNG phải `export function contracts() { return 'contracts' }`

**`libs/shared/nest-common/src/`**:
- [ ] `request-context/correlation-id.middleware.ts`
- [ ] `request-context/request-context.service.ts`
- [ ] `filters/problem-details-exception.filter.ts`
- [ ] `pipes/zod-validation.pipe.ts`
- [ ] `interceptors/logging.interceptor.ts`
- [ ] `nest-common.module.ts`

**`libs/shared/nest-config/`, `nest-redis/`, `nest-rabbitmq/`, `nest-persistence/`** — mỗi lib có code thật, không stub.

#### Tests có thật + pass
- [ ] `tests/dotnet/VietRide.Shared.Kernel.UnitTests/` ≥ 30 test, ALL PASS
- [ ] `tests/dotnet/VietRide.Identity.IntegrationTests/` ≥ 2 test (health + ping), ALL PASS qua WebApplicationFactory
- [ ] Gateway jest spec ≥ 10 test (signer + routes), ALL PASS
- [ ] Tests trong `tests/<svc>-e2e/` (gateway/notification/rag/tracking) còn placeholder "Hello API" hay đã có test thật?

#### Infra YAML
- [ ] `infra/docker/docker-compose.yml` — validate
- [ ] `infra/docker/docker-compose.observability.yml` — validate (Prometheus + Grafana + Loki + Tempo + OTEL collector)
- [ ] `infra/observability/` — 6 file config (otel-collector, prometheus, tempo, loki, grafana datasources, dashboards provider)
- [ ] `infra/postgres/init.sql` — tạo 8 DB + pgvector cho RAG
- [ ] 9 Dockerfile (5 service + Gateway + 3 worker)

#### CI/CD
- [ ] `.github/workflows/ci.yml` — trigger PR + push main/develop, job lint+test+build cho cả TS lẫn .NET, cache đầy đủ
- [ ] `.github/workflows/docker-build.yml` — tag v* trigger GHCR push
- [ ] Pre-commit hook? (husky/lefthook) — optional nhưng add value
- [ ] CODEOWNERS file? — optional

#### Docs & secret hygiene
- [ ] `docs/SECURITY.md` — dev defaults documented + pre-deploy rotation checklist
- [ ] `docs/BACKEND_SOURCE_OF_TRUTH.md` version + changelog gần nhất
- [ ] `docs/BE_TIMELINE_VU.md` Day 1-2 spec không bị stale
- [ ] `docs/adr/0001-monorepo-layout.md`, `0002-gateway-thin-proxy-vs-bff.md`
- [ ] **Không file nào commit chứa secret thật**: grep `password|secret|key` trong commit history; `.env` phải gitignore; `secrets.json` phải gitignore; appsettings.json không chứa real password (dev-only OK nếu doc rõ)
- [ ] `.env` file thật KHÔNG tồn tại trong repo (chỉ `.env.example`)

### Bước 5 — Cross-check với Truth Matrix (Bảng A)
**Đây là bước quan trọng nhất** — không chỉ check "có file" mà phải check "file ĐÚNG theo truth source không":

Với mỗi quy tắc trong Bảng A (Bước 1), mở file implementation tương ứng và verify khớp. Ví dụ:
- "Money floor 1000 VND" → mở `Money.cs`, đọc logic `FromRaw`, verify công thức `rawAmount - (rawAmount % 1000)` đúng.
- "Internal JWT issuer=vietride-gateway audience=vietride-internal HS256 TTL 120s" → mở `internal-jwt.signer.ts` (Gateway) + `InternalJwtAuthenticationHandler.cs` (.NET), verify BOTH dùng cùng giá trị, đối chiếu với truth doc.
- "Route POST /v1/auth/login returns AuthTokenResponse {accessToken, refreshToken, expiresIn}" → mở `routes.ts` Gateway xem route `/v1/auth` có map đến Identity không; mở Identity service xem có endpoint đó không (nếu Day 2 chưa implement endpoint thực thì OK, nhưng route table phải đã có entry).
- "RFC 7807 ProblemDetails error envelope" → check `ProblemDetailsExceptionFilter.cs` shape có khớp contract không.
- "Schema per service snake_case (vietride_identity, vietride_trip, ...)" → check `<Svc>DbContext.cs::OnModelCreating` có set schema không.
- "Mapster (không AutoMapper)" → grep `AutoMapper` trong toàn repo, phải = 0 hit; check Mapster có trong package references (Day 2 có thể chưa add, ghi nhận nhưng không block).
- "MediatR pinned < v12" → check `Directory.Packages.props` hoặc csproj, version phải `< 12.0.0`.

Báo cáo: với mỗi quy tắc, status (✅ KHỚP / ⚠️ KHỚP MỘT PHẦN / ❌ SAI / 🕐 CHƯA TỚI Day 1-2 SCOPE).

### Bước 6 — Đánh giá thiết kế tổng thể
- Clean Architecture có đúng spec không? (Domain không reference Infrastructure?)
- Gateway có đúng ADR 0002 (thin proxy) không?
- Shared libs có đúng "split by layer concern" pattern, không trộn lẫn?
- Có code duplicate giữa lib và service không?
- Có anti-pattern: circular dep, layer leak, service biết FE specific?
- Naming convention có nhất quán (PascalCase .NET, kebab-case TS file)?
- **Drift between truth docs**: có chỗ nào `BACKEND_SOURCE_OF_TRUTH.md` mâu thuẫn với `technical_context_v7.md` hoặc `VietRide_API_Contract_v1.md` không? Nếu có, ghi nhận để fix doc.

## Format báo cáo (BẮT BUỘC, theo đúng thứ tự)

### Phần 1 — Source-of-truth audit
Từ Bước 1, output 3 bảng:
- **Bảng A — Truth matrix** (quy tắc kiến trúc/contract từ tier 0+1+2)
- **Bảng B — Day 1-2 delivery scope** (DoD strict SCV-8/9/69/70)
- **Bảng C — Truth ↔ Delivery cross-check** (quy tắc nào thuộc scope Day 1-2)

Trong phần này phải nêu rõ phiên bản gần nhất của mỗi truth doc + ngày update. Nếu phát hiện **drift giữa các truth doc** (ví dụ technical_context_v7 nói X mà BACKEND_SOURCE_OF_TRUTH nói Y), flag ngay ở đây.

### Phần 2 — Khám phá hiện trạng
- Số project Nx detect được
- Số file .cs, .ts thật
- Tree thư mục depth 3

### Phần 3 — Clone-and-go smoke test result
Bảng:
| Bước | Lệnh | Exit code | Output cuối | PASS/FAIL |
8 lệnh ở Bước 3.

### Phần 4 — Bảng audit nội dung (≥ 60 mục)
| Mục | Status (✅/⚠️/❌) | File path | Bằng chứng (đoạn code/snippet hoặc lý do fail) |

### Phần 5 — Truth matrix compliance (Bước 5 result)
Bảng:
| Quy tắc truth (từ Bảng A) | Nguồn | File implementation | Status (✅/⚠️/❌/🕐) | Bằng chứng |

Tóm tắt: tổng số quy tắc | khớp | sai | partial | chưa-Day-1-2.

### Phần 6 — Gap thực sự còn lại (BLOCKER cho Day 3)
Với mỗi gap:
- Tên gap
- Loại: **truth violation** (sai kiến trúc/contract) HAY **delivery missing** (chưa làm theo timeline)
- File path
- Hiện trạng (snippet)
- Đề xuất fix (snippet code cụ thể)
- Mức độ block Day 3: BLOCKER / NICE-TO-HAVE / DEFER
- Thời gian ước tính fix

### Phần 7 — Đánh giá thiết kế
- Architecture compliance vs truth docs
- Gateway design compliance vs ADR 0002 + BACKEND_SOURCE_OF_TRUTH §3.4
- Shared libs pattern (layer-based) đúng spec?
- Anti-pattern phát hiện (theo §anti-patterns trong truth doc)
- Drift giữa các truth doc (nếu có)
- Recommend cải thiện ngắn hạn

### Phần 8 — Kết luận go/no-go
Đúng 1 trong 3:
- ✅ **READY 100%**: clone-and-go smoke test PASS hết, dev pull về `docker compose up && dotnet ef migrations add InitialCreate -p ... -s ... && dotnet run` → chạy được Day 3 NGAY. KHÔNG có blocker.
- ⚠️ **CƠ BẢN ĐỦ**: smoke test pass nhưng có gap nice-to-have. Liệt kê chính xác từng gap + thời gian ước tính fix.
- ❌ **CHƯA ĐỦ**: smoke test fail ở bước X. Dev pull về sẽ bị block. Liệt kê BLOCKER + thời gian ước tính fix.

## Ràng buộc
- **KHÔNG sửa file** trong quá trình audit, chỉ review + verify + báo cáo.
- **KHÔNG commit**, không tạo branch mới.
- Báo cáo bằng tiếng Việt, code/path/tên class giữ nguyên tiếng Anh.
- Trung thực — nếu phát hiện báo cáo session trước nói "100% done" mà thực tế còn gap, **phải chỉ ra rõ ràng**, không gloss over.
- Nếu Bước 3 smoke test fail bước nào, vẫn tiếp tục các bước còn lại (đừng bỏ giữa chừng) để báo cáo đầy đủ.
- **Hierarchy ưu tiên khi conflict** (cao xuống thấp):
  1. `SU26SE101_VIETRIDE_technical_context_v7.md` + `VietRide_API_Contract_v1.md` (business + API contract gốc)
  2. `BACKEND_SOURCE_OF_TRUTH.md` (backend architecture truth)
  3. ADRs trong `docs/adr/`
  4. Code thực tế trong repo
  5. `BE_TIMELINE_VU.md` (chỉ là schedule, không phải spec)
  6. Prompt này (giả định, có thể sai)
- Nếu prompt này conflict với truth doc → **ưu tiên truth doc**, ghi nhận prompt sai để tôi fix lần sau.
- Nếu code conflict với truth doc → **truth doc thắng**, code là gap cần fix.
- Nếu các truth doc conflict lẫn nhau → ưu tiên theo hierarchy trên + flag drift trong báo cáo.

Bắt đầu audit ngay.
