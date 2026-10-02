using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trainer.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutOriginalDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalDate",
                table: "PlannedWorkouts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalDate",
                table: "PlannedWorkouts");
        }
    }
}
