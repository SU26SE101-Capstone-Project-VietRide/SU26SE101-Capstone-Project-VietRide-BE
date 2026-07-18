using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStationMergeRedirect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_station_id",
                schema: "vietride_trip",
                table: "stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_stations_merged_into",
                schema: "vietride_trip",
                table: "stations",
                column: "merged_into_station_id",
                filter: "merged_into_station_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stations_no_self_merge",
                schema: "vietride_trip",
                table: "stations",
                sql: "merged_into_station_id IS NULL OR merged_into_station_id <> id");

            migrationBuilder.AddForeignKey(
                name: "fk_stations_merged_into_station",
                schema: "vietride_trip",
                table: "stations",
                column: "merged_into_station_id",
                principalSchema: "vietride_trip",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stations_merged_into_station",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "idx_stations_merged_into",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stations_no_self_merge",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "merged_into_station_id",
                schema: "vietride_trip",
                table: "stations");
        }
    }
}
