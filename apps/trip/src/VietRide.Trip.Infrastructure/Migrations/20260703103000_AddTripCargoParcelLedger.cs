using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripCargoParcelLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_cargo_parcels",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_cargo_parcels", x => x.id);
                    table.CheckConstraint("chk_trip_cargo_parcels_state", "state IN ('RESERVED', 'LOADED', 'RELEASED')");
                    table.CheckConstraint("chk_trip_cargo_parcels_weight_positive", "weight_kg > 0");
                    table.ForeignKey(
                        name: "fk_trip_cargo_parcels_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_trip_cargo_parcels_trip_state",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                columns: new[] { "trip_id", "state" });

            migrationBuilder.CreateIndex(
                name: "uq_trip_cargo_parcels_trip_parcel",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                columns: new[] { "trip_id", "parcel_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_cargo_parcels",
                schema: "vietride_trip");
        }
    }
}
