using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routes",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    origin_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_fare = table.Column<long>(type: "bigint", nullable: false),
                    total_distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routes", x => x.id);
                    table.CheckConstraint("chk_routes_base_fare_non_negative", "base_fare >= 0");
                    table.CheckConstraint("chk_routes_origin_dest_different", "origin_station_id <> destination_station_id");
                    table.ForeignKey(
                        name: "FK_routes_routes_return_route_id",
                        column: x => x.return_route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_routes_stations_destination_station_id",
                        column: x => x.destination_station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_routes_stations_origin_station_id",
                        column: x => x.origin_station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alternative_routes",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    destination_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alternative_routes", x => x.id);
                    table.ForeignKey(
                        name: "FK_alternative_routes_routes_route_id",
                        column: x => x.route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alternative_routes_stations_destination_station_id",
                        column: x => x.destination_station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_stop_fare_templates",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fare_from_this_stop = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_stop_fare_templates", x => x.id);
                    table.CheckConstraint("chk_route_stop_fare_templates_effective_order", "effective_until IS NULL OR effective_until > effective_from");
                    table.CheckConstraint("chk_route_stop_fare_templates_fare_non_negative", "fare_from_this_stop >= 0");
                    table.ForeignKey(
                        name: "FK_route_stop_fare_templates_routes_route_id",
                        column: x => x.route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_route_stop_fare_templates_stops_stop_id",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_stops",
                schema: "vietride_trip",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    estimated_duration_from_origin_minutes = table.Column<int>(type: "integer", nullable: false),
                    distance_from_origin_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    allow_pickup = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    allow_dropoff = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_stops", x => new { x.route_id, x.stop_id });
                    table.CheckConstraint("chk_route_stops_allow_at_least_one", "allow_pickup = TRUE OR allow_dropoff = TRUE");
                    table.CheckConstraint("chk_route_stops_order_positive", "order_index > 0");
                    table.ForeignKey(
                        name: "FK_route_stops_routes_route_id",
                        column: x => x.route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_route_stops_stops_stop_id",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alternative_route_stops",
                schema: "vietride_trip",
                columns: table => new
                {
                    alternative_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    estimated_duration_from_origin_minutes = table.Column<int>(type: "integer", nullable: false),
                    distance_from_origin_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alternative_route_stops", x => new { x.alternative_route_id, x.stop_id });
                    table.CheckConstraint("chk_alternative_route_stops_order_positive", "order_index > 0");
                    table.ForeignKey(
                        name: "FK_alternative_route_stops_alternative_routes_alternative_rout~",
                        column: x => x.alternative_route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "alternative_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alternative_route_stops_stops_stop_id",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_alternative_route_stops_route_order",
                schema: "vietride_trip",
                table: "alternative_route_stops",
                columns: new[] { "alternative_route_id", "order_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_alternative_routes_route_id",
                schema: "vietride_trip",
                table: "alternative_routes",
                column: "route_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_route_stop_fare_templates_route_stop_effective",
                schema: "vietride_trip",
                table: "route_stop_fare_templates",
                columns: new[] { "route_id", "stop_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "idx_route_stops_stop_id",
                schema: "vietride_trip",
                table: "route_stops",
                column: "stop_id");

            migrationBuilder.CreateIndex(
                name: "uq_route_stops_route_order",
                schema: "vietride_trip",
                table: "route_stops",
                columns: new[] { "route_id", "order_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_routes_operator_id",
                schema: "vietride_trip",
                table: "routes",
                column: "operator_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_routes_origin_destination",
                schema: "vietride_trip",
                table: "routes",
                columns: new[] { "origin_station_id", "destination_station_id" },
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_routes_return_route_id",
                schema: "vietride_trip",
                table: "routes",
                column: "return_route_id",
                filter: "return_route_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alternative_route_stops",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "route_stop_fare_templates",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "route_stops",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "alternative_routes",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "routes",
                schema: "vietride_trip");
        }
    }
}
