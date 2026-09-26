using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityCommentRepliesAndReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCommentId",
                table: "FarmActivityComments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FarmActivityCommentReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReactorKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmActivityCommentReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmActivityCommentReactions_FarmActivityComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "FarmActivityComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FarmActivityComments_ParentCommentId",
                table: "FarmActivityComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmActivityCommentReactions_CommentId_ReactorKey_Emoji",
                table: "FarmActivityCommentReactions",
                columns: new[] { "CommentId", "ReactorKey", "Emoji" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FarmActivityComments_FarmActivityComments_ParentCommentId",
                table: "FarmActivityComments",
                column: "ParentCommentId",
                principalTable: "FarmActivityComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FarmActivityComments_FarmActivityComments_ParentCommentId",
                table: "FarmActivityComments");

            migrationBuilder.DropTable(
                name: "FarmActivityCommentReactions");

            migrationBuilder.DropIndex(
                name: "IX_FarmActivityComments_ParentCommentId",
                table: "FarmActivityComments");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "FarmActivityComments");
        }
    }
}
