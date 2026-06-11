using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NativeBookingEnumMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No database DDL is required. The enum types/columns already exist from
            // InitBookingSchema; this migration records the EF/Npgsql native enum
            // mapping in the model snapshot so runtime writes use typed PG enums.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No database DDL to revert.
        }
    }
}
