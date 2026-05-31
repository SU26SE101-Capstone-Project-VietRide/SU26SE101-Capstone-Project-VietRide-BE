# AI Changelog — VietRide NestJS

> Log những gì Codex/Antigravity đã làm. Append sau mỗi session.

## 2026-05-31
- **[Monorepo Root]**: Hoàn tất dọn dẹp `BACKEND_SOURCE_OF_TRUTH.md` và `AGENTS.md`. Thiết lập luật lõi cho hệ sinh thái NestJS.
- **[NestJS Workflow]**: Thực hiện thành công quá trình đại tu kiến trúc: Loại bỏ hoàn toàn `TypeORM` và `PgService`, chuyển đổi toàn bộ tài liệu và rules sang sử dụng **Prisma ORM** (với kiến trúc mỗi service 1 file `schema.prisma` riêng).
- **[Documentation]**: Khởi tạo hệ thống Knowledge Items (KI) cho AI, bộ Developer Guides và Prompt Templates.
- **[Task Management]**: Cấu hình file `TASK.md` và `CHANGELOG_AI.md` để lưu vết session.
- **[Monorepo Root]** [type: feat] Bật chế độ Thiết Quân Luật: Áp dụng các rules khắt khe về code quality (naming convention, chống magic numbers, vòng lặp import) vào `AGENTS.md`, `eslint.config.mjs` và bật strict flags trong `tsconfig.base.json`.
- **[Gateway]** [type: fix] Sửa các lỗi biên dịch TS do cờ `noUncheckedIndexedAccess` và `exactOptionalPropertyTypes` quét ra — `proxy.middleware.ts`, `app.module.ts`.
