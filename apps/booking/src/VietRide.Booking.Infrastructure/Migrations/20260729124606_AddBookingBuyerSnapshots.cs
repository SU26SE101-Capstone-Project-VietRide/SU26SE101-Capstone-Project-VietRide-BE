using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingBuyerSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "buyer_avatar_url",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_display_name",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_email",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_phone",
                schema: "vietride_booking",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "buyer_avatar_url",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "buyer_display_name",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "buyer_email",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "buyer_phone",
                schema: "vietride_booking",
                table: "bookings");
        }
    }
}
