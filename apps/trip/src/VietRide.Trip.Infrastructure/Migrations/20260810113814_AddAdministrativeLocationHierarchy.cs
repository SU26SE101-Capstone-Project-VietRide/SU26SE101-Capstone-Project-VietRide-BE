using System;
using System.IO;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeLocationHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_locations_type",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_location_id",
                schema: "vietride_trip",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_locations_active_parent_sort",
                schema: "vietride_trip",
                table: "locations",
                columns: new[] { "parent_location_id", "sort_order", "name" },
                filter: "parent_location_id IS NOT NULL AND is_active = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "chk_locations_parent_level",
                schema: "vietride_trip",
                table: "locations",
                sql: "((type IN ('PROVINCE', 'MUNICIPALITY') AND parent_location_id IS NULL) OR (type IN ('WARD', 'COMMUNE', 'SPECIAL_ZONE') AND parent_location_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "chk_locations_type",
                schema: "vietride_trip",
                table: "locations",
                sql: "type IN ('PROVINCE', 'MUNICIPALITY', 'WARD', 'COMMUNE', 'SPECIAL_ZONE')");

            migrationBuilder.AddForeignKey(
                name: "fk_locations_parent_location_id",
                schema: "vietride_trip",
                table: "locations",
                column: "parent_location_id",
                principalSchema: "vietride_trip",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(ReadEmbeddedSql("vietnam-administrative-units-2025-up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReadEmbeddedSql("vietnam-administrative-units-2025-down.sql"));

            migrationBuilder.DropForeignKey(
                name: "fk_locations_parent_location_id",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "idx_locations_active_parent_sort",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_locations_parent_level",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_locations_type",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "parent_location_id",
                schema: "vietride_trip",
                table: "locations");

            migrationBuilder.AddCheckConstraint(
                name: "chk_locations_type",
                schema: "vietride_trip",
                table: "locations",
                sql: "type IN ('PROVINCE', 'MUNICIPALITY')");
        }

        private static string ReadEmbeddedSql(string fileName)
        {
            var resourceName = $"VietRide.Trip.Infrastructure.Migrations.Data.{fileName}";
            using var stream = typeof(AddAdministrativeLocationHierarchy).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
