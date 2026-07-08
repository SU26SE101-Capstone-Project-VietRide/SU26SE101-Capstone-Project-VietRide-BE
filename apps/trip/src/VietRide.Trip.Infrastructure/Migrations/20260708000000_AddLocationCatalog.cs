using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TripDbContext))]
    [Migration("20260708000000_AddLocationCatalog")]
    public partial class AddLocationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "locations",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                    table.CheckConstraint("chk_locations_sort_order_non_negative", "sort_order >= 0");
                    table.CheckConstraint("chk_locations_type", "type IN ('PROVINCE', 'MUNICIPALITY')");
                });

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "vietride_trip",
                table: "stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "vietride_trip",
                table: "stops",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_locations_active_sort",
                schema: "vietride_trip",
                table: "locations",
                columns: new[] { "is_active", "sort_order", "name" });

            migrationBuilder.CreateIndex(
                name: "uq_locations_code",
                schema: "vietride_trip",
                table: "locations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_stations_location_id",
                schema: "vietride_trip",
                table: "stations",
                column: "location_id",
                filter: "location_id IS NOT NULL AND is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_stops_location_id",
                schema: "vietride_trip",
                table: "stops",
                column: "location_id",
                filter: "location_id IS NOT NULL AND is_active = TRUE");

            migrationBuilder.AddForeignKey(
                name: "fk_stations_locations_location_id",
                schema: "vietride_trip",
                table: "stations",
                column: "location_id",
                principalSchema: "vietride_trip",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_stops_locations_location_id",
                schema: "vietride_trip",
                table: "stops",
                column: "location_id",
                principalSchema: "vietride_trip",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_trip.locations (code, name, type, sort_order, is_active)
                VALUES
                    ('HN', 'Ha Noi', 'MUNICIPALITY', 1, TRUE),
                    ('HPG', 'Hai Phong', 'MUNICIPALITY', 2, TRUE),
                    ('HUE', 'Hue', 'MUNICIPALITY', 3, TRUE),
                    ('DNG', 'Da Nang', 'MUNICIPALITY', 4, TRUE),
                    ('HCM', 'Ho Chi Minh City', 'MUNICIPALITY', 5, TRUE),
                    ('CTO', 'Can Tho', 'MUNICIPALITY', 6, TRUE),
                    ('LCU', 'Lai Chau', 'PROVINCE', 7, TRUE),
                    ('DBN', 'Dien Bien', 'PROVINCE', 8, TRUE),
                    ('SLA', 'Son La', 'PROVINCE', 9, TRUE),
                    ('LSN', 'Lang Son', 'PROVINCE', 10, TRUE),
                    ('QNH', 'Quang Ninh', 'PROVINCE', 11, TRUE),
                    ('THA', 'Thanh Hoa', 'PROVINCE', 12, TRUE),
                    ('NAN', 'Nghe An', 'PROVINCE', 13, TRUE),
                    ('HTH', 'Ha Tinh', 'PROVINCE', 14, TRUE),
                    ('CBG', 'Cao Bang', 'PROVINCE', 15, TRUE),
                    ('TQG', 'Tuyen Quang', 'PROVINCE', 16, TRUE),
                    ('LCI', 'Lao Cai', 'PROVINCE', 17, TRUE),
                    ('TNN', 'Thai Nguyen', 'PROVINCE', 18, TRUE),
                    ('PTO', 'Phu Tho', 'PROVINCE', 19, TRUE),
                    ('BNH', 'Bac Ninh', 'PROVINCE', 20, TRUE),
                    ('HYN', 'Hung Yen', 'PROVINCE', 21, TRUE),
                    ('NBH', 'Ninh Binh', 'PROVINCE', 22, TRUE),
                    ('QTI', 'Quang Tri', 'PROVINCE', 23, TRUE),
                    ('QNI', 'Quang Ngai', 'PROVINCE', 24, TRUE),
                    ('GLI', 'Gia Lai', 'PROVINCE', 25, TRUE),
                    ('KHA', 'Khanh Hoa', 'PROVINCE', 26, TRUE),
                    ('LDG', 'Lam Dong', 'PROVINCE', 27, TRUE),
                    ('DLK', 'Dak Lak', 'PROVINCE', 28, TRUE),
                    ('DNI', 'Dong Nai', 'PROVINCE', 29, TRUE),
                    ('TNY', 'Tay Ninh', 'PROVINCE', 30, TRUE),
                    ('VLG', 'Vinh Long', 'PROVINCE', 31, TRUE),
                    ('DTP', 'Dong Thap', 'PROVINCE', 32, TRUE),
                    ('AGG', 'An Giang', 'PROVINCE', 33, TRUE),
                    ('CMU', 'Ca Mau', 'PROVINCE', 34, TRUE)
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stations_locations_location_id",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropForeignKey(
                name: "fk_stops_locations_location_id",
                schema: "vietride_trip",
                table: "stops");

            migrationBuilder.DropIndex(
                name: "idx_stations_location_id",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "idx_stops_location_id",
                schema: "vietride_trip",
                table: "stops");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "vietride_trip",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "vietride_trip",
                table: "stops");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "vietride_trip");
        }
    }
}
