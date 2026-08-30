using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeParcelIncidentSearchDeadlineNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "search_deadline",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcel_incidents
                SET search_deadline = created_at + INTERVAL '72 hours'
                WHERE search_deadline IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "search_deadline",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
