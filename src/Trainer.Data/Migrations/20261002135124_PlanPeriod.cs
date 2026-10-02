using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trainer.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlanPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlanEndDate",
                table: "Athletes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlanStartDate",
                table: "Athletes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanEndDate",
                table: "Athletes");

            migrationBuilder.DropColumn(
                name: "PlanStartDate",
                table: "Athletes");
        }
    }
}
