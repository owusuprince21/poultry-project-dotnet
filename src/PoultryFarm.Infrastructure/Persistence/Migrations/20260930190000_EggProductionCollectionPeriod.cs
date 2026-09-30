using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PoultryFarm.Infrastructure.Persistence;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260930190000_EggProductionCollectionPeriod")]
    public class EggProductionCollectionPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectionPeriod",
                table: "EggProductions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Morning");

            migrationBuilder.DropIndex(
                name: "IX_EggProductions_BatchVariantId_Date_CollectionType_EggColor",
                table: "EggProductions");

            migrationBuilder.CreateIndex(
                name: "IX_EggProductions_BatchVariantId_Date_CollectionPeriod_CollectionType_EggColor",
                table: "EggProductions",
                columns: new[] { "BatchVariantId", "Date", "CollectionPeriod", "CollectionType", "EggColor" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EggProductions_BatchVariantId_Date_CollectionPeriod_CollectionType_EggColor",
                table: "EggProductions");

            migrationBuilder.DropColumn(
                name: "CollectionPeriod",
                table: "EggProductions");

            migrationBuilder.CreateIndex(
                name: "IX_EggProductions_BatchVariantId_Date_CollectionType_EggColor",
                table: "EggProductions",
                columns: new[] { "BatchVariantId", "Date", "CollectionType", "EggColor" },
                unique: true);
        }
    }
}
