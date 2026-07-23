using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripAlternativeRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "alternative_route_id",
                schema: "vietride_trip",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_trips_alternative_route_id",
                schema: "vietride_trip",
                table: "trips",
                column: "alternative_route_id");

            migrationBuilder.AddForeignKey(
                name: "FK_trips_alternative_routes_alternative_route_id",
                schema: "vietride_trip",
                table: "trips",
                column: "alternative_route_id",
                principalSchema: "vietride_trip",
                principalTable: "alternative_routes",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trips_alternative_routes_alternative_route_id",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "idx_trips_alternative_route_id",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "alternative_route_id",
                schema: "vietride_trip",
                table: "trips");

        }
    }
}
