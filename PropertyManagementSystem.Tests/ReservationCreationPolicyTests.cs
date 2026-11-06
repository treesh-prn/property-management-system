using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationCreationPolicyTests
{
    [Fact]
    public void Admin_approver_without_employee_can_book_using_account_identity_and_selected_cost_center()
    {
        var actor = Actor(ReservationRoles.AdminApprover) with
        {
            EmployeeName = "", EmployeeEmail = "", CompanyId = null, DepartmentId = null
        };

        var details = ReservationCreationPolicy.Resolve(actor, 4, 18);

        Assert.NotNull(details);
        Assert.Equal("Account Approver", details!.Requestor);
        Assert.Equal("account@example.invalid", details.RequestorEmail);
        Assert.Equal(4, details.CompanyId);
        Assert.Equal(18, details.DepartmentId);
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.DepartmentHead)]
    [InlineData(ReservationRoles.AdminManager)]
    [InlineData(ReservationRoles.SystemAdmin)]
    public void Other_roles_cannot_override_employee_cost_center_even_with_admin_role_assigned(string role)
    {
        var actor = Actor(role) with { AssignedRoles = new[] { role, ReservationRoles.AdminApprover } };

        var details = ReservationCreationPolicy.Resolve(actor, 99, 999);

        Assert.False(ReservationCreationPolicy.CanChooseCostCenter(actor));
        Assert.NotNull(details);
        Assert.Equal(1, details!.CompanyId);
        Assert.Equal(2, details.DepartmentId);
        Assert.Equal("Employee Name", details.Requestor);
        Assert.Equal("employee@example.invalid", details.RequestorEmail);
    }

    [Fact]
    public void Admin_active_role_alone_does_not_grant_free_selection()
    {
        var actor = Actor(ReservationRoles.AdminApprover) with
        {
            AssignedRoles = new[] { ReservationRoles.Requestor }
        };
        var details = ReservationCreationPolicy.Resolve(actor, 99, 999);
        Assert.False(ReservationCreationPolicy.CanChooseCostCenter(actor));
        Assert.Equal(1, details!.CompanyId);
        Assert.Equal(2, details.DepartmentId);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    [InlineData(1, -1)]
    public void Admin_must_select_both_company_and_department(int company, int department)
    {
        Assert.Null(ReservationCreationPolicy.Resolve(Actor(ReservationRoles.AdminApprover), company, department));
    }

    [Fact]
    public void Other_roles_without_employee_profile_still_cannot_book_without_assigned_cost_center()
    {
        var actor = Actor(ReservationRoles.Requestor) with
        {
            EmployeeName = "", EmployeeEmail = "", CompanyId = null, DepartmentId = null
        };
        Assert.Null(ReservationCreationPolicy.Resolve(actor, 4, 18));
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.DepartmentHead)]
    [InlineData(ReservationRoles.AdminApprover)]
    [InlineData(ReservationRoles.AdminManager)]
    [InlineData(ReservationRoles.SystemAdmin)]
    public void Missing_employee_name_displays_account_username_for_every_role(string role)
    {
        var actor = Actor(role) with { EmployeeName = "", AccountName = "jane.approver" };

        Assert.Equal("jane.approver", ReservationCreationPolicy.GetRequestorName(actor));
    }

    [Fact]
    public void Employee_full_name_takes_precedence_over_account_username()
    {
        Assert.Equal("Employee Name", ReservationCreationPolicy.GetRequestorName(Actor(ReservationRoles.AdminApprover)));
    }

    [Fact]
    public async Task Cost_center_validation_rejects_missing_records_and_cross_company_departments()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsCreationPolicy_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<DAL>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true")
            .Options;
        await using var context = new DAL(options);
        try
        {
            await context.Database.EnsureCreatedAsync();
            var company = new Gen_Company { Company = "Company A", CompanyCode = "A" };
            var otherCompany = new Gen_Company { Company = "Company B", CompanyCode = "B" };
            context.Companies.AddRange(company, otherCompany);
            await context.SaveChangesAsync();
            var department = new Gen_Department { CompanyId = company.Id, DepartmentName = "Operations" };
            context.Departments.Add(department);
            await context.SaveChangesAsync();

            var details = new ReservationCreationDetails("Approver", "approver@example.invalid", company.Id, department.Id);
            Assert.True(await ReservationCreationPolicy.IsValidCostCenterAsync(context, details));
            Assert.False(await ReservationCreationPolicy.IsValidCostCenterAsync(context, details with { CompanyId = otherCompany.Id }));
            Assert.False(await ReservationCreationPolicy.IsValidCostCenterAsync(context, details with { CompanyId = int.MaxValue }));
            Assert.False(await ReservationCreationPolicy.IsValidCostCenterAsync(context, details with { DepartmentId = int.MaxValue }));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static ReservationActor Actor(string role) => new(
        "account-id", "account@example.invalid", role, "Employee Name", "employee@example.invalid",
        2, 1, new[] { role }, Array.Empty<string>(), "Account Approver");
}
