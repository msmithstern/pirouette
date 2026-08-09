using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pirouette.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M1CoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DanceClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Style = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Level = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    MinimumAge = table.Column<int>(type: "integer", nullable: true),
                    MaximumAge = table.Column<int>(type: "integer", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceClasses", x => x.Id);
                    table.CheckConstraint("CK_DanceClass_AgeRangeOrdered", "\"MinimumAge\" IS NULL OR \"MaximumAge\" IS NULL OR \"MinimumAge\" <= \"MaximumAge\"");
                    table.CheckConstraint("CK_DanceClass_AgesNotNegative", "(\"MinimumAge\" IS NULL OR \"MinimumAge\" >= 0) AND (\"MaximumAge\" IS NULL OR \"MaximumAge\" >= 0)");
                    table.CheckConstraint("CK_DanceClass_CapacityPositive", "\"Capacity\" > 0");
                    table.CheckConstraint("CK_DanceClass_EndNotBeforeStart", "\"EndDate\" >= \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_DanceClasses_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DanceClasses_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DanceClasses_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrimaryPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PrimaryEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Households_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeetingPatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    DanceClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingPatterns", x => x.Id);
                    table.CheckConstraint("CK_MeetingPattern_DoesNotCrossMidnight", "EXTRACT(EPOCH FROM \"StartTime\") + EXTRACT(EPOCH FROM \"Duration\") <= 86400");
                    table.CheckConstraint("CK_MeetingPattern_DurationPositive", "\"Duration\" > INTERVAL '0'");
                    table.CheckConstraint("CK_MeetingPattern_DurationWithinLimit", "\"Duration\" <= INTERVAL '8 hours'");
                    table.ForeignKey(
                        name: "FK_MeetingPatterns_DanceClasses_DanceClassId",
                        column: x => x.DanceClassId,
                        principalTable: "DanceClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MeetingPatterns_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Members_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    DanceClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassAssignments", x => x.Id);
                    table.CheckConstraint("CK_ClassAssignment_UntilNotBeforeFrom", "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_ClassAssignments_DanceClasses_DanceClassId",
                        column: x => x.DanceClassId,
                        principalTable: "DanceClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassAssignments_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassAssignments_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberRoles", x => x.Id);
                    table.CheckConstraint("CK_MemberRole_UntilNotBeforeFrom", "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_MemberRoles_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemberRoles_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Term_EndNotBeforeStart",
                table: "Terms",
                sql: "\"EndDate\" >= \"StartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Room_CapacityPositive",
                table: "Rooms",
                sql: "\"Capacity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ClassAssignments_DanceClassId",
                table: "ClassAssignments",
                column: "DanceClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassAssignments_MemberId_DanceClassId",
                table: "ClassAssignments",
                columns: new[] { "MemberId", "DanceClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassAssignments_StudioId",
                table: "ClassAssignments",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_DanceClasses_RoomId",
                table: "DanceClasses",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_DanceClasses_StudioId_TermId",
                table: "DanceClasses",
                columns: new[] { "StudioId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_DanceClasses_TermId",
                table: "DanceClasses",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_Households_StudioId_Name",
                table: "Households",
                columns: new[] { "StudioId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingPatterns_DanceClassId",
                table: "MeetingPatterns",
                column: "DanceClassId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingPatterns_StudioId",
                table: "MeetingPatterns",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberRoles_MemberId_Role",
                table: "MemberRoles",
                columns: new[] { "MemberId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberRoles_StudioId_Role_EffectiveFrom",
                table: "MemberRoles",
                columns: new[] { "StudioId", "Role", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Members_HouseholdId",
                table: "Members",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_StudioId_LastName_FirstName",
                table: "Members",
                columns: new[] { "StudioId", "LastName", "FirstName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassAssignments");

            migrationBuilder.DropTable(
                name: "MeetingPatterns");

            migrationBuilder.DropTable(
                name: "MemberRoles");

            migrationBuilder.DropTable(
                name: "DanceClasses");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Term_EndNotBeforeStart",
                table: "Terms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Room_CapacityPositive",
                table: "Rooms");
        }
    }
}
