using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPointSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dropoff_point_address_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "dropoff_point_id_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dropoff_point_name_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dropoff_point_planned_at_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dropoff_point_type_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_address_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pickup_point_id_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_name_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pickup_point_planned_at_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_type_snapshot",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dropoff_point_address_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "dropoff_point_id_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "dropoff_point_name_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "dropoff_point_planned_at_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "dropoff_point_type_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_point_address_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_point_id_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_point_name_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_point_planned_at_snapshot",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_point_type_snapshot",
                schema: "vietride_booking",
                table: "bookings");
        }
    }
}
