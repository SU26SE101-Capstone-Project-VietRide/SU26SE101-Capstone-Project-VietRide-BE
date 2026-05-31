# Prompt Templates — NestJS VietRide

## Scaffold module mới
Read AGENTS.md §NestJS conventions and docs/developer-guides/nest/nest-scaffold-module.md first.

Task: Tạo module [tên aggregate] cho app [tracking/notification/rag]
Requirements:
- [mô tả ngắn]
Constraints:
- Prisma ORM via PrismaService only, NO TypeORM. Sử dụng schema.prisma riêng cho từng service.
- Tự động viết E2E test tại `test/[aggregate].e2e-spec.ts`.
- Verify: nx run <app>:lint + test + e2e + build before done, sau đó yêu cầu USER test manual theo manual-test-checklist.md

## Thêm endpoint
Read AGENTS.md §NestJS conventions and docs/developer-guides/nest/nest-add-endpoint.md first.

Task: Thêm [POST/GET/DELETE] /v1/[route] vào [app]
Requirements:
- [mô tả ngắn]
Constraints:
- ZodValidationPipe for input, HttpException for errors (RFC 7807 ProblemDetails).
- Tự động viết E2E test bằng Supertest tại `test/[endpoint].e2e-spec.ts`. Cover 3 cases: Happy path (200/201), Auth missing (401), Validation payload sai (400 + VALIDATION_FAILED).
- Verify: nx run <app>:lint + test + e2e + build before done, sau đó yêu cầu USER test manual theo manual-test-checklist.md

## Thêm event consumer
Read AGENTS.md §NestJS conventions and docs/developer-guides/nest/nest-event-handling.md first.

Task: Subscribe event [routing.key] tại app [tracking/notification/rag]
Requirements:
- [mô tả ngắn]
Constraints:
- Redis idempotency check bắt buộc
- Verify: nx run <app>:lint + test + build before done

## Build new service (auto-planning)
Read AGENTS.md §Service Planning Workflow first.

Task: [mô tả tự nhiên]
Constraints:
- Tự phân tích và chia phases trước khi code
- Tạo timeline file trước khi thực thi
- Chờ confirm sau mỗi phase
- Verify: nx run <app>:lint + test + build before done
