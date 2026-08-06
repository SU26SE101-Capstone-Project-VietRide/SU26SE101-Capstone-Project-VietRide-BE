using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStationProvinceWithWard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ward",
                schema: "vietride_trip",
                table: "stations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE vietride_trip.stations AS station
                SET city = COALESCE(
                    (SELECT location.name
                     FROM vietride_trip.locations AS location
                     WHERE location.id = station.location_id),
                    NULLIF(BTRIM(station.province), ''),
                    station.city);
                """);

            migrationBuilder.DropIndex(
                name: "idx_stations_city_province",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "province",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.CreateIndex(
                name: "idx_stations_city_ward",
                schema: "vietride_trip",
                table: "stations",
                columns: new[] { "city", "ward" },
                filter: "is_active = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_stations_city_ward",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.AddColumn<string>(
                name: "province",
                schema: "vietride_trip",
                table: "stations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE vietride_trip.stations
                SET province = city;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "province",
                schema: "vietride_trip",
                table: "stations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ward",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.CreateIndex(
                name: "idx_stations_city_province",
                schema: "vietride_trip",
                table: "stations",
                columns: new[] { "city", "province" },
                filter: "is_active = TRUE");
        }
    }
}
