using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations;

public partial class AddLoadedByUserId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "loaded_by_user_id",
            schema: "vietride_parcel",
            table: "parcels",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "idx_parcels_loaded_by_user_id",
            schema: "vietride_parcel",
            table: "parcels",
            column: "loaded_by_user_id",
            filter: "loaded_by_user_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_parcels_loaded_by_user_id",
            schema: "vietride_parcel",
            table: "parcels");

        migrationBuilder.DropColumn(
            name: "loaded_by_user_id",
            schema: "vietride_parcel",
            table: "parcels");
    }
}
