using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShuttleTripAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "vietride_trip",
                table: "shuttle_trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "vietride_trip",
                table: "shuttle_trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_user_id",
                schema: "vietride_trip",
                table: "shuttle_trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "vietride_trip",
                table: "shuttle_trips",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "vietride_trip",
                table: "shuttle_trips");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "vietride_trip",
                table: "shuttle_trips");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                schema: "vietride_trip",
                table: "shuttle_trips");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "vietride_trip",
                table: "shuttle_trips");
        }
    }
}
