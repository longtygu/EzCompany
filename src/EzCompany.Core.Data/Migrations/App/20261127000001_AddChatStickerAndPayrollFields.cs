using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
[Migration("20261127000001_AddChatStickerAndPayrollFields")]
public partial class AddChatStickerAndPayrollFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MessageType",
            table: "ChatMessages",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "Text");

        migrationBuilder.AddColumn<decimal>(
            name: "BaseSalaryVnd",
            table: "EmployeeProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "AllowanceVnd",
            table: "EmployeeProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<double>(
            name: "StandardWorkDaysPerMonth",
            table: "EmployeeProfiles",
            type: "REAL",
            nullable: false,
            defaultValue: 26.0);

        migrationBuilder.AddColumn<decimal>(
            name: "InsuranceDeductionVnd",
            table: "EmployeeProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<double>(
            name: "OvertimeHours",
            table: "AttendanceRecords",
            type: "REAL",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.AddColumn<double>(
            name: "WorkDayUnits",
            table: "AttendanceRecords",
            type: "REAL",
            nullable: false,
            defaultValue: 1.0);

        migrationBuilder.AddColumn<bool>(
            name: "IsManualEntry",
            table: "AttendanceRecords",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MessageType", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "BaseSalaryVnd", table: "EmployeeProfiles");
        migrationBuilder.DropColumn(name: "AllowanceVnd", table: "EmployeeProfiles");
        migrationBuilder.DropColumn(name: "StandardWorkDaysPerMonth", table: "EmployeeProfiles");
        migrationBuilder.DropColumn(name: "InsuranceDeductionVnd", table: "EmployeeProfiles");
        migrationBuilder.DropColumn(name: "OvertimeHours", table: "AttendanceRecords");
        migrationBuilder.DropColumn(name: "WorkDayUnits", table: "AttendanceRecords");
        migrationBuilder.DropColumn(name: "IsManualEntry", table: "AttendanceRecords");
    }
}
