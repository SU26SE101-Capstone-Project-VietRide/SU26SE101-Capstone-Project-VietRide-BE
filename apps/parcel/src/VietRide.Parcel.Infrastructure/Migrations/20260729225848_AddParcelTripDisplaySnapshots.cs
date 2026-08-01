using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelTripDisplaySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trip_snapshot_destination_station_name",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trip_snapshot_origin_station_name",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_snapshot_route_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trip_snapshot_route_name",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_snapshot_vehicle_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trip_snapshot_vehicle_license_plate",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_parcels_trip_snapshot_backfill",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "created_at", "id" },
                filter: "trip_snapshot_route_id IS NULL OR trip_snapshot_route_name IS NULL OR trip_snapshot_origin_station_name IS NULL OR trip_snapshot_destination_station_name IS NULL OR trip_snapshot_vehicle_id IS NULL OR trip_snapshot_vehicle_license_plate IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_parcels_trip_snapshot_backfill",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_destination_station_name",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_origin_station_name",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_route_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_route_name",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_vehicle_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "trip_snapshot_vehicle_license_plate",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
