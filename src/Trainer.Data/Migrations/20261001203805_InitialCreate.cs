using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trainer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    DurationSec = table.Column<int>(type: "INTEGER", nullable: false),
                    DistanceKm = table.Column<double>(type: "REAL", nullable: true),
                    AvgPower = table.Column<double>(type: "REAL", nullable: true),
                    NormalizedPower = table.Column<double>(type: "REAL", nullable: true),
                    AvgHr = table.Column<double>(type: "REAL", nullable: true),
                    AvgCadence = table.Column<double>(type: "REAL", nullable: true),
                    Tss = table.Column<double>(type: "REAL", nullable: false),
                    IntensityFactor = table.Column<double>(type: "REAL", nullable: true),
                    HrBasedTss = table.Column<bool>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    StravaId = table.Column<long>(type: "INTEGER", nullable: true),
                    PlannedWorkoutId = table.Column<int>(type: "INTEGER", nullable: true),
                    PowerCurve = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Athletes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    WeeklyHours = table.Column<double>(type: "REAL", nullable: false),
                    TrainingDays = table.Column<string>(type: "TEXT", nullable: false),
                    LongRideDay = table.Column<string>(type: "TEXT", nullable: false),
                    NoRaceGoal = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultIndoor = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThresholdHr = table.Column<int>(type: "INTEGER", nullable: true),
                    PlanAnchor = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EdgePath = table.Column<string>(type: "TEXT", nullable: true),
                    WatchFolder = table.Column<string>(type: "TEXT", nullable: true),
                    StravaClientId = table.Column<string>(type: "TEXT", nullable: true),
                    StravaClientSecret = table.Column<string>(type: "TEXT", nullable: true),
                    StravaRefreshToken = table.Column<string>(type: "TEXT", nullable: true),
                    StravaAccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    StravaTokenExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StravaLastSyncUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Athletes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockedDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLoads",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Tss = table.Column<double>(type: "REAL", nullable: false),
                    Ctl = table.Column<double>(type: "REAL", nullable: false),
                    Atl = table.Column<double>(type: "REAL", nullable: false),
                    Tsb = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLoads", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "FtpHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Watts = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FtpHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedWorkouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    IsKey = table.Column<bool>(type: "INTEGER", nullable: false),
                    Steps = table.Column<string>(type: "TEXT", nullable: false),
                    DurationSec = table.Column<int>(type: "INTEGER", nullable: false),
                    Tss = table.Column<double>(type: "REAL", nullable: false),
                    IntensityFactor = table.Column<double>(type: "REAL", nullable: false),
                    Indoor = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Superseded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedWorkouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TargetRaceId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    DurationHours = table.Column<double>(type: "REAL", nullable: false),
                    Intensity = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Intervals = table.Column<string>(type: "TEXT", nullable: false),
                    MinLevel = table.Column<string>(type: "TEXT", nullable: false),
                    MaxLevel = table.Column<string>(type: "TEXT", nullable: false),
                    BuiltIn = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanWeeks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", nullable: false),
                    WeekType = table.Column<string>(type: "TEXT", nullable: false),
                    TargetHours = table.Column<double>(type: "REAL", nullable: false),
                    TargetTss = table.Column<double>(type: "REAL", nullable: false),
                    PlannedCtl = table.Column<double>(type: "REAL", nullable: false),
                    RaceId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanWeeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanWeeks_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_StartTime",
                table: "Activities",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_StravaId",
                table: "Activities",
                column: "StravaId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDays_Date",
                table: "BlockedDays",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FtpHistory_Date",
                table: "FtpHistory",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedWorkouts_Date_Superseded",
                table: "PlannedWorkouts",
                columns: new[] { "Date", "Superseded" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanWeeks_PlanId",
                table: "PlanWeeks",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Races_Date",
                table: "Races",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplates_Name",
                table: "WorkoutTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Athletes");

            migrationBuilder.DropTable(
                name: "BlockedDays");

            migrationBuilder.DropTable(
                name: "DailyLoads");

            migrationBuilder.DropTable(
                name: "FtpHistory");

            migrationBuilder.DropTable(
                name: "PlannedWorkouts");

            migrationBuilder.DropTable(
                name: "PlanWeeks");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropTable(
                name: "WorkoutTemplates");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
