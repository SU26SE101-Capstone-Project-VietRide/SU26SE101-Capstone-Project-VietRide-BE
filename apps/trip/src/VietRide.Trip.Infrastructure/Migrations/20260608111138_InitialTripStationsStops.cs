using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTripStationsStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vietride_trip");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"unaccent\";");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_street = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    operating_hours = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    facilities = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    supports_shuttle = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stops",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    google_place_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    shared_suggestion = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    replaced_by_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stops", x => x.id);
                    table.CheckConstraint("chk_stops_no_self_replacement", "replaced_by_stop_id IS NULL OR replaced_by_stop_id <> id");
                    table.ForeignKey(
                        name: "FK_stops_stops_replaced_by_stop_id",
                        column: x => x.replaced_by_stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "operator_stations",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name_override = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    counter_location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    instructions = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_stations", x => x.id);
                    table.ForeignKey(
                        name: "FK_operator_stations_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_operator_stations_operator_id",
                schema: "vietride_trip",
                table: "operator_stations",
                column: "operator_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_operator_stations_station_id",
                schema: "vietride_trip",
                table: "operator_stations",
                column: "station_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "uq_operator_stations_operator_station",
                schema: "vietride_trip",
                table: "operator_stations",
                columns: new[] { "operator_id", "station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                schema: "vietride_trip",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "idx_stations_city_province",
                schema: "vietride_trip",
                table: "stations",
                columns: new[] { "city", "province" },
                filter: "is_active = TRUE");

            migrationBuilder.Sql("CREATE INDEX idx_stations_name_trgm ON vietride_trip.stations USING gin (name gin_trgm_ops) WHERE FALSE;");

            migrationBuilder.CreateIndex(
                name: "idx_stations_supports_shuttle",
                schema: "vietride_trip",
                table: "stations",
                column: "supports_shuttle",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "uq_stations_slug",
                schema: "vietride_trip",
                table: "stations",
                column: "slug",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_stops_operator_id",
                schema: "vietride_trip",
                table: "stops",
                column: "operator_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_stops_replaced_by",
                schema: "vietride_trip",
                table: "stops",
                column: "replaced_by_stop_id",
                filter: "replaced_by_stop_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_stops_shared_suggestion",
                schema: "vietride_trip",
                table: "stops",
                column: "shared_suggestion",
                filter: "shared_suggestion = TRUE AND is_active = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS vietride_trip.idx_stations_name_trgm;");

            migrationBuilder.DropTable(
                name: "operator_stations",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "stops",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "stations",
                schema: "vietride_trip");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"pg_trgm\";");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"unaccent\";");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"pgcrypto\";");
        }
    }
}
