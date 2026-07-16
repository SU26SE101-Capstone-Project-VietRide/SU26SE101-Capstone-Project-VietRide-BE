using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripDestinationArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "destination_arrived_at",
                schema: "vietride_trip",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "destination_arrived_by_user_id",
                schema: "vietride_trip",
                table: "trips",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "destination_arrived_at",
                schema: "vietride_trip",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "destination_arrived_by_user_id",
                schema: "vietride_trip",
                table: "trips");
        }
    }
}
