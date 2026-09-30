using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PoultryFarm.Infrastructure.Persistence;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260930173000_ClearPlatformStaffFarmNotifications")]
    public class ClearPlatformStaffFarmNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AppNotifications" AS n
                USING "Users" AS recipient
                WHERE n."RecipientUserId" = recipient."Id"
                  AND (
                        recipient."IsSystemAdmin" = TRUE
                        OR recipient."FarmRole" IN (0, 3)
                  )
                  AND NOT (
                        n."TargetType" = 'farmer_registration'
                        OR (
                            n."Kind" = 'chat'
                            AND NOT EXISTS (
                                SELECT 1
                                FROM "Users" AS other
                                WHERE other."FarmRole" = 4
                                  AND (other."Id" = n."TargetId" OR other."Id" = n."ActorUserId")
                            )
                        )
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
