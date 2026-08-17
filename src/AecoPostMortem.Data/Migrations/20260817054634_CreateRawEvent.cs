using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AecoPostMortem.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateRawEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "raw_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    seq = table.Column<long>(type: "INTEGER", nullable: false),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    ts = table.Column<string>(type: "TEXT", nullable: false),
                    provider_version = table.Column<string>(type: "TEXT", nullable: false),
                    source_file = table.Column<string>(type: "TEXT", nullable: false),
                    byte_offset = table.Column<long>(type: "INTEGER", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_raw_session_seq",
                table: "raw_event",
                columns: new[] { "session_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_raw_type",
                table: "raw_event",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ux_raw_identity",
                table: "raw_event",
                columns: new[] { "source_file", "byte_offset", "content_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raw_event");
        }
    }
}
