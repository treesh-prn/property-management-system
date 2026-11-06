using System.Security.Claims;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationAuthorizationServiceTests
{
    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Auto_approval_removes_unused_department_head_assignment_and_access(string type)
    {
        var service = CreateService();
        var owner = User("owner", "owner@example.invalid", ReservationRoles.AdminApprover);
        var head = User("head", "head@example.invalid", ReservationRoles.DepartmentHead, "Department Head");
        var reservation = Reservation("owner@example.invalid", type, ReservationStatuses.Draft);
        reservation.ApproverEmail = "head@example.invalid";
        reservation.Approver = "Department Head";

        reservation.Status = await service.GetSubmissionStatusAsync(reservation, await service.GetActorAsync(owner));

        Assert.Equal(ReservationStatuses.Approved, reservation.Status);
        Assert.Null(reservation.ApproverEmail);
        Assert.Null(reservation.Approver);
        foreach (var operation in new[] { ReservationOperation.Read, ReservationOperation.Approve, ReservationOperation.Reject })
            Assert.False((await service.AuthorizeAsync(head, reservation, operation)).Succeeded);
        Assert.True((await service.AuthorizeAsync(owner, reservation, ReservationOperation.Read)).Succeeded);
        Assert.True((await service.AuthorizeAsync(
            User("admin", "admin@example.invalid", ReservationRoles.AdminManager), reservation, ReservationOperation.Read)).Succeeded);
    }

    [Theory]
    [InlineData("FN", ReservationStatuses.DepartmentHeadApproval)]
    [InlineData("GH", ReservationStatuses.DepartmentHeadApproval)]
    [InlineData("FN", ReservationStatuses.Approved)]
    [InlineData("GH", ReservationStatuses.Approved)]
    public async Task Genuine_department_head_assignment_retains_review_and_history_access(string type, string status)
    {
        var reservation = Reservation("owner@example.invalid", type, status);
        reservation.ApproverEmail = "head@example.invalid";
        var result = await CreateService().AuthorizeAsync(
            User("head", "head@example.invalid", ReservationRoles.DepartmentHead), reservation, ReservationOperation.Read);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Matching_display_name_alone_cannot_grant_department_head_access(string type)
    {
        var reservation = Reservation("owner@example.invalid", type, ReservationStatuses.DepartmentHeadApproval);
        reservation.Approver = "Same Name ";
        reservation.ApproverEmail = "another-head@example.invalid";
        var head = User("head", "head@example.invalid", ReservationRoles.DepartmentHead, "Same Name");
        foreach (var operation in new[] { ReservationOperation.Read, ReservationOperation.Approve, ReservationOperation.Reject })
            Assert.False((await CreateService().AuthorizeAsync(head, reservation, operation)).Succeeded);
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Department_head_cannot_read_someone_elses_draft_even_with_stale_assignment(string type)
    {
        var reservation = Reservation("owner@example.invalid", type, ReservationStatuses.Draft);
        reservation.ApproverEmail = "head@example.invalid";
        Assert.False((await CreateService().AuthorizeAsync(
            User("head", "head@example.invalid", ReservationRoles.DepartmentHead), reservation, ReservationOperation.Read)).Succeeded);
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public void Admin_approver_submission_is_automatically_approved(string type)
    {
        Assert.Equal(ReservationStatuses.Approved,
            ReservationAuthorizationService.GetSubmissionStatus(
                ReservationRoles.AdminApprover, "ADMIN", "Administration", type));
    }

    [Fact]
    public async Task Admin_approver_can_assign_rooms_to_own_approved_guesthouse_without_reapproval()
    {
        var owner = User("owner", "owner@example.invalid", ReservationRoles.AdminApprover);
        var reservation = Reservation("owner@example.invalid", "GH", ReservationStatuses.Approved);
        var result = await CreateService().AuthorizeAsync(owner, reservation, ReservationOperation.Assign);
        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatuses.Approved, result.NextStatus);
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor, "owner@example.invalid", "GH")]
    [InlineData(ReservationRoles.AdminManager, "owner@example.invalid", "GH")]
    [InlineData(ReservationRoles.AdminApprover, "other@example.invalid", "GH")]
    [InlineData(ReservationRoles.AdminApprover, "owner@example.invalid", "FN")]
    public async Task Approved_room_assignment_exception_is_restricted_to_admin_approver_guesthouse_owner(
        string role, string email, string type)
    {
        var user = User("user", email, role);
        var result = await CreateService().AuthorizeAsync(user,
            Reservation("owner@example.invalid", type, ReservationStatuses.Approved), ReservationOperation.Assign);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Admin_manager_cannot_approve_reject_or_assign_at_final_review(string type)
    {
        var service = CreateService();
        var manager = User("manager", "manager@example.invalid", ReservationRoles.AdminManager);
        foreach (var status in new[] { ReservationStatuses.AdminApproverApproval, ReservationStatuses.AdminManagerApproval })
        foreach (var operation in new[] { ReservationOperation.Approve, ReservationOperation.Reject, ReservationOperation.Assign })
        {
            var result = await service.AuthorizeAsync(manager,
                Reservation("owner@example.invalid", type, status), operation);
            Assert.False(result.Succeeded);
            Assert.Equal(ReservationAuthorizationFailure.Forbidden, result.Failure);
        }
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor, "IT", "Information Technology", ReservationStatuses.DepartmentHeadApproval)]
    [InlineData(ReservationRoles.DepartmentHead, "IT", "Information Technology", ReservationStatuses.AdminApproverApproval)]
    [InlineData(ReservationRoles.Requestor, "ADMIN", "Administrative Services", ReservationStatuses.AdminApproverApproval)]
    [InlineData(ReservationRoles.Requestor, "ADM", "Administration", ReservationStatuses.AdminApproverApproval)]
    [InlineData(ReservationRoles.Requestor, " admin ", null, ReservationStatuses.AdminApproverApproval)]
    [InlineData(ReservationRoles.AdminApprover, "ADMIN", "Administration", ReservationStatuses.AdminManagerApproval)]
    public void Submission_routes_by_requestor_role_and_department(
        string role, string code, string? name, string expected)
    {
        Assert.Equal(expected, ReservationAuthorizationService.GetSubmissionStatus(role, code, name));
    }

    [Theory]
    [InlineData(ReservationStatuses.Draft)]
    [InlineData(ReservationStatuses.DepartmentHeadApproval)]
    [InlineData(ReservationStatuses.AdminApproverApproval)]
    [InlineData(ReservationStatuses.AdminManagerApproval)]
    [InlineData(ReservationStatuses.Reserved)]
    [InlineData(ReservationStatuses.Approved)]
    [InlineData(ReservationStatuses.Cancelled)]
    public async Task Owner_can_cancel_or_repeat_cancel_for_supported_statuses(string status)
    {
        var service = CreateService();
        var owner = User("owner", "owner@example.invalid", ReservationRoles.Requestor);
        var reservation = Reservation("owner@example.invalid", "FN", status);

        var result = await service.AuthorizeAsync(owner, reservation, ReservationOperation.Cancel);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Requestor_B_cannot_read_or_mutate_requestor_A_record()
    {
        var service = CreateService();
        var requestorB = User("user-b", "b@example.invalid", ReservationRoles.Requestor);
        var reservation = Reservation("a@example.invalid", "FN", ReservationStatuses.Draft);

        foreach (var operation in new[]
        {
            ReservationOperation.Read,
            ReservationOperation.CreateDetails,
            ReservationOperation.Edit,
            ReservationOperation.Delete,
            ReservationOperation.Cancel,
            ReservationOperation.Assign,
            ReservationOperation.Approve,
            ReservationOperation.Reject
        })
        {
            var result = await service.AuthorizeAsync(requestorB, reservation, operation);
            Assert.False(result.Succeeded);
            Assert.Equal(ReservationAuthorizationFailure.Forbidden, result.Failure);
        }
    }

    [Fact]
    public async Task Owner_can_edit_and_cancel_a_request_returned_for_rescheduling()
    {
        var service = CreateService();
        var owner = User("owner", "owner@example.invalid", ReservationRoles.Requestor);
        var reservation = Reservation(
            "owner@example.invalid", "FN", ReservationStatuses.ForRescheduling);

        Assert.True((await service.AuthorizeAsync(
            owner, reservation, ReservationOperation.Edit)).Succeeded);
        Assert.True((await service.AuthorizeAsync(
            owner, reservation, ReservationOperation.Cancel)).Succeeded);
        Assert.False((await service.AuthorizeAsync(
            owner, reservation, ReservationOperation.Approve)).Succeeded);
    }

    [Theory]
    [InlineData("FN", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead)]
    [InlineData("FN", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover)]
    [InlineData("FN", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminApprover)]
    [InlineData("GH", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead)]
    [InlineData("GH", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover)]
    [InlineData("GH", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminApprover)]
    [InlineData("DRM", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead)]
    [InlineData("DRM", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminManager)]
    [InlineData("DRM", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover)]
    public async Task Approval_fails_for_every_incorrect_active_role(
        string type,
        string status,
        string expectedRole)
    {
        var service = CreateService();
        var reservation = Reservation("owner@example.invalid", type, status);
        reservation.ApproverEmail = "staff@example.invalid";

        foreach (var incorrectRole in AllRoles.Where(role => role != expectedRole))
        {
            var user = User("staff", "staff@example.invalid", incorrectRole, "Staff User");
            var result = await service.AuthorizeAsync(user, reservation, ReservationOperation.Approve);
            Assert.False(result.Succeeded);
            Assert.Equal(ReservationAuthorizationFailure.Forbidden, result.Failure);
        }
    }

    [Theory]
    [InlineData("FN", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead, ReservationStatuses.AdminApproverApproval)]
    [InlineData("FN", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover, ReservationStatuses.Approved)]
    [InlineData("FN", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminApprover, ReservationStatuses.Approved)]
    [InlineData("GH", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead, ReservationStatuses.AdminApproverApproval)]
    [InlineData("GH", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover, ReservationStatuses.Approved)]
    [InlineData("GH", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminApprover, ReservationStatuses.Approved)]
    [InlineData("DRM", ReservationStatuses.DepartmentHeadApproval, ReservationRoles.DepartmentHead, ReservationStatuses.AdminApproverApproval)]
    [InlineData("DRM", ReservationStatuses.AdminApproverApproval, ReservationRoles.AdminApprover, ReservationStatuses.AdminManagerApproval)]
    [InlineData("DRM", ReservationStatuses.AdminManagerApproval, ReservationRoles.AdminManager, ReservationStatuses.Approved)]
    public async Task Correct_active_role_advances_only_the_expected_transition(
        string type,
        string status,
        string expectedRole,
        string nextStatus)
    {
        var service = CreateService();
        var user = User("staff", "staff@example.invalid", expectedRole, "Staff User");
        var reservation = Reservation("owner@example.invalid", type, status);
        reservation.ApproverEmail = "staff@example.invalid";

        var result = await service.AuthorizeAsync(user, reservation, ReservationOperation.Approve);

        Assert.True(result.Succeeded);
        Assert.Equal(status, result.ExpectedStatus);
        Assert.Equal(nextStatus, result.NextStatus);
    }

    [Fact]
    public async Task Department_head_may_only_approve_their_assigned_request()
    {
        var service = CreateService();
        var departmentHead = User(
            "head-b", "head-b@example.invalid", ReservationRoles.DepartmentHead, "Head B");
        var reservation = Reservation(
            "owner@example.invalid", "FN", ReservationStatuses.DepartmentHeadApproval);
        reservation.ApproverEmail = "head-a@example.invalid";
        reservation.Approver = "Head A";

        var result = await service.AuthorizeAsync(
            departmentHead, reservation, ReservationOperation.Approve);

        Assert.False(result.Succeeded);
        Assert.Equal(ReservationAuthorizationFailure.Forbidden, result.Failure);
    }

    [Fact]
    public async Task Stale_department_head_approval_fails_after_status_changed()
    {
        var service = CreateService();
        var departmentHead = User(
            "head", "head@example.invalid", ReservationRoles.DepartmentHead, "Head User");
        var reservation = Reservation(
            "owner@example.invalid", "FN", ReservationStatuses.AdminApproverApproval);
        reservation.ApproverEmail = "head@example.invalid";

        var result = await service.AuthorizeAsync(
            departmentHead, reservation, ReservationOperation.Approve);

        Assert.False(result.Succeeded);
        Assert.Equal(ReservationAuthorizationFailure.Forbidden, result.Failure);
    }

    [Fact]
    public async Task System_administrator_has_read_only_reservation_privileges()
    {
        var service = CreateService();
        var administrator = User(
            "sysadmin", "sysadmin@example.invalid", ReservationRoles.SystemAdmin, "System Admin");
        var reservation = Reservation(
            "owner@example.invalid", "GH", ReservationStatuses.AdminApproverApproval);

        Assert.True((await service.AuthorizeAsync(
            administrator, reservation, ReservationOperation.Read)).Succeeded);

        foreach (var operation in new[]
        {
            ReservationOperation.CreateDetails,
            ReservationOperation.Edit,
            ReservationOperation.Delete,
            ReservationOperation.Cancel,
            ReservationOperation.Assign,
            ReservationOperation.Approve,
            ReservationOperation.Reject
        })
        {
            Assert.False((await service.AuthorizeAsync(administrator, reservation, operation)).Succeeded);
        }
    }

    private static ReservationAuthorizationService CreateService() =>
        new(new FakeRoleService());

    private static readonly string[] AllRoles =
    {
        ReservationRoles.Requestor,
        ReservationRoles.DepartmentHead,
        ReservationRoles.AdminApprover,
        ReservationRoles.AdminManager,
        ReservationRoles.SystemAdmin
    };

    private static ClaimsPrincipal User(
        string id,
        string email,
        string activeRole,
        string employeeName = "Employee User")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email),
            new Claim("GFMS.Active Role", activeRole),
            new Claim("test:employee-name", employeeName)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static GFMS_ReservationHeader Reservation(string ownerEmail, string type, string status) =>
        new()
        {
            ReservationId = 42,
            RequestorEmail = ownerEmail,
            ReservationType = type,
            Status = status
        };

    private sealed class FakeRoleService : IRoleService
    {
        public Task<IEnumerable<string>> GetRolesAsync(ClaimsPrincipal user) =>
            Task.FromResult<IEnumerable<string>>(new[] { GetRole(user) });

        public Task<string> GetActiveRoleAsync(ClaimsPrincipal user) =>
            Task.FromResult(GetRole(user));

        public Task<string> SetDefaultActiveRoleAsync(ApplicationUser user) =>
            Task.FromResult(string.Empty);

        public Task<Gen_Employee?> GetUserName(ClaimsPrincipal user) =>
            Task.FromResult<Gen_Employee?>(new Gen_Employee
            {
                UserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                Email = user.FindFirstValue(ClaimTypes.Email),
                Firstname = user.FindFirstValue("test:employee-name"),
                Lastname = string.Empty,
                DepartmentId = 1,
                CompanyId = 1
            });

        private static string GetRole(ClaimsPrincipal user) =>
            user.FindFirstValue("GFMS.Active Role") ?? string.Empty;
    }
}
