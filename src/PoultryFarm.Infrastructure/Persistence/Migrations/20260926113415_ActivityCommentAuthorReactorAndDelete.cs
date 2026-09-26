using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityCommentAuthorReactorAndDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorReactorKey",
                table: "FarmActivityComments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmActivityComments_AuthorReactorKey",
                table: "FarmActivityComments",
                column: "AuthorReactorKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FarmActivityComments_AuthorReactorKey",
                table: "FarmActivityComments");

            migrationBuilder.DropColumn(
                name: "AuthorReactorKey",
                table: "FarmActivityComments");
        }
    }
}
