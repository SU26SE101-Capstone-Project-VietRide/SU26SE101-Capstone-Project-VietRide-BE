using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorFareSurcharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator_fare_surcharge_periods",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    surcharge_percent = table.Column<short>(type: "smallint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_fare_surcharge_periods", x => x.id);
                    table.CheckConstraint("chk_operator_fare_surcharge_periods_date_order", "start_date <= end_date");
                    table.CheckConstraint("chk_operator_fare_surcharge_periods_name_not_blank", "length(btrim(name)) BETWEEN 1 AND 120");
                    table.CheckConstraint("chk_operator_fare_surcharge_periods_percent", "surcharge_percent BETWEEN 1 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "operator_fare_surcharge_settings",
                schema: "vietride_trip",
                columns: table => new
                {
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_fare_surcharge_settings", x => x.operator_id);
                });

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.operator_fare_surcharge_periods
                ADD CONSTRAINT ex_operator_fare_surcharge_periods_no_active_overlap
                EXCLUDE USING gist (
                    operator_id WITH =,
                    daterange(start_date, end_date + 1, '[)') WITH &&
                )
                WHERE (is_active = TRUE AND deleted_at IS NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "idx_operator_fare_surcharge_periods_operator_start",
                schema: "vietride_trip",
                table: "operator_fare_surcharge_periods",
                columns: new[] { "operator_id", "start_date", "id" },
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.operator_fare_surcharge_periods
                DROP CONSTRAINT IF EXISTS ex_operator_fare_surcharge_periods_no_active_overlap;
                """);

            migrationBuilder.DropTable(
                name: "operator_fare_surcharge_periods",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "operator_fare_surcharge_settings",
                schema: "vietride_trip");
        }
    }
}
