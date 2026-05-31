# Scaffold a NestJS Module — VietRide

> How to create a new domain aggregate (Module + Controller + Service + Repository + DTO)
> inside an existing NestJS app. Follow every step in order.

## Prerequisites

Read `nest-conventions.md` first. All naming and import rules apply here.

---

## Step 1 — Generate files with Nx CLI

```bash
# Generate module
nx g @nx/nest:module --project=<app-name> <aggregate-name>

# Generate controller (no test file needed at scaffold time)
nx g @nx/nest:controller --project=<app-name> <aggregate-name> --no-spec

# Generate service
nx g @nx/nest:service --project=<app-name> <aggregate-name> --no-spec
```

Example for a `trip` aggregate in the `tracking` app:
```bash
nx g @nx/nest:module    --project=tracking trip
nx g @nx/nest:controller --project=tracking trip --no-spec
nx g @nx/nest:service    --project=tracking trip --no-spec
```

Nx places files at `apps/<app>/src/<aggregate>/`. Do not move them.

---

## Step 2 — Create Repository

Nx does not generate repositories. Create manually:

```typescript
// apps/<app>/src/<aggregate>/<aggregate>.repository.ts
import { Injectable } from '@nestjs/common';
import { PgService } from '@vietride/nest-persistence';
import { randomUUID } from 'node:crypto';
import type { Create<Aggregate>Dto } from './dto/create-<aggregate>.dto';

export interface <Aggregate>Row {
  id: string;
  // ... columns matching your db-schema/<service>/schema.sql
  created_at: Date;
  updated_at: Date;
  deleted_at: Date | null;
}

@Injectable()
export class <Aggregate>Repository {
  constructor(private readonly pg: PgService) {}

  async findById(id: string): Promise<<Aggregate>Row | null> {
    const { rows } = await this.pg.query<<Aggregate>Row>(
      `SELECT * FROM <table> WHERE id = $1 AND deleted_at IS NULL`,
      [id],
    );
    return rows[0] ?? null;
  }

  async create(dto: Create<Aggregate>Dto): Promise<<Aggregate>Row> {
    const { rows } = await this.pg.query<<Aggregate>Row>(
      `INSERT INTO <table> (id, /* columns */, created_at, updated_at)
       VALUES ($1, /* $2... */, NOW(), NOW())
       RETURNING *`,
      [randomUUID(), /* values */],
    );
    return rows[0];
  }
}
```

Replace `<table>` with the actual table name from `db-schema/<service>/schema.sql`.
Always check the schema file — never invent column names.

---

## Step 3 — Create DTO

```typescript
// apps/<app>/src/<aggregate>/dto/create-<aggregate>.dto.ts
import { z } from 'zod';

export const Create<Aggregate>Schema = z.object({
  // fields matching API contract
});

export type Create<Aggregate>Dto = z.infer<typeof Create<Aggregate>Schema>;
```

---

## Step 4 — Wire the Module

```typescript
// apps/<app>/src/<aggregate>/<aggregate>.module.ts
import { Module } from '@nestjs/common';
import { <Aggregate>Controller } from './<aggregate>.controller';
import { <Aggregate>Service } from './<aggregate>.service';
import { <Aggregate>Repository } from './<aggregate>.repository';

@Module({
  controllers: [<Aggregate>Controller],
  providers: [<Aggregate>Service, <Aggregate>Repository],
})
export class <Aggregate>Module {}
```

---

## Step 5 — Import into AppModule

```typescript
// apps/<app>/src/app.module.ts
import { <Aggregate>Module } from './<aggregate>/<aggregate>.module';

@Module({
  imports: [
    NestCommonModule,
    NestPersistenceModule.forRoot({ connectionString: env.DATABASE_URL }),
    <Aggregate>Module,   // ← add here
  ],
  // ...
})
export class AppModule {}
```

---

## Step 6 — Verify

```bash
npm run lint:ts
npm run build:ts
```

Both must pass before moving on. Fix any lint errors before continuing.

---

## Checklist

- [ ] Files generated via Nx CLI (not created manually except Repository and DTO)
- [ ] Repository uses `PgService` — no Prisma, no ORM
- [ ] DTO uses Zod schema + inferred type
- [ ] Module declares Controller, Service, Repository in `providers`
- [ ] Module imported into AppModule
- [ ] Lint and build succeed
