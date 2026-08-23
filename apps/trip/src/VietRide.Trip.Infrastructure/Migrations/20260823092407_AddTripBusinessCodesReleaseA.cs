using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripBusinessCodesReleaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trip_code",
                schema: "vietride_trip",
                table: "trips",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "vietride_trip",
                table: "routes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_trips_trip_code",
                schema: "vietride_trip",
                table: "trips",
                column: "trip_code",
                unique: true,
                filter: "trip_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_routes_operator_code",
                schema: "vietride_trip",
                table: "routes",
                columns: new[] { "operator_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL AND code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_trips_trip_code",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "uq_routes_operator_code",
                schema: "vietride_trip",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "trip_code",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "vietride_trip",
                table: "routes");
        }
    }
}
