using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripVehiclesAndDriverSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TYPE vehicle_status AS ENUM ('ACTIVE', 'MAINTENANCE', 'OFF_DUTY', 'RETIRED');");

            migrationBuilder.CreateTable(
                name: "vehicle_types",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    estimated_passenger_luggage_kg_per_seat = table.Column<int>(type: "integer", nullable: true),
                    default_seat_count = table.Column<int>(type: "integer", nullable: true),
                    is_system_defined = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_types", x => x.id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_trip.vehicle_types (
                    id,
                    code,
                    display_name,
                    estimated_passenger_luggage_kg_per_seat,
                    default_seat_count,
                    is_system_defined,
                    is_active
                ) VALUES
                    ('00000000-0000-0000-0000-000000000101', 'STANDARD_BUS', 'Xe ghế ngồi tiêu chuẩn', 10, 45, TRUE, TRUE),
                    ('00000000-0000-0000-0000-000000000102', 'LIMOUSINE', 'Limousine', 15, 9, TRUE, TRUE),
                    ('00000000-0000-0000-0000-000000000103', 'SLEEPER_BUS', 'Xe giường nằm', 20, 40, TRUE, TRUE)
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seat_layout_json = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    max_cargo_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    max_cargo_volume_m3 = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    status = table.Column<string>(type: "vehicle_status", nullable: false, defaultValueSql: "'ACTIVE'::vehicle_status"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                    table.CheckConstraint("chk_vehicles_cargo_weight_non_negative", "max_cargo_weight_kg IS NULL OR max_cargo_weight_kg >= 0");
                    table.CheckConstraint("chk_vehicles_total_seats_positive", "total_seats > 0");
                    table.ForeignKey(
                        name: "FK_vehicles_vehicle_types_vehicle_type_id",
                        column: x => x.vehicle_type_id,
                        principalSchema: "vietride_trip",
                        principalTable: "vehicle_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "driver_schedules",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    day_of_week = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    departure_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_schedules", x => x.id);
                    table.CheckConstraint("chk_driver_schedules_valid_until_after_from", "valid_until IS NULL OR valid_until >= valid_from");
                    table.ForeignKey(
                        name: "FK_driver_schedules_routes_route_id",
                        column: x => x.route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_driver_schedules_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "vietride_trip",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_driver_schedules_driver_active",
                schema: "vietride_trip",
                table: "driver_schedules",
                columns: new[] { "driver_user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "idx_driver_schedules_operator_active",
                schema: "vietride_trip",
                table: "driver_schedules",
                columns: new[] { "operator_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "idx_driver_schedules_route_active",
                schema: "vietride_trip",
                table: "driver_schedules",
                columns: new[] { "route_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "idx_driver_schedules_vehicle_active",
                schema: "vietride_trip",
                table: "driver_schedules",
                columns: new[] { "vehicle_id", "is_active" },
                filter: "vehicle_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_vehicle_types_is_active",
                schema: "vietride_trip",
                table: "vehicle_types",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_types_code",
                schema: "vietride_trip",
                table: "vehicle_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_vehicles_operator_status",
                schema: "vietride_trip",
                table: "vehicles",
                columns: new[] { "operator_id", "status" },
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_vehicles_vehicle_type_id",
                schema: "vietride_trip",
                table: "vehicles",
                column: "vehicle_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_vehicles_license_plate",
                schema: "vietride_trip",
                table: "vehicles",
                column: "license_plate",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_schedules",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "vehicle_types",
                schema: "vietride_trip");

            migrationBuilder.Sql("DROP TYPE vehicle_status;");
        }
    }
}
