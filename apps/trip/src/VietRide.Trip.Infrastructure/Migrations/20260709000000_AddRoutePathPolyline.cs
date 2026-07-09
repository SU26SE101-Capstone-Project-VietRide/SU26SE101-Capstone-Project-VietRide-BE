using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TripDbContext))]
    [Migration("20260709000000_AddRoutePathPolyline")]
    public partial class AddRoutePathPolyline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "path_polyline",
                schema: "vietride_trip",
                table: "routes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "path_polyline",
                schema: "vietride_trip",
                table: "alternative_routes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "path_polyline",
                schema: "vietride_trip",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "path_polyline",
                schema: "vietride_trip",
                table: "alternative_routes");
        }
    }
}
