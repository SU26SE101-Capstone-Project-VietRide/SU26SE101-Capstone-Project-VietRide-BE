using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260712130000_AddSubscriptionQuotaAllocations")]
public partial class AddSubscriptionQuotaAllocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscription_quota_allocations",
            schema: "vietride_identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                period_key = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_subscription_quota_allocations", x => x.id);
                table.ForeignKey(
                    name: "fk_subscription_quota_allocations_subscription_id",
                    column: x => x.subscription_id,
                    principalSchema: "vietride_identity",
                    principalTable: "operator_subscriptions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "uq_subscription_quota_allocations_resource",
            schema: "vietride_identity",
            table: "subscription_quota_allocations",
            columns: new[] { "operator_id", "resource", "resource_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "idx_subscription_quota_allocations_subscription_resource",
            schema: "vietride_identity",
            table: "subscription_quota_allocations",
            columns: new[] { "subscription_id", "resource", "released_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "subscription_quota_allocations", schema: "vietride_identity");
}
