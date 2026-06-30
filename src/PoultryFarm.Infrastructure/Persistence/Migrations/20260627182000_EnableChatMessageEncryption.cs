using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoultryFarm.Infrastructure.Persistence.Migrations;

public partial class EnableChatMessageEncryption : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Body",
            table: "ChatMessages",
            type: "nvarchar(max)",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(4000)",
            oldMaxLength: 4000);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Body",
            table: "ChatMessages",
            type: "nvarchar(4000)",
            maxLength: 4000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");
    }
}
