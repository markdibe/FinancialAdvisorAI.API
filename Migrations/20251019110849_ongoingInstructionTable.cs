using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialAdvisorAI.API.Migrations
{
    /// <inheritdoc />
    public partial class ongoingInstructionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Instruction",
                table: "OngoingInstructions",
                newName: "TriggerType");

            migrationBuilder.AddColumn<string>(
                name: "Actions",
                table: "OngoingInstructions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionCount",
                table: "OngoingInstructions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InstructionText",
                table: "OngoingInstructions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExecutedAt",
                table: "OngoingInstructions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "OngoingInstructions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TriggerConditions",
                table: "OngoingInstructions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "OngoingInstructions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OngoingInstructionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActivityType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    TriggeredBy = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentActivities_OngoingInstructions_OngoingInstructionId",
                        column: x => x.OngoingInstructionId,
                        principalTable: "OngoingInstructions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgentActivities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivities_OngoingInstructionId",
                table: "AgentActivities",
                column: "OngoingInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivities_UserId",
                table: "AgentActivities",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentActivities");

            migrationBuilder.DropColumn(
                name: "Actions",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "ExecutionCount",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "InstructionText",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "LastExecutedAt",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "TriggerConditions",
                table: "OngoingInstructions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "OngoingInstructions");

            migrationBuilder.RenameColumn(
                name: "TriggerType",
                table: "OngoingInstructions",
                newName: "Instruction");
        }
    }
}
