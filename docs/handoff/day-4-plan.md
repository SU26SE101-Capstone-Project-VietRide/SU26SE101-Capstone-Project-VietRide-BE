# Day 4 — Plan

> Produced by manager. Gated by reviewer (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 4 — Identity Service: Google OAuth + Complete Phone + Admin bootstrap (no Jira key in timeline)
- **Prior checklist**: docs/handoff/day-3-checklist.md (found; no open blocker; Day-3 committed as 7bac9af)
- **Plan status**: APPROVED (PLAN-REVIEW passed)

## Objective
Day 4 adds the three remaining Identity entry paths on top of the Day-3 email/password foundation:
(1) Google OAuth login/auto-link/auto-create, (2) complete-profile phone enforcement so passenger Google
accounts cannot use the app until they supply a phone, and (3) the second-and-subsequent SYSTEM_ADMIN
creation endpoint plus a verified idempotent bootstrap admin startup seeder. It unblocks Day 6 (Operator
self-register, which needs an admin to approve) and gives FE all three auth paths to wire.

## Success criteria (DoD -- binary, verifiable)
- [ ] Google OAuth: valid Google ID token for a brand-new email creates a User (status ACTIVE, phone NULL,
      password_hash NULL) + OAuthIdentity, returns the access+refresh token bundle with hasPhone=false claim.
      (v7 lines 4707-4710)
- [ ] Google OAuth auto-link: Google ID token whose email already exists (email/password account, no
      OAuthIdentity) creates the OAuthIdentity row and logs in the existing account. (v7 line 4709)
- [ ] Google OAuth repeat: Google ID token for an already-linked subject logs in normally. (v7 line 4708)
- [ ] Invalid/forged/expired Google ID token -> 401 AUTH_GOOGLE_TOKEN_INVALID (D2: new registered error code).
- [ ] A passenger with phone IS NULL calling any non-whitelisted endpoint through the Gateway gets
      403 AUTH_PHONE_REQUIRED; whitelisted endpoints (GET /v1/users/me, POST /v1/users/me/complete-profile,
      POST /v1/auth/logout, POST /v1/auth/refresh, health) pass through. (v7 lines 1320-1333)
- [ ] hasPhone claim emitted in all access tokens (all roles); Gateway reads it to enforce the block.
      (D7: Option (a) -- claim name hasPhone, boolean).
- [ ] POST /v1/users/me/complete-profile with a valid unused E.164 phone sets the phone, returns 200
      (NO OTP -- D1), writes a COMPLETE_PROFILE activity log; duplicate phone -> 409 AUTH_PHONE_ALREADY_REGISTERED;
      bad format -> 400 AUTH_PHONE_INVALID_FORMAT; phone already set -> 422 VALIDATION_ERROR. Client refreshes
      token after success to pick up hasPhone=true claim (D7).
      (v7 1336-1354; D1)
- [ ] GET /v1/users/me returns caller profile (id, email, displayName, phone, role, operatorId, status, avatarUrl).
- [ ] POST /v1/admin/users (SYSTEM_ADMIN only) creates a SYSTEM_ADMIN with status PENDING_INITIAL_PASSWORD,
      passwordless (no password_hash). SET_INITIAL_PASSWORD token row + email send deferred to Day 5 per D6.
      Non-admin caller -> 403 FORBIDDEN. (v7 lines 1380-1384; D6)
- [ ] Bootstrap admin: startup seeder (Task 4.0) reads SYSTEM_ADMIN_BOOTSTRAP_* env vars, bcrypt-cost-12,
      creates exactly one SYSTEM_ADMIN (status=ACTIVE); re-running is idempotent (no duplicate).
      (v7 lines 1364-1378; D3)
- [ ] Each new endpoint has >=1 happy-path + >=1 error-case test; dotnet build / dotnet format
      --verify-no-changes / dotnet test green for Identity + Libs; Gateway lint/build/test green; NetArchTest passes.
- [ ] Each new public endpoint has a Gateway route entry and Swashbuckle annotations.

## Contract changes
New REST endpoints (NOT yet in VietRide_API_Contract_v1.md, which stops at JWKS at line 222;
adding them is part of Task 4.7):
- POST /v1/auth/google -- body { idToken } -> token bundle (login envelope shape). (v7 4705-4710; D5)
- POST /v1/users/me/complete-profile -- body { phone } -> { userId, phone, message }. NO OTP (D1).
  (v7 lines 1336-1354)
- GET /v1/users/me -- caller profile. (v7 line 1329)
- POST /v1/admin/users -- body { email, displayName, role: SYSTEM_ADMIN } -> { userId, status }.
  Status = PENDING_INITIAL_PASSWORD; SET_INITIAL_PASSWORD token row + email send are Day 5 scope (D6). (v7 lines 1380-1384)

Error codes: all required codes already exist in BSOT 5.9 EXCEPT AUTH_GOOGLE_TOKEN_INVALID (401),
which is newly registered by Task 4.7 per D2 (BSOT version bump 1.5.1 -> 1.6.0 MINOR).
Existing codes used: AUTH_PHONE_REQUIRED 403, AUTH_PHONE_ALREADY_REGISTERED 409,
AUTH_PHONE_INVALID_FORMAT 400, AUTH_PENDING_INITIAL_PASSWORD 403, FORBIDDEN 403,
VALIDATION_ERROR 422, AUTH_GOOGLE_TOKEN_INVALID 401 (NEW).

DB migration (Task 4.5): a new EF migration adds activity_logs table + activity_log_action enum ONLY
(schema-only, no seed data). Bootstrap admin is a startup seeder (Task 4.0), NOT part of the migration (D3).
oauth_identities, users.phone, email_verification_tokens already exist from Day 3.

Events: No Outbox event in Day 4. identity.user.created is explicitly deferred to Day 10
(D4; day-3-checklist.md:102). Forward-dependency: Day 10 must emit for ALL THREE creation flows --
email register (Day 3), Google OAuth (Task 4.2), admin-created (Task 4.4).

Gateway routes: /v1/auth/google (authRequired none); /v1/users family already routed user;
/v1/admin already routed user + requiredRoles SYSTEM_ADMIN. Gateway currently does NOT enforce
requiredRoles and has no phone-required check -- Task 4.6 adds both (D7: reading hasPhone claim).
`mixed` routes such as /v1/operators must keep their public/auth sub-path behavior; Task 4.6 must not accidentally force every mixed route through the user-only gate.

Timeline correction (Task 4.7): BE_TIMELINE_VU.md:62-63 (wrong: 428 + POST /auth/complete-phone
+ OTP -> correct: 403 + POST /v1/users/me/complete-profile + NO OTP per D1). BE_TIMELINE_VU.md:64
(wrong: "Admin bootstrap migration" -> correct: "Admin bootstrap startup seeder" per D3).
BE_TIMELINE_VU.md:66 (wrong: 428 on protected route -> correct: 403 AUTH_PHONE_REQUIRED).

## Tasks

### Task 4.0 -- Architecture baseline: ActivityLog entity + repository + Google verifier abstraction + bootstrap admin startup seeder (DO FIRST)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | scaffold-aggregate (ActivityLog domain/EF only; seeder is plain Infrastructure) |
| owned files | apps/identity/src/VietRide.Identity.Domain/Entities/ActivityLog.cs ; apps/identity/src/VietRide.Identity.Domain/Enums/ActivityLogAction.cs ; apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IActivityLogRepository.cs ; apps/identity/src/VietRide.Identity.Application/Abstractions/IGoogleIdTokenVerifier.cs (+ a result DTO record file) ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/ActivityLogConfiguration.cs ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/ActivityLogRepository.cs ; apps/identity/src/VietRide.Identity.Infrastructure/IdentityDbContext.cs (add DbSet ActivityLog) ; apps/identity/src/VietRide.Identity.Infrastructure/Seed/BootstrapAdminSeeder.cs (new -- idempotent startup seeder, reads SYSTEM_ADMIN_BOOTSTRAP_* env, bcrypt cost 12, creates SYSTEM_ADMIN if none exists); apps/identity/src/VietRide.Identity.Api/Program.cs (invoke seeder after builder.Build(), before app.Run()) ; apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (register seeder in DI if injected + `AddScoped<IActivityLogRepository, ActivityLogRepository>()`) |
| forbidden scope | .env*, secrets, git ops, non-identity services, Gateway, Day-3 entities/migrations (do NOT edit 20260531103145_InitIdentityAuth* or the snapshot by hand -- happens in 4.5 via dotnet ef), no new NuGet package EXCEPT Google.Apis.Auth is added by Task 4.3; do NOT run dotnet ef migrations add or create ANY new migration/Designer/snapshot file -- the migration is owned exclusively by Task 4.5. 4.0 stops at code only (entity + enum + IEntityTypeConfiguration + DbSet + repo + verifier abstraction + bootstrap seeder). Bootstrap seeder is a PLAIN class (not BackgroundService/HostedService -- invoked synchronously from Program.cs startup for simplicity). Seeder MUST be idempotent (WHERE NOT EXISTS SELECT 1 FROM users WHERE role='\''SYSTEM_ADMIN'\''). |
| depends on | -- |
| invariant flags | CRLF/.cs ; CPM no Version= ; MediatR v11 ; BCrypt cost 12 ; no cross-DB FK (activity_logs FK to users same-DB OK) ; one class per file ; Clean Architecture direction (Domain->nothing, abstractions in Application, seeder in Infrastructure) |
| acceptance | identity sln build green; dotnet format --verify-no-changes clean; NetArchTest passes; ActivityLog + ActivityLogAction match db-schema/identity-user/schema.sql:51-66,284-297 (PK uuid, user_id, action enum, metadata jsonb, ip_address, user_agent, created_at; NO updated_at/soft-delete -- append-only); IdentityDbContext registers ActivityLogAction in both ConfigurePostgresEnums (`builder.MapEnum<ActivityLogAction>("activity_log_action", PostgresEnumNameTranslator)`) and RegisterPostgresEnums (`modelBuilder.HasPostgresEnum("activity_log_action", Enum.GetNames<ActivityLogAction>())`) and exposes `DbSet<ActivityLog>`; `InfrastructureServiceCollectionExtensions.AddInfrastructure` registers `AddScoped<IActivityLogRepository, ActivityLogRepository>()`; IGoogleIdTokenVerifier returns subject/email/displayName/avatarUrl, no impl yet; BootstrapAdminSeeder reads SYSTEM_ADMIN_BOOTSTRAP_EMAIL, SYSTEM_ADMIN_BOOTSTRAP_PASSWORD, SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME env vars (D3 naming), bcrypts password at cost 12, creates SYSTEM_ADMIN ACTIVE if none exists, idempotent on re-run; BootstrapAdminSeeder behavior is explicit: if a SYSTEM_ADMIN already exists, skip without requiring bootstrap env vars; if no SYSTEM_ADMIN exists and SYSTEM_ADMIN_BOOTSTRAP_EMAIL or SYSTEM_ADMIN_BOOTSTRAP_PASSWORD is missing/blank, fail fast with a configuration error before app serves traffic; display name defaults to "System Administrator" when missing/blank; Program.cs invokes seeder at startup after app is built |
| source citations | db-schema/identity-user/schema.sql:51-66 (activity_log_action enum), :284-297 (activity_logs); v7 line 4707 (Google callback fields); v7:1362-1385 (bootstrap admin, D3); Program.cs:40-49 (startup pattern); BSOT 5.10 (repo pattern), 3.5 (naming) |
| parallel-safe | no (blocks 4.1-4.5) |


### Task 4.1 -- Domain: User factory for Google accounts + CompleteProfile + admin-created user
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files | apps/identity/src/VietRide.Identity.Domain/Entities/User.cs ; apps/identity/src/VietRide.Identity.Domain/Entities/OAuthIdentity.cs (only if a helper is needed) ; apps/identity/tests/VietRide.Identity.UnitTests/Domain/UserTests.cs (existing User test file) |
| forbidden scope | .env*, secrets, git ops, Infrastructure/Api/Application handlers, Gateway, other services, EF migrations |
| depends on | 4.0 |
| invariant flags | CRLF/.cs ; domain has no infra deps ; phone normalization stays in Application (factory takes already-normalized PhoneNumber) ; status machine guarded ; PENDING_INITIAL_PASSWORD already exists (UserStatus.cs:6 -- D6: do NOT add it) |
| acceptance | User.CreateGoogleAccount(email, displayName, avatarUrl) -> Role=PASSENGER, Status=ACTIVE, Phone=null, PasswordHash=null (v7 line 4710); User.CompleteProfile(PhoneNumber) sets phone only when currently null else domain guard mapping to VALIDATION_ERROR (v7 1344,1353; D1: NO OTP); User.CreateAdminPendingPassword(email, displayName) -> Role=SYSTEM_ADMIN, Status=PENDING_INITIAL_PASSWORD, no password (passwordHash null), OperatorId null (v7 1381-1384; D6: passwordless, full flow Day 5); unit tests each path incl. CompleteProfile-already-set guard; dotnet test green |
| source citations | v7 lines 4707-4710, 1336-1354, 1380-1384; schema.sql:140-184 (users + chk_users_operator_role: SYSTEM_ADMIN operator_id NULL); User.cs:44-65 (CreatePassenger pattern); UserStatus.cs:6 (PENDING_INITIAL_PASSWORD already exists); D1, D6 |
| parallel-safe | no |

### Task 4.2 -- Application: Google OAuth login command + hasPhone claim in RsaAccessTokenService
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint (application slice only) |
| owned files | apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin/ (Command, Handler, Validator; reuse existing Login/LoginDto.cs TokenBundleDto -- do NOT duplicate) ; apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IOAuthIdentityRepository.cs ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OAuthIdentityRepository.cs ; apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (register `AddScoped<IOAuthIdentityRepository, OAuthIdentityRepository>()`) ; apps/identity/src/VietRide.Identity.Infrastructure/Security/RsaAccessTokenService.cs (add hasPhone claim -- D7: true when user.Phone != null, emitted for ALL roles) ; apps/identity/tests/VietRide.Identity.UnitTests/Application/GoogleLoginCommandHandlerTests.cs ; apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/RsaAccessTokenServiceTests.cs (or existing token-service test file) |
| forbidden scope | .env*, secrets, git ops, Google verifier impl (Task 4.3), Gateway, other services, Day-3 Login/Refresh handlers, EF migrations |
| depends on | 4.0, 4.1 |
| invariant flags | CRLF/.cs ; MediatR v11 (IRequestHandler) ; TransactionBehavior wraps create+link in one tx ; constant token TTL reuse (900s) ; one class per file ; hasPhone claim name IS hasPhone (lowercase); value must be interoperable with JWT libraries by asserting the encoded access token decodes to either boolean true/false or string "true"/"false", and Gateway must handle both defensively |
| acceptance | [D7-resolved] RsaAccessTokenService.IssueToken emits hasPhone claim (true when user.Phone != null, all roles); token-service unit test issues a real access token via RsaAccessTokenService and decodes it with JwtSecurityTokenHandler to assert claim name/value for phone-present and phone-null users. `InfrastructureServiceCollectionExtensions.AddInfrastructure` registers `AddScoped<IOAuthIdentityRepository, OAuthIdentityRepository>()`. Handler resolves IGoogleIdTokenVerifier then: existing OAuthIdentity -> login (with hasPhone based on existing user phone); email exists no link -> create OAuthIdentity + login; new email -> CreateGoogleAccount + OAuthIdentity + login (v7 4708-4710, hasPhone=false for new Google user since phone IS NULL); issues access+refresh via existing IAccessTokenService/IRefreshTokenFactory like LoginCommandHandler.cs:86-94; invalid/expired/wrong-aud Google token -> throw UnauthorizedException with code AUTH_GOOGLE_TOKEN_INVALID (D2: 401). NSubstitute unit tests cover 3 branches + invalid token; dotnet test green |
| source citations | v7 lines 4705-4710; LoginCommandHandler.cs:43-106; oauth_identities uq indexes schema.sql:200-204; RsaAccessTokenService.cs:52-61 (existing claims, D7); InfrastructureServiceCollectionExtensions.cs:47-53 (repository DI pattern); BSOT 5.10; D2, D5, D7 |
| parallel-safe | yes vs 4.4; NO vs 4.3 because both edit InfrastructureServiceCollectionExtensions.cs (run 4.2/4.3 serial or merge DI changes in one pass) |
### Task 4.3 -- Infrastructure: Google ID-token verifier using Google.Apis.Auth
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files | apps/identity/src/VietRide.Identity.Infrastructure/Security/GoogleIdTokenVerifier.cs ; apps/identity/src/VietRide.Identity.Infrastructure/Security/GoogleOAuthOptions.cs ; apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (register verifier + options binding) ; Directory.Packages.props (add `<PackageVersion Include="Google.Apis.Auth" Version="1.74.0" />` -- CPM: no Version= on csproj PackageReference per D5) ; apps/identity/src/VietRide.Identity.Infrastructure/VietRide.Identity.Infrastructure.csproj (add `<PackageReference Include="Google.Apis.Auth" />` WITHOUT Version= attribute) ; apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/GoogleIdTokenVerifierTests.cs |
| forbidden scope | .env* (reference config keys, no real secrets), git ops, Gateway, other services, Application/Domain layers, EF migrations, adding any NuGet package other than Google.Apis.Auth |
| depends on | 4.0 |
| invariant flags | CRLF/.cs ; CPM no Version= (Google.Apis.Auth Version goes ONLY in Directory.Packages.props); options pattern ; DI in Infrastructure only; Google.Apis.Auth uses GoogleJsonWebSignature.ValidateAsync -- no manual JWKS/fetch (D5 APPROVED) |
| acceptance | [D5-resolved] IGoogleIdTokenVerifier implemented with Google.Apis.Auth (GoogleJsonWebSignature.ValidateAsync). Validates Google ID token against Google servers (audience = GOOGLE_OAUTH_CLIENT_ID). Returns subject(sub)/email/displayName(name)/avatarUrl(picture). Throws typed failure on invalid/expired/wrong-aud token that 4.2 maps to 401 AUTH_GOOGLE_TOKEN_INVALID. GOOGLE_OAUTH_CLIENT_ID read from config (IOptions<GoogleOAuthOptions>). Directory.Packages.props has pinned `<PackageVersion Include="Google.Apis.Auth" Version="1.74.0" />`; csproj reference has no Version= attribute. Build + format + tests green. |
| source citations | v7 line 4707 (callback fields); BSOT 6 (RS256/JWKS verification conventions); NuGet latest stable decision: Google.Apis.Auth 1.74.0; D5 |
| parallel-safe | yes vs 4.4; NO vs 4.2 because both edit InfrastructureServiceCollectionExtensions.cs (run 4.2/4.3 serial or merge DI changes in one pass) |

### Task 4.4 -- Application + API: complete-profile + GET users/me + admin create-user
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | apps/identity/src/VietRide.Identity.Application/Features/Users/CompleteProfile/ ; apps/identity/src/VietRide.Identity.Application/Features/Users/GetMe/ ; apps/identity/src/VietRide.Identity.Application/Features/Admin/CreateAdminUser/ ; apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IUserRepository.cs (only add narrowly-needed query/update helpers; preserve existing signatures) ; apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IActivityLogRepository.cs (only if the generic IRepository contract is insufficient for the COMPLETE_PROFILE write) ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/UserRepository.cs (implement any IUserRepository additions) ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/ActivityLogRepository.cs (implement any IActivityLogRepository additions) ; apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (only repository/optional application-service DI needed by this task; do not change Google/JWT/bootstrap registrations) ; apps/identity/src/VietRide.Identity.Application/Abstractions/Services/ and apps/identity/src/VietRide.Identity.Application/Services/ (ONLY if the worker introduces a reusable User/Profile application service for shared logic across these three use cases; otherwise do not create service files) ; apps/identity/src/VietRide.Identity.Api/Program.cs (ONLY if such optional application service needs DI registration; no auth/bootstrap/config changes) ; apps/identity/src/VietRide.Identity.Api/Controllers/UsersController.cs ; apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs ; apps/identity/src/VietRide.Identity.Api/Controllers/Requests/CompleteProfileRequest.cs ; apps/identity/src/VietRide.Identity.Api/Controllers/Requests/CreateAdminUserRequest.cs ; apps/identity/src/VietRide.Identity.Api/Controllers/*Claims*.cs or *CurrentUser*.cs (new helper only if needed to parse JWT sub/role safely at the API boundary) ; apps/identity/tests/VietRide.Identity.IntegrationTests/Api/UsersEndpointsTests.cs ; apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AdminUsersEndpointsTests.cs ; apps/identity/tests/VietRide.Identity.UnitTests/Application/Users/ ; apps/identity/tests/VietRide.Identity.UnitTests/Application/Admin/ |
| forbidden scope | .env*, secrets, git ops, AuthController (Google endpoint is 4.7), Gateway, other services, EF migrations, the SET_INITIAL_PASSWORD consume endpoint/token generation/email send (Day 5 -- D6), RsaAccessTokenService (hasPhone claim ownership is 4.2 per D7), Domain entity/enum edits from Task 4.1 (User.cs/UserStatus.cs/UserRole.cs are already complete; if a domain/status gap is discovered, STOP and report instead of editing), Google OAuth handler/verifier (4.2/4.3), bootstrap seeder behavior (4.0), BSOT/API-contract/timeline docs (4.7) |
| depends on | 4.0, 4.1; file-conflict note: if this task touches InfrastructureServiceCollectionExtensions.cs or Program.cs, run it after 4.2/4.3 in a single working tree to avoid DI merge conflicts (already satisfied in the current progress tracker) |
| invariant flags | CRLF/.cs ; MediatR v11 ; ApiResponse envelope (ADR 0004) auto-wrap via existing filters ; [Authorize] on both controllers; AdminUsers requires SYSTEM_ADMIN role check at the Identity boundary and handler/request level where practical (defense-in-depth even though Gateway also gates); controllers stay thin and call MediatR.Send only; Application must not reference Infrastructure or DbContext directly; repository/service additions must stay narrow and use existing IRepository/UnitOfWork patterns; one class per file |
| acceptance | [D7-resolved] complete-profile: caller id comes from authenticated JWT `sub` (X-Internal-Auth claims forwarded by Gateway); normalize+validate E.164 (BSOT/schema regex chk_users_phone_format); dup phone -> 409 AUTH_PHONE_ALREADY_REGISTERED; bad format -> 400 AUTH_PHONE_INVALID_FORMAT; already-set -> 422 VALIDATION_ERROR; success updates the existing User through the repository, writes ActivityLog COMPLETE_PROFILE in the same MediatR transaction, and returns { userId, phone, message } (v7 1341-1353); NO OTP (D1). Response body does NOT include a new token bundle (D7: client refreshes separately via POST /v1/auth/refresh to pick up hasPhone=true claim). GET /v1/users/me returns caller profile (id, email, displayName, phone, role, operatorId, status, avatarUrl) from the authenticated caller id. [D6-resolved] POST /v1/admin/users requires caller role SYSTEM_ADMIN, creates SYSTEM_ADMIN with status PENDING_INITIAL_PASSWORD (reusing existing enum UserStatus.cs:6 and User.CreateAdminPendingPassword), passwordless (no password_hash set). SET_INITIAL_PASSWORD EmailVerificationToken row + email send deferred to Day 5 per D6 and v7 §5.1.1 updated scope split. Non-SYSTEM_ADMIN caller -> 403 FORBIDDEN. Any repository/service/helper additions are only those needed to compile these three use cases and their tests; no new dependency; no direct SaveChanges in handlers outside existing UnitOfWork/TransactionBehavior pattern. Swashbuckle annotations; happy+error integration tests for each new endpoint plus focused handler/unit tests where integration stubs would not exercise business logic; dotnet build + dotnet format --verify-no-changes + dotnet test green for Identity. |
| source citations | v7 lines 1320-1333 (Gateway block + whitelist), 1335-1354 (complete-profile endpoint/validation/response/audit), 1380-1383 (subsequent admin endpoint), 1387-1391 and 1448-1463 (token/caller claims flow); BSOT 3.2 lines 371-382 (Clean Architecture/CQRS/repository/service rules), 5.5 lines 1212-1238 (ADR 0004 error envelope), 5.9 lines 1314-1330 (AUTH_PHONE_ALREADY_REGISTERED, AUTH_PHONE_REQUIRED, AUTH_PHONE_INVALID_FORMAT, AUTH_PENDING_INITIAL_PASSWORD), 5.6 lines 1240-1258 (Day-4 mutations are not in Idempotency-Key required list); schema.sql:51-66 (activity_log_action includes COMPLETE_PROFILE), :140-184 (users columns/constraints, phone unique/format, SYSTEM_ADMIN operator_id NULL), :241-260 (email_verification_tokens Day-5 deferred token table), :284-297 (activity_logs); User.cs:96-115 (CreateAdminPendingPassword), :140-152 (CompleteProfile); UserStatus.cs:6 (PENDING_INITIAL_PASSWORD exists); IUserRepository.cs:9-22 and UserRepository.cs:18-52 (current repo contract/impl); IActivityLogRepository.cs:6-8 and ActivityLogRepository.cs:19-35 (current activity-log contract/impl); ActivityLog.cs:50-56 (ActivityLog.Create); InternalJwtAuthenticationHandler.cs:45-58 (X-Internal-Auth bearer source); VietRide_API_Contract_v1.md:16-222 (current Identity contract shape to mirror; Day-4 endpoint docs owned by 4.7); D1, D6, D7 |
| parallel-safe | no vs 4.2/4.3 because this patch may touch InfrastructureServiceCollectionExtensions.cs/Program.cs DI seams; yes with remaining 4.5/4.6/4.7/4.8 at file level after 4.2/4.3 are done, except normal single-tree sequencing still applies |

### Task 4.5 -- Infrastructure: EF migration for activity_logs table + activity_log_action enum (SCHEMA ONLY)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files | apps/identity/src/VietRide.Identity.Infrastructure/Migrations/<timestamp>_AddActivityLogs.cs (+ .Designer.cs) ; apps/identity/src/VietRide.Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs (regenerated by dotnet ef) ; apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/ActivityLogConfiguration.cs (may need CreatePgsqlEnum call matching migration) |
| forbidden scope | .env*, secrets, git ops, editing the Day-3 migration 20260531103145_InitIdentityAuth*, hand-editing the snapshot (let dotnet ef regenerate), Gateway, other services, Domain/Application/Api code, ANY bootstrap admin seed data -- Task 4.5 is SCHEMA ONLY (D3) |
| depends on | 4.0 (needs ActivityLog entity + configuration), 4.4 (final entity shape) |
| invariant flags | CRLF/.cs ; migration MUST have a working Down() (reversible); activity_logs matches schema (PK uuid, user_id FK to users ON DELETE RESTRICT, action activity_log_action enum, metadata jsonb, ip_address, user_agent, created_at, NO updated_at trigger per append-only design); migration creates activity_log_action enum if not already present (mirror Day-3 migrationBuilder.Sql enum style); [D3-resolved] NO bootstrap admin seed data in this migration -- that is a startup seeder owned by Task 4.0 |
| acceptance | [D3-resolved] dotnet ef migrations add then apply on fresh empty DB creates activity_logs table + activity_log_action enum (mirror Day-3 migrationBuilder.Sql enum style). Migration is schema-only -- no INSERT statements, no seed data. Down() drops activity_logs table cleanly and drops the enum if this migration created it (safe since no other table references activity_log_action yet). dotnet build + format green. |
| source citations | schema.sql:284-297 (activity_logs), :51-66 (enum); existing 20260531103145_InitIdentityAuth.cs enum-creation style; D3 |
| parallel-safe | no (touches snapshot) |

### Task 4.6 -- Gateway: phone-required enforcement (hasPhone claim) + RBAC (requiredRoles) + google route
| Field | Value |
|---|---|
| stack/owner | nest |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files | apps/gateway/src/config/routes.ts (add /v1/auth/google authRequired none route; confirm /v1/users/me/complete-profile reachable via /v1/users prefix) ; apps/gateway/src/proxy/proxy.middleware.ts (enforce requiredRoles -> 403 FORBIDDEN; add phone-required gate reading `hasPhone` claim per D7) ; apps/gateway/src/auth/* (only if a claim helper is needed) ; apps/gateway/src/proxy/proxy.middleware.spec.ts ; a new spec for the phone/RBAC gate |
| forbidden scope | .env*, secrets, git ops, any .NET code, other NestJS apps (tracking/notification/rag), Identity service internals |
| depends on | 4.2 (RsaAccessTokenService must emit hasPhone claim for 4.6 to consume) ; Note: 4.6 is parallel-safe with all .NET tasks at file level, but functionally depends on 4.2 producing the claim shape |
| invariant flags | LF/.ts ; ADR 0004 envelope for Gateway-generated errors ; use registered codes only: FORBIDDEN (403) for RBAC, AUTH_PHONE_REQUIRED (403) for missing phone ; no new dep |
| acceptance | [D7-resolved] A request to a requiredRoles SYSTEM_ADMIN route by a non-admin JWT -> 403 FORBIDDEN enveloped (currently NOT enforced -- proxy.middleware.ts ignores route.requiredRoles). A passenger JWT with hasPhone=false, hasPhone="false", or hasPhone claim absent for PASSENGER -> 403 AUTH_PHONE_REQUIRED enveloped on non-whitelisted paths. hasPhone=true or hasPhone="true" passes the phone gate. Claim name = `hasPhone` (matching RsaAccessTokenService output from Task 4.2 -- D7). Gateway specs include at least one token payload decoded/verified through jose with `hasPhone` values boolean false and string "false" to lock parse behavior. Whitelist (GET /v1/users/me, POST /v1/users/me/complete-profile, /v1/auth/logout, /v1/auth/refresh, /health, /ready) passes. /v1/auth/google routes to Identity authRequired none. Existing `authRequired: mixed` routes such as /v1/operators keep their mixed public/auth behavior and are not forced into user-only auth by the new RBAC/phone gates. Gateway lint+build+test green. No per-request Identity call -- phone check is purely claim-based (D7). |
| source citations | v7 lines 1320-1333 (Gateway block + whitelist + 403 AUTH_PHONE_REQUIRED); BSOT 5.9 (AUTH_PHONE_REQUIRED:1326, FORBIDDEN:1401); routes.ts:36-41 (admin route already declares requiredRoles); proxy.middleware.ts:118-191 (no RBAC/phone check today); RsaAccessTokenService.cs:52-61 (claim emission point, D7); D7 |
| parallel-safe | yes (disjoint from all .NET tasks) |

### Task 4.7 -- API + contract + BSOT registry: wire POST /v1/auth/google + update API contract doc + register AUTH_GOOGLE_TOKEN_INVALID + fix timeline
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | apps/identity/src/VietRide.Identity.Api/Controllers/AuthController.cs (add google action only) ; apps/identity/src/VietRide.Identity.Api/Controllers/Requests/GoogleLoginRequest.cs ; apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AuthEndpointsTests.cs (add google case) ; VietRide_API_Contract_v1.md (add the 4 new Identity endpoints after line 222 -- /v1/auth/google, /v1/users/me/complete-profile, GET /v1/users/me, POST /v1/admin/users) ; BACKEND_SOURCE_OF_TRUTH.md (add AUTH_GOOGLE_TOKEN_INVALID 401 to section 5.9 Auth group; set version header line 3 to 1.6.0 from the current stale 1.5.0; add section 13 changelog entry 1.6.0 above existing 1.5.1 with MINOR bump for new error code per BSOT:1312; keep the existing 1.5.1 row) ; BE_TIMELINE_VU.md (fix lines 62-63,64,66 -- see acceptance below for exact corrections) |
| forbidden scope | .env*, secrets, git ops, Google verifier impl (4.3) + handler (4.2), Gateway, other services, EF migrations, rewriting Day-3 endpoints |
| depends on | 4.2, 4.3 |
| invariant flags | CRLF/.cs for code ; LF/.md for the contract/BSOT/timeline docs ; ApiResponse envelope ; [AllowAnonymous] on google action ; Swashbuckle annotations ; one class per file ; BSOT version bump per section 0.4 rules |
| acceptance | POST /v1/auth/google action sends GoogleLoginCommand, returns 200 token bundle envelope; integration test covers happy (mocked verifier) + invalid-token (401 AUTH_GOOGLE_TOKEN_INVALID per D2). VietRide_API_Contract_v1.md gains 4 new endpoint sections with request/response + error envelopes consistent with Day-3 entries (append after the JWKS section at line 222); each section must explicitly state auth mode and `Idempotency-Key: not required by BSOT §5.6` for the new Day-4 mutations. [D2-resolved] BSOT section 5.9 Auth group: add AUTH_GOOGLE_TOKEN_INVALID (HTTP 401, "Google ID token signature/expiry/audience invalid"). BSOT version header line 3: set to 1.6.0 from the current stale 1.5.0; BSOT section 13 changelog: add 1.6.0 MINOR entry above the existing 1.5.1 row and keep 1.5.1. [D1/D3-resolved] BE_TIMELINE_VU.md corrections: line 62 change "428" to "403", drop "middleware" wording -> "Gateway enforcement"; line 63 change "POST /auth/complete-phone" to "POST /v1/users/me/complete-profile", remove "re-verify via OTP" -> "no OTP (D1)"; line 64 change "Admin bootstrap migration" to "Admin bootstrap startup seeder" (D3); line 66 change "428" to "403". dotnet build + format + test green. |
| source citations | VietRide_API_Contract_v1.md Identity section lines 16-222 (shape to mirror, ends at JWKS line 222); v7 endpoint specs cited in 4.2/4.4; AuthController.cs action style; BSOT:1240-1258 (Idempotency-Key list), :1312 (registry rule), :1318, :1326, :2666-2669 (section 13); BE_TIMELINE_VU.md:62-66; D1, D2, D3 |
| parallel-safe | no (depends on 4.2/4.3) |

### Task 4.8 -- Cross-cutting: env + docker config for Google OAuth + admin bootstrap
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer (or /code-review) |
| skill | (none) |
| owned files | .env.example (rename BOOTSTRAP_ADMIN_* to SYSTEM_ADMIN_BOOTSTRAP_* per D3; add GOOGLE_OAUTH_CLIENT_ID / GOOGLE_OAUTH_CLIENT_SECRET / SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME) ; infra/docker/docker-compose.yml (pass the new env vars to the identity service container) ; apps/identity/src/VietRide.Identity.Api/appsettings.json (config key binding placeholders, NO secrets) ; db-schema/identity-user/seed.sql (remove/keep-removed bootstrap SYSTEM_ADMIN placeholder insert; default SubscriptionPlan seed remains) ; NOTE: Directory.Packages.props NOT in scope for 4.8 — Google.Apis.Auth PackageVersion already registered by Task 4.3 |
| forbidden scope | real secret values (placeholders only), .env (only .env.example), git ops, application/domain logic, other services compose entries beyond identity, Gateway proxy logic; do NOT touch apps/identity/src/VietRide.Identity.Api/appsettings.Development.json -- config keys go in the base appsettings.json only; do NOT remove default SubscriptionPlan seed from db-schema/identity-user/seed.sql |
| depends on | -- (D3 and D5 resolved; env var names per D3) |
| invariant flags | LF for .ts/.yml/.md and .json (per .gitattributes .json = LF) ; CPM no Version= on csproj ; no banned dep ; placeholders only, never commit a real client secret ; Google.Apis.Auth version goes ONLY in Directory.Packages.props |
| acceptance | [D3-resolved] .env.example: rename BOOTSTRAP_ADMIN_EMAIL -> SYSTEM_ADMIN_BOOTSTRAP_EMAIL, BOOTSTRAP_ADMIN_PASSWORD -> SYSTEM_ADMIN_BOOTSTRAP_PASSWORD, add SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME (default "System Administrator"). [D5-resolved] Add GOOGLE_OAUTH_CLIENT_ID / GOOGLE_OAUTH_CLIENT_SECRET placeholders. Docker-compose identity service receives all new env vars. Appsettings.json has the binding sections the Infrastructure options classes (4.0 seeder, 4.3 verifier) read. db-schema/identity-user/seed.sql does NOT insert a bootstrap SYSTEM_ADMIN or placeholder password_hash; it keeps only system seed data that does not conflict with the startup seeder (default SubscriptionPlan stays). No secret committed. Directory.Packages.props remains owned exclusively by Task 4.3 and MUST NOT be edited by Task 4.8. |
| source citations | BACKEND_SOURCE_OF_TRUTH.md:2334-2337 (bootstrap + Google OAuth config env keys); v7:1362-1385 (bootstrap env, D3 naming); db-schema/identity-user/README.md:35,90 (startup seeder; no bootstrap admin in seed/migration); db-schema/identity-user/seed.sql (must not insert SYSTEM_ADMIN); .env.example:70-92 (current bootstrap vars to rename), :73-75 (old BOOTSTRAP_ADMIN_* lines); .gitattributes EOL policy; D3, D5 |
| parallel-safe | yes |

## Dispatch order
1. Task 4.0 (baseline -- blocks 4.1/4.2/4.3/4.4/4.5). Serial first. Includes bootstrap seeder (D3).
2. Task 4.1 (domain) -- after 4.0.
3. Tasks 4.2 + 4.3 -- after 4.0/4.1. They are not safe to batch in parallel because both touch `InfrastructureServiceCollectionExtensions.cs` for DI registration. Run them serially or merge the DI edits in one coordinated pass; 4.2 still compiles against the 4.0 abstraction, not the 4.3 impl.
4. Task 4.4 (complete-profile / me / admin-create) -- after 4.0/4.1, and after 4.2/4.3 if it needs DI seam edits in InfrastructureServiceCollectionExtensions.cs/Program.cs. In current progress, 4.2/4.3 are already done, so resume 4.4 after this patch is approved. Parallel-safe with remaining tasks at file level.
5. Task 4.5 (migration -- schema only, no seed per D3) -- after 4.0/4.1/4.4 (needs final entity + config shape). Serial (touches snapshot).
6. Task 4.7 (google endpoint + contract + BSOT registry + timeline fix) -- after 4.2/4.3.
7. Task 4.6 (Gateway -- hasPhone claim consumer, RBAC) -- parallel-safe with all .NET tasks at file level; land after 4.2 (needs claim producer side to define shape). Do not batch 4.2 and 4.3 together unless one worker owns the shared DI-file merge.
8. Task 4.8 (env/docker) -- parallel-safe; land after D3/D5 resolved.

Default execution in one tree is serial per /implement-task; parallel-safe flags identify batchable sections only.

## Progress tracker
> Orchestrator bookkeeping -- updated after each /implement-task. Informational only -- NOT audit evidence.
> /audit-day re-verifies every task independently; a row here is bookkeeping, not a passed audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 4.0 | ✅ done | APPROVE | 2026-06-04 | Approved after human-authorized extra patch; human verify pending. |
| 4.1 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.2 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.3 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.4 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.5 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.6 | ✅ done | APPROVE | 2026-06-04 | Approved on first review; human verify pending. |
| 4.7 | ✅ done | APPROVE | 2026-06-04 | Approved after one patch round; human verify pending. |
| 4.8 | todo | -- | -- | -- |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions
**None open.** (Q1-Q6 below resolved by human decisions D1-D7 at the gate — every resolution is binding on the task acceptance criteria.)

### Day-4 questions — all resolved at gate (human decisions D1-D7)

- ~~Q1 — Phone-required enforcement source~~ (BLOCKS 4.6; affects 4.2/4.4/4.7).
  v7 5.1 says Gateway blocks phone IS NULL + role=PASSENGER with 403 AUTH_PHONE_REQUIRED, but the
  RS256 access token does NOT carry phone/profile-complete info (RsaAccessTokenService.cs:52-61 emits
  only sub/role/email/operatorId). Options: (a) add a claim to the access token → Gateway reads it;
  (b) enforce inside Identity via ASP.NET action filter.
  → **RESOLVED by D7: Option (a)** — add `hasPhone` claim (boolean) to access token at
  RsaAccessTokenService.cs (Task 4.2). Gateway reads it (Task 4.6). complete-profile does NOT change
  response body; client calls POST /v1/auth/refresh after success to pick up hasPhone=true.
  No per-request Identity call. Source: RsaAccessTokenService.cs:52-61; technical_context_v7:1320-1334.

- ~~Q2 — Google-OAuth-failure error code~~ (BLOCKS 4.2/4.3/4.7).
  BSOT 5.9 has no Google-specific code. Options: (a) reuse AUTH_TOKEN_INVALID (401); (b) register new
  AUTH_OAUTH_TOKEN_INVALID (401).
  → **RESOLVED by D2: Register new code** — AUTH_GOOGLE_TOKEN_INVALID, HTTP 401, added to BSOT
  §5.9 Auth group + §13 changelog (MINOR bump 1.5.0 → 1.6.0). Wired into Task 4.2 acceptance,
  documented by Task 4.7. Source: BSOT:1318 (convention), :1312 (changelog rule).

- ~~Q3 — Google verification: manual JWKS vs Google.Apis.Auth~~ (affects 4.3).
  Plan defaulted to manual JWKS validation against Google certs using already-present libs (no new dep).
  Google.Apis.Auth requires a NEW NuGet dependency — AGENTS.md requires explicit approval.
  → **RESOLVED by D5: APPROVED Google.Apis.Auth** — use GoogleJsonWebSignature.ValidateAsync.
  MUST be added to Directory.Packages.props as <PackageVersion> (CPM — no Version= on
  PackageReference). Manual JWKS validation is DROPPED. Source: AGENTS.md hard invariants.

- ~~Q4 — SET_INITIAL_PASSWORD consume endpoint = Day 5?~~ (affects 4.4).
  Timeline puts POST /auth/set-initial-password on Day 5. v7 5.1.1 previously said POST /v1/admin/users issues
  the token; plan review decision D6 updates v7 to split Day 4 vs Day 5 scope.
  → **RESOLVED by D6: Confirmed Day 5** — admin-created user gets status PENDING_INITIAL_PASSWORD,
  passwordless (no password_hash). Full SET_INITIAL_PASSWORD flow (EmailVerificationToken row +
  email send) stays Day 5. PENDING_INITIAL_PASSWORD ALREADY EXISTS in UserStatus.cs:6 — Task 4.4
  reuses it, does NOT add it. Source: technical_context_v7:1380-1384; UserStatus.cs:6; BSOT:1330.

- ~~Q5 — Bootstrap admin env var names + seed vs runtime~~ (BLOCKS 4.0/4.5/4.8).
  .env.example uses BOOTSTRAP_ADMIN_*; canonical naming is SYSTEM_ADMIN_BOOTSTRAP_*. EF seed migration
  cannot reliably read runtime env/secrets at apply time — bcrypt hashing needs runtime config.
  → **RESOLVED by D3: Startup seeder** — idempotent startup seeder (Task 4.0, invoked from
  Program.cs, reads SYSTEM_ADMIN_BOOTSTRAP_* env + bcrypt-cost-12 at runtime). NOT an EF seed
  migration. Task 4.5 is REPURPOSED to schema-only EF migration (activity_logs table + enum,
  no seed data). Env var naming: SYSTEM_ADMIN_BOOTSTRAP_* (v7), NOT BOOTSTRAP_ADMIN_*.
  Source: technical_context_v7:1362-1385; Program.cs:40-49.

- ~~Q6 — GET /v1/users/me in scope for Day 4?~~
  Not yet in API contract; v7 line 1329 lists it on the phone-required whitelist.
  → **RESOLVED (downgraded to stated decision during PLAN-REVIEW): YES, in scope.**
  v7 line 1329 lists it on the phone-required whitelist → MUST exist for Day-4 DoD.
  Owned by Task 4.4 (slice + controller) and whitelisted by Task 4.6.

### Additional gate decisions (not from plan Q's — raised by human at gate)

- ~~D1 — Contract gap + 428-vs-403 conflict~~ (was plan CRITICAL #1, #2).
  VietRide_API_Contract_v1.md has NO section for the Day-4 endpoints. Timeline says 428 +
  POST /auth/complete-phone + OTP; v7 says 403 + POST /v1/users/me/complete-profile + NO OTP.
  → Follow technical_context_v7. BE_TIMELINE_VU.md:62-63,64,66 is WRONG and MUST be fixed by
  Task 4.7. Source: technical_context_v7:1320-1354; BSOT:1326.

- ~~D4 — Event emission deferred to Day 10~~ (was plan "Events" section).
  Day-3 deferred identity.user.created to Day 10. Day-4 creates users via Google + admin paths.
  → Defer to Day 10, consistent with Day-3. EXPLICIT forward-dependency: Day 10 must emit
  identity.user.created for ALL THREE creation flows — email register (Day 3), Google OAuth
  (Task 4.2), admin-created (Task 4.4). Source: day-3-checklist.md:102; technical_context_v7:4770.
