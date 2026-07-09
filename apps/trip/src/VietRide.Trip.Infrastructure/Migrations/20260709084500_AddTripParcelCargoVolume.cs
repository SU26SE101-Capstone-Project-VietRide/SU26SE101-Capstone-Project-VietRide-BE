using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TripDbContext))]
    [Migration("20260709084500_AddTripParcelCargoVolume")]
    public partial class AddTripParcelCargoVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_trips_cargo_counters_non_negative",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.AddColumn<decimal>(
                name: "max_cargo_volume_m3",
                schema: "vietride_trip",
                table: "trips",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reserved_parcel_volume_m3",
                schema: "vietride_trip",
                table: "trips",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_loaded_volume_m3",
                schema: "vietride_trip",
                table: "trips",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_volume_m3",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_weight_kg",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "volume_m3",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0.0001m);

            migrationBuilder.Sql(
                """
                UPDATE vietride_trip.trips t
                SET max_cargo_volume_m3 = v.max_cargo_volume_m3
                FROM vietride_trip.vehicles v
                WHERE t.vehicle_id = v.id
                  AND t.max_cargo_volume_m3 IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "chk_trips_cargo_counters_non_negative",
                schema: "vietride_trip",
                table: "trips",
                sql: "reserved_parcel_weight_kg >= 0 AND reserved_parcel_volume_m3 >= 0 AND total_loaded_weight_kg >= 0 AND total_loaded_volume_m3 >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_trip_cargo_parcels_actual_volume_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                sql: "actual_volume_m3 IS NULL OR actual_volume_m3 > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_trip_cargo_parcels_actual_weight_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                sql: "actual_weight_kg IS NULL OR actual_weight_kg > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_trip_cargo_parcels_volume_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels",
                sql: "volume_m3 > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_trips_cargo_counters_non_negative",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropCheckConstraint(
                name: "chk_trip_cargo_parcels_actual_volume_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_trip_cargo_parcels_actual_weight_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_trip_cargo_parcels_volume_positive",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.DropColumn(
                name: "max_cargo_volume_m3",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "reserved_parcel_volume_m3",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "total_loaded_volume_m3",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "actual_volume_m3",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.DropColumn(
                name: "actual_weight_kg",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.DropColumn(
                name: "volume_m3",
                schema: "vietride_trip",
                table: "trip_cargo_parcels");

            migrationBuilder.AddCheckConstraint(
                name: "chk_trips_cargo_counters_non_negative",
                schema: "vietride_trip",
                table: "trips",
                sql: "reserved_parcel_weight_kg >= 0 AND total_loaded_weight_kg >= 0");
        }
    }
}
