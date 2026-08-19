using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AecoPostMortem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemPromptText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_prompt_text",
                columns: table => new
                {
                    content_hash = table.Column<string>(type: "TEXT", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_prompt_text", x => x.content_hash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_prompt_text");
        }
    }
}
