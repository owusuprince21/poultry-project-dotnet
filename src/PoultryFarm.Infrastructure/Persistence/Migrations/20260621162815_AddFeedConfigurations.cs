using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FeedConfigurationId",
                table: "FeedStockLots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedConfigurationId",
                table: "FeedConsumptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeedConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BagSizeKg = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedConfigurations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedStockLots_FeedConfigurationId",
                table: "FeedStockLots",
                column: "FeedConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedConsumptions_FeedConfigurationId",
                table: "FeedConsumptions",
                column: "FeedConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedConfigurations_CompanyId_Name_BagSizeKg",
                table: "FeedConfigurations",
                columns: new[] { "CompanyId", "Name", "BagSizeKg" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FeedConsumptions_FeedConfigurations_FeedConfigurationId",
                table: "FeedConsumptions",
                column: "FeedConfigurationId",
                principalTable: "FeedConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FeedStockLots_FeedConfigurations_FeedConfigurationId",
                table: "FeedStockLots",
                column: "FeedConfigurationId",
                principalTable: "FeedConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FeedConsumptions_FeedConfigurations_FeedConfigurationId",
                table: "FeedConsumptions");

            migrationBuilder.DropForeignKey(
                name: "FK_FeedStockLots_FeedConfigurations_FeedConfigurationId",
                table: "FeedStockLots");

            migrationBuilder.DropTable(
                name: "FeedConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_FeedStockLots_FeedConfigurationId",
                table: "FeedStockLots");

            migrationBuilder.DropIndex(
                name: "IX_FeedConsumptions_FeedConfigurationId",
                table: "FeedConsumptions");

            migrationBuilder.DropColumn(
                name: "FeedConfigurationId",
                table: "FeedStockLots");

            migrationBuilder.DropColumn(
                name: "FeedConfigurationId",
                table: "FeedConsumptions");
        }
    }
}
