using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <summary>Adds durable actual-departure persistence for a trip stop.</summary>
    public partial class AddTripStopActualDepartureTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_departure_time",
                schema: "vietride_trip",
                table: "trip_stops",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_departure_time",
                schema: "vietride_trip",
                table: "trip_stops");
        }
    }
}
