using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class DashboardApprovalScopeTests
{
    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public void Department_head_queue_requires_explicit_email_assignment_for_facility_requests(string type)
    {
        var manager = new Gen_Employee { GUID = Guid.NewGuid(), Firstname = "Same", Lastname = "Name", Email = "head@example.invalid" };
        var report = new Gen_Employee { ReportingTo = manager.GUID, Email = "report@example.invalid" };
        var reservations = new[]
        {
            new GFMS_ReservationHeader { ReservationId = 1, ReservationType = type, Status = ReservationStatuses.DepartmentHeadApproval, ApproverEmail = manager.Email },
            new GFMS_ReservationHeader { ReservationId = 2, ReservationType = type, Status = ReservationStatuses.DepartmentHeadApproval, Approver = manager.Fullname },
            new GFMS_ReservationHeader { ReservationId = 3, ReservationType = type, Status = ReservationStatuses.DepartmentHeadApproval, RequestorEmail = report.Email },
            new GFMS_ReservationHeader { ReservationId = 4, ReservationType = type, Status = ReservationStatuses.Approved, ApproverEmail = manager.Email }
        }.AsQueryable();

        var result = reservations.ForAssignedApprovalRoles(new[] { ReservationRoles.DepartmentHead }, manager, new[] { report }.AsQueryable());

        Assert.Equal(new[] { 1 }, result.Select(reservation => reservation.ReservationId));
    }

    private static readonly Gen_Employee DepartmentHead = new()
    {
        Firstname = "Jerwin",
        Lastname = "Alimuin",
        Email = "jerwin@example.com",
        DepartmentId = 10
    };

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public void Legacy_manager_queue_belongs_to_admin_approver(string type)
    {
        var reservations = new[]
        {
            new GFMS_ReservationHeader { ReservationId = 1, ReservationType = type,
                Status = ReservationStatuses.AdminManagerApproval }
        }.AsQueryable();
        var employees = Array.Empty<Gen_Employee>().AsQueryable();
        Assert.Single(reservations.ForAssignedApprovalRoles(
            new[] { ReservationRoles.AdminApprover }, null, employees));
        Assert.Empty(reservations.ForAssignedApprovalRoles(
            new[] { ReservationRoles.AdminManager }, null, employees));
    }

    [Fact]
    public void ForDepartmentHead_OnlyIncludesAssignedReservationsFromOwnDepartment()
    {
        var reservations = new[]
        {
            Reservation(1, 10, "Jerwin Alimuin", null),
            Reservation(2, 10, null, "jerwin@example.com"),
            Reservation(3, 20, "Jerwin Alimuin", "jerwin@example.com"),
            Reservation(4, 10, "Another Department Head", "other@example.com")
        }.AsQueryable();

        var result = reservations.ForDepartmentHead(DepartmentHead).ToList();

        Assert.Equal(new[] { 1, 2 }, result.Select(r => r.ReservationId));
    }

    [Fact]
    public void ForDepartmentHead_WithoutDepartment_ReturnsNoReservations()
    {
        var employeeWithoutDepartment = new Gen_Employee
        {
            Firstname = "Jerwin",
            Lastname = "Alimuin",
            Email = "jerwin@example.com"
        };

        var result = new[]
        {
            Reservation(1, 10, "Jerwin Alimuin", "jerwin@example.com")
        }.AsQueryable().ForDepartmentHead(employeeWithoutDepartment);

        Assert.Empty(result);
    }

    [Fact]
    public void ForDepartmentHead_MatchesAuthenticatedEmailWhenEmployeeEmailDiffers()
    {
        var reservations = new[]
        {
            Reservation(1, 10, null, "jerwin.identity@example.com"),
            Reservation(2, 10, null, "other@example.com"),
            Reservation(3, 20, null, "jerwin.identity@example.com")
        }.AsQueryable();

        var result = reservations
            .ForDepartmentHead(DepartmentHead, "jerwin.identity@example.com")
            .ToList();

        Assert.Equal(new[] { 1 }, result.Select(r => r.ReservationId));
    }

    [Fact]
    public void ForAssignedApprovalRoles_DualRoleIncludesOwnDepartmentAndAdminManagerQueues()
    {
        var reservations = new[]
        {
            Reservation(1, 10, "Jerwin Alimuin", "jerwin@example.com", ReservationStatuses.DepartmentHeadApproval),
            Reservation(2, 20, "Another Head", "other@example.com", ReservationStatuses.DepartmentHeadApproval),
            Reservation(3, 20, null, null, ReservationStatuses.AdminManagerApproval),
            Reservation(4, 20, null, null, ReservationStatuses.AdminApproverApproval)
        }.AsQueryable();

        var result = reservations.ForAssignedApprovalRoles(
                new[] { ReservationRoles.AdminManager, ReservationRoles.DepartmentHead },
                DepartmentHead,
                Array.Empty<Gen_Employee>().AsQueryable(),
                "jerwin@example.com")
            .ToList();

        Assert.Equal(new[] { 1, 3 }, result.Select(r => r.ReservationId));
    }

    [Fact]
    public void ForAssignedApprovalRoles_IncludesDirectReportWhenLegacyApproverFieldsDiffer()
    {
        var manager = new Gen_Employee
        {
            GUID = Guid.NewGuid(),
            Email = "manager@example.com",
            DepartmentId = 10
        };
        var directReport = new Gen_Employee
        {
            GUID = Guid.NewGuid(),
            Email = "employee@example.com",
            DepartmentId = 10,
            ReportingTo = manager.GUID
        };
        var reservations = new[]
        {
            new GFMS_ReservationHeader
            {
                ReservationId = 1,
                Department = 10,
                RequestorEmail = directReport.Email,
                ApproverEmail = "legacy-head@example.com",
                Status = ReservationStatuses.DepartmentHeadApproval
            }
        }.AsQueryable();

        var result = reservations.ForAssignedApprovalRoles(
                new[] { ReservationRoles.AdminManager, ReservationRoles.DepartmentHead },
                manager,
                new[] { directReport }.AsQueryable(),
                manager.Email)
            .ToList();

        Assert.Equal(new[] { 1 }, result.Select(item => item.ReservationId));
    }

    [Fact]
    public void ForAssignedApprovalRoles_IncludesExplicitApproverAcrossDepartments()
    {
        var manager = new Gen_Employee
        {
            GUID = Guid.NewGuid(),
            Email = "manager@example.com",
            DepartmentId = 10
        };
        var reservations = new[]
        {
            new GFMS_ReservationHeader
            {
                ReservationId = 1,
                Department = 20,
                ApproverEmail = manager.Email,
                Status = ReservationStatuses.DepartmentHeadApproval
            }
        }.AsQueryable();

        var result = reservations.ForAssignedApprovalRoles(
                new[] { ReservationRoles.AdminManager, ReservationRoles.DepartmentHead },
                manager,
                Array.Empty<Gen_Employee>().AsQueryable(),
                manager.Email)
            .ToList();

        Assert.Equal(new[] { 1 }, result.Select(item => item.ReservationId));
    }

    private static GFMS_ReservationHeader Reservation(
        int id,
        int departmentId,
        string? approver,
        string? approverEmail,
        string? status = null) => new()
        {
            ReservationId = id,
            Department = departmentId,
            Approver = approver,
            ApproverEmail = approverEmail,
            Status = status
        };
}
