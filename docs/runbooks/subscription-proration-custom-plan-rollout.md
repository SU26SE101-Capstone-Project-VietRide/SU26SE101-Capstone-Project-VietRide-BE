# Runbook rollout: proration và Custom Plan

## 1. Mục tiêu

Rollout thay đổi subscription theo hai pha database để tránh ép `NOT NULL` trước khi pricing snapshot được backfill đầy đủ.

Hai migration bắt buộc:

- Release A: `20260819124524_ExpandSubscriptionProrationAndCustomPlans`
- Release B: `20260819124848_ContractSubscriptionPricingSnapshots`

Không được để Identity auto-migrate thẳng lên Release B trong lần deploy A.

## 2. Release A — expand và backfill

Đặt biến môi trường cho Identity:

```text
IDENTITY_MIGRATION_TARGET=20260819124524_ExpandSubscriptionProrationAndCustomPlans
```

Khởi động Identity. Startup migration sẽ dừng đúng ở Release A và reload PostgreSQL type catalog.

Migration A:

- Thêm các pricing snapshot ở trạng thái nullable.
- Backfill theo batch 500 dòng.
- Legacy attempt được chuyển thành full-price quote snapshot.
- Trial hoặc subscription chưa có billing period nhận `cycle_price_amount = 0`.
- Subscription paid ưu tiên attempt `SUCCEEDED` gần nhất khớp plan/kỳ, sau đó fallback về giá hiện tại của plan.
- Thêm Custom Request/private-plan schema, constraint và index.

Xác nhận migration đang dừng ở A:

```sql
SELECT migration_id
FROM vietride_identity.__ef_migrations_history
ORDER BY migration_id DESC
LIMIT 1;
```

Kết quả phải là:

```text
20260819124524_ExpandSubscriptionProrationAndCustomPlans
```

## 3. Zero-null gate

Chạy trước khi deploy Release B:

```sql
SELECT
    (SELECT COUNT(*)
     FROM vietride_identity.operator_subscriptions
     WHERE cycle_price_amount IS NULL) AS subscription_nulls,
    (SELECT COUNT(*)
     FROM vietride_identity.subscription_upgrade_attempts
     WHERE source_plan_id IS NULL
        OR quoted_at IS NULL
        OR period_from IS NULL
        OR period_to IS NULL
        OR current_cycle_price_amount IS NULL
        OR target_cycle_price_amount IS NULL
        OR unused_credit_amount IS NULL
        OR prorated_target_amount IS NULL
        OR is_prorated IS NULL) AS attempt_nulls;
```

Chỉ được tiếp tục khi cả hai giá trị bằng `0`.

Kiểm tra invariant tiền:

```sql
SELECT COUNT(*) AS invalid_amount_rows
FROM vietride_identity.subscription_upgrade_attempts
WHERE amount <= 0
   OR current_cycle_price_amount < 0
   OR target_cycle_price_amount < 0
   OR unused_credit_amount < 0
   OR prorated_target_amount < 0
   OR prorated_target_amount <> unused_credit_amount + amount;
```

Kết quả phải bằng `0`.

## 4. Release B — contract

Đổi target:

```text
IDENTITY_MIGRATION_TARGET=20260819124848_ContractSubscriptionPricingSnapshots
```

Khởi động lại Identity. Migration B tự chạy lại zero-null guard ở database trước khi ép `NOT NULL`. Nếu còn null, startup phải fail và không được bỏ qua guard.

Sau khi tất cả instance đã lên Release B, có thể bỏ `IDENTITY_MIGRATION_TARGET`; mặc định Identity migrate đến migration mới nhất.

## 5. Verification bắt buộc

Chạy:

```powershell
dotnet ef migrations has-pending-model-changes `
  -p apps\identity\src\VietRide.Identity.Infrastructure `
  -s apps\identity\src\VietRide.Identity.Api `
  --configuration Release
```

Chạy PostgreSQL integration tests:

```powershell
dotnet test apps\identity\tests\VietRide.Identity.IntegrationTests\VietRide.Identity.IntegrationTests.csproj `
  -c Release --filter FullyQualifiedName~SubscriptionUpgradePostgresTests

dotnet test apps\payment\tests\VietRide.Payment.IntegrationTests\VietRide.Payment.IntegrationTests.csproj `
  -c Release --filter FullyQualifiedName~SubscriptionWalletConcurrencyPostgresTests
```

Các gate phải chứng minh:

- Concurrent quote không tạo hai active attempt.
- Unique violation đúng constraint được map 409, không rơi vào 500.
- Concurrent confirm chỉ gọi Payment một lần.
- Concurrent WALLET create chỉ có một Payment, một debit, một platform credit và một success event.
- Target deactivate sau quote nhưng trước confirm bị từ chối.
- Migration A/B downgrade và reapply được; activity-log enum labels được giữ lại có chủ đích khi downgrade vì PostgreSQL không hỗ trợ xóa label an toàn.

## 6. Rollback

Nếu Release B lỗi ứng dụng nhưng schema hợp lệ, rollback binary trước; cột `NOT NULL` tương thích với Release A vì dữ liệu đã zero-null.

Nếu cần downgrade migration B, migrate về A; các cột snapshot trở lại nullable.

Nếu cần downgrade migration A trên môi trường thử nghiệm, migration sẽ xóa bảng/cột/index/FK mới. Các label mới của `activity_log_action` vẫn được giữ lại để downgrade/reapply an toàn. Không chạy downgrade A trên production khi đã có Custom Request hoặc Custom Plan thật nếu chưa có phương án sao lưu dữ liệu.
