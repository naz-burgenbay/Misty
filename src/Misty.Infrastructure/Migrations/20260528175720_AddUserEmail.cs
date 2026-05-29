using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "users",
                table: "User",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with a deterministic, unique placeholder so the unique index below can be created. Real users will overwrite this on first profile edit.
            migrationBuilder.Sql(
                "UPDATE [users].[User] SET [Email] = CONCAT(N'legacy+', LOWER([Username]), N'@local.invalid') WHERE [Email] = N'';");

            migrationBuilder.CreateIndex(
                name: "UX_User_Email",
                schema: "users",
                table: "User",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_User_Email",
                schema: "users",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "users",
                table: "User");
        }
    }
}
