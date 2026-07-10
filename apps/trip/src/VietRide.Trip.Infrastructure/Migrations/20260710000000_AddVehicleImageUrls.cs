using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Trip.Infrastructure.Persistence;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations;

[DbContext(typeof(TripDbContext))]
[Migration("20260710000000_AddVehicleImageUrls")]
public partial class AddVehicleImageUrls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "image_urls",
            schema: "vietride_trip",
            table: "vehicles",
            type: "jsonb",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(
            name: "image_urls",
            schema: "vietride_trip",
            table: "vehicles");
}
