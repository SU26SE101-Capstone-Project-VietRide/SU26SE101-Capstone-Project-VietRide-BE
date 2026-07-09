using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelCapacityVolumeDimWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_chargeable_weight_kg",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_dim_weight_kg",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_height_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_length_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_volume_m3",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_width_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_chargeable_weight_kg",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0.01m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_dim_weight_kg",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0.01m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_height_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_length_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_volume_m3",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0.0001m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_width_cm",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "pending_action_reason",
                schema: "vietride_parcel",
                table: "parcels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_action_type",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "refund_amount",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "minimum_price_vnd",
                schema: "vietride_parcel",
                table: "parcel_route_fares",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "price_per_chargeable_kg_vnd",
                schema: "vietride_parcel",
                table: "parcel_route_fares",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.CreateTable(
                name: "operator_deposit_policies",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deposit_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_deposit_policies", x => x.id);
                    table.CheckConstraint("chk_operator_deposit_policies_percent", "deposit_percent > 0 AND deposit_percent <= 100");
                });

            migrationBuilder.CreateTable(
                name: "system_configs",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decimal_value = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_configs", x => x.id);
                    table.CheckConstraint("chk_system_configs_version_positive", "version > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_actual_dimensions_positive",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "(actual_length_cm IS NULL AND actual_width_cm IS NULL AND actual_height_cm IS NULL) OR (actual_length_cm > 0 AND actual_width_cm > 0 AND actual_height_cm > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_dimensions_positive",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "estimated_length_cm > 0 AND estimated_width_cm > 0 AND estimated_height_cm > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_volume_positive",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "estimated_volume_m3 > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_route_fares_weight_price_non_negative",
                schema: "vietride_parcel",
                table: "parcel_route_fares",
                sql: "price_per_chargeable_kg_vnd >= 0 AND minimum_price_vnd >= 0");

            migrationBuilder.CreateIndex(
                name: "idx_operator_deposit_policies_lookup",
                schema: "vietride_parcel",
                table: "operator_deposit_policies",
                columns: new[] { "operator_id", "route_id", "is_active", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "idx_system_configs_lookup",
                schema: "vietride_parcel",
                table: "system_configs",
                columns: new[] { "key", "is_active", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "uq_system_configs_key_version",
                schema: "vietride_parcel",
                table: "system_configs",
                columns: new[] { "key", "version" },
                unique: true);

            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcel_route_fares
                SET price_per_chargeable_kg_vnd = price_vnd
                WHERE price_per_chargeable_kg_vnd = 0;

                INSERT INTO vietride_parcel.system_configs
                    (key, decimal_value, version, is_active, effective_from)
                VALUES
                    ('DIM_WEIGHT_FACTOR', 6000, 1, TRUE, now()),
                    ('REWEIGH_TOLERANCE_PERCENT', 10, 1, TRUE, now()),
                    ('DEFAULT_DEPOSIT_PERCENT', 20, 1, TRUE, now()),
                    ('AUTO_APPROVE_OVERFLOW_PERCENT', 5, 1, TRUE, now())
                ON CONFLICT (key, version) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_deposit_policies",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "system_configs",
                schema: "vietride_parcel");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_actual_dimensions_positive",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_dimensions_positive",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_volume_positive",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_route_fares_weight_price_non_negative",
                schema: "vietride_parcel",
                table: "parcel_route_fares");

            migrationBuilder.DropColumn(
                name: "actual_chargeable_weight_kg",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_dim_weight_kg",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_height_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_length_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_volume_m3",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_width_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "deposit_percent",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_chargeable_weight_kg",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_dim_weight_kg",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_height_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_length_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_volume_m3",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_width_cm",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "pending_action_reason",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "pending_action_type",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "refund_amount",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "minimum_price_vnd",
                schema: "vietride_parcel",
                table: "parcel_route_fares");

            migrationBuilder.DropColumn(
                name: "price_per_chargeable_kg_vnd",
                schema: "vietride_parcel",
                table: "parcel_route_fares");
        }
    }
}
