using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PoultryFarm.Infrastructure.Persistence;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260928161600_ClearPlatformStaffCompany")]
    public class ClearPlatformStaffCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Users" AS u
                SET "CompanyId" = NULL
                WHERE u."IsSystemAdmin" = TRUE
                   OR u."FarmRole" IN (0, 3)
                   OR EXISTS (
                        SELECT 1
                        FROM "UserRoles" ur
                        INNER JOIN "Roles" r ON r."Id" = ur."RoleId"
                        WHERE ur."UserId" = u."Id"
                          AND r."NormalizedName" IN ('SYSTEMADMIN', 'SUBADMIN', 'SUPERADMIN')
                   );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
