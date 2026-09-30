using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PoultryFarm.Infrastructure.Persistence;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260930193000_FeedConsumptionCollectionPeriod")]
    public class FeedConsumptionCollectionPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectionPeriod",
                table: "FeedConsumptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Morning");

            migrationBuilder.CreateIndex(
                name: "IX_FeedConsumptions_BatchVariantId_Date_FeedConfigurationId_CollectionPeriod",
                table: "FeedConsumptions",
                columns: new[] { "BatchVariantId", "Date", "FeedConfigurationId", "CollectionPeriod" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedConsumptions_BatchVariantId_Date_FeedConfigurationId_CollectionPeriod",
                table: "FeedConsumptions");

            migrationBuilder.DropColumn(
                name: "CollectionPeriod",
                table: "FeedConsumptions");
        }
    }
}
