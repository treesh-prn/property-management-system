using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class ReservationRoles
    {
        public const string Requestor = "GFMS.Requestor";
        public const string DepartmentHead = "GFMS.Department Head";
        public const string AdminApprover = "GFMS.Admin Approver";
        public const string AdminManager = "GFMS.Admin Manager";
        public const string SystemAdmin = "GFMS.System Admin";
    }

    public static class ReservationStatuses
    {
        public const string Draft = "DRAFT";
        public const string DepartmentHeadApproval = "FOR DEPARTMENT HEAD'S APPROVAL";
        public const string AdminApproverApproval = "FOR ADMIN APPROVER'S APPROVAL";
        public const string AdminManagerApproval = "FOR ADMIN MANAGER'S APPROVAL";
        public const string Reserved = "RESERVED";
        public const string Approved = "APPROVED";
        public const string Disapproved = "DISAPPROVED";
        public const string Cancelled = "CANCELLED";
        public const string Deleted = "DELETED";
        public const string ForRescheduling = "FOR RESCHEDULING";
        public const string Expired = "EXPIRED";
    }

    public enum ReservationOperation
    {
        Read,
        CreateDetails,
        Edit,
        Delete,
        Cancel,
        Assign,
        Approve,
        Reject
    }

    public enum ReservationAuthorizationFailure
    {
        None,
        Forbidden,
        InvalidState
    }

    public sealed record ReservationActor(
        string UserId,
        string Email,
        string ActiveRole,
        string EmployeeName,
        string EmployeeEmail,
        int? DepartmentId,
        int? CompanyId,
        IReadOnlyCollection<string> AssignedRoles,
        IReadOnlyCollection<string> ManagedEmployeeEmails);

    public sealed record ReservationAuthorizationResult(
        bool Succeeded,
        ReservationAuthorizationFailure Failure,
        string? ExpectedStatus = null,
        string? NextStatus = null)
    {
        public static ReservationAuthorizationResult Allow(string? expectedStatus = null, string? nextStatus = null) =>
            new(true, ReservationAuthorizationFailure.None, expectedStatus, nextStatus);

        public static ReservationAuthorizationResult Forbid() =>
            new(false, ReservationAuthorizationFailure.Forbidden);

        public static ReservationAuthorizationResult InvalidState(string? expectedStatus = null) =>
            new(false, ReservationAuthorizationFailure.InvalidState, expectedStatus);
    }

    public interface IReservationAuthorizationService
    {
        Task<ReservationActor> GetActorAsync(ClaimsPrincipal user);
        Task<bool> CanCreateAsync(ClaimsPrincipal user);
        Task<bool> ShouldBypassDepartmentHeadAsync(GFMS_ReservationHeader reservation);
        Task<string> GetSubmissionStatusAsync(GFMS_ReservationHeader reservation);
        Task<string> GetSubmissionStatusAsync(GFMS_ReservationHeader reservation, ReservationActor actor);
        Task<ReservationAuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            GFMS_ReservationHeader reservation,
            ReservationOperation operation);
    }

    public sealed class ReservationAuthorizationService : IReservationAuthorizationService
    {
        private readonly IRoleService _roleService;
        private readonly DAL? _context;
        private readonly UserManager<ApplicationUser>? _userManager;

        public ReservationAuthorizationService(
            IRoleService roleService,
            DAL? context = null,
            UserManager<ApplicationUser>? userManager = null)
        {
            _roleService = roleService;
            _context = context;
            _userManager = userManager;
        }

        public async Task<ReservationActor> GetActorAsync(ClaimsPrincipal user)
        {
            var employee = await _roleService.GetUserName(user);
            var assignedRoles = (await _roleService.GetRolesAsync(user)).ToArray();
            var managedEmployeeEmails = employee == null || _context == null
                ? Array.Empty<string>()
                : await _context.Employees
                    .AsNoTracking()
                    .Where(candidate =>
                        candidate.ReportingTo == employee.GUID &&
                        candidate.Email != null)
                    .Select(candidate => candidate.Email!)
                    .ToArrayAsync();
            var email = user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("email")
                ?? user.Identity?.Name
                ?? string.Empty;

            return new ReservationActor(
                user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                email,
                await _roleService.GetActiveRoleAsync(user),
                employee?.Fullname ?? string.Empty,
                employee?.Email ?? string.Empty,
                employee?.DepartmentId,
                employee?.CompanyId,
                assignedRoles,
                managedEmployeeEmails);
        }

        public async Task<bool> CanCreateAsync(ClaimsPrincipal user)
        {
            var actor = await GetActorAsync(user);
            return actor.AssignedRoles.Any(role => role is
                ReservationRoles.Requestor or
                ReservationRoles.DepartmentHead or
                ReservationRoles.AdminApprover or
                ReservationRoles.AdminManager);
        }

        public static string GetSubmissionStatus(string activeRole, string? departmentCode, string? departmentName, string? reservationType = null)
        {
            var isAdmin = new[] { departmentCode, departmentName }.Any(value =>
                string.Equals(value?.Trim(), "ADMIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value?.Trim(), "ADMINISTRATION", StringComparison.OrdinalIgnoreCase));
            if (activeRole == ReservationRoles.DepartmentHead ||
                (activeRole == ReservationRoles.Requestor && isAdmin))
                return ReservationStatuses.AdminApproverApproval;
            if (activeRole == ReservationRoles.AdminApprover)
                return reservationType is "FN" or "GH"
                    ? ReservationStatuses.AdminApproverApproval
                    : ReservationStatuses.AdminManagerApproval;
            return ReservationStatuses.DepartmentHeadApproval;
        }

        public async Task<string> GetSubmissionStatusAsync(
            GFMS_ReservationHeader reservation, ReservationActor actor)
        {
            var department = _context == null ? null : await _context.Departments
                .AsNoTracking().FirstOrDefaultAsync(d => d.Id == actor.DepartmentId);
            var status = GetSubmissionStatus(actor.ActiveRole, department?.DepartmentCode, department?.DepartmentName, reservation.ReservationType);
            var head = _context == null || department?.DepartmentHead == null ? null :
                await _context.Employees.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.GUID == department.DepartmentHead);
            reservation.ApproverEmail = head?.Email;
            reservation.Approver = head == null ? null : head.Firstname + " " + head.Lastname;
            return status;
        }

        public async Task<bool> ShouldBypassDepartmentHeadAsync(GFMS_ReservationHeader reservation)
        {
            if (reservation.ReservationType is "FN" or "GH") return false;

            if (_userManager == null || string.IsNullOrWhiteSpace(reservation.ApproverEmail))
            {
                return false;
            }

            var approverEmail = reservation.ApproverEmail.Trim();
            var approverUser = await _userManager.FindByEmailAsync(approverEmail);
            if (approverUser == null && _context != null)
            {
                var approverUserId = await _context.Employees
                    .AsNoTracking()
                    .Where(employee => employee.Email == approverEmail)
                    .Select(employee => employee.UserId)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(approverUserId))
                {
                    approverUser = await _userManager.FindByIdAsync(approverUserId);
                }
            }

            return approverUser != null &&
                await _userManager.IsInRoleAsync(approverUser, ReservationRoles.AdminManager);
        }

        public async Task<string> GetSubmissionStatusAsync(GFMS_ReservationHeader reservation) =>
            await ShouldBypassDepartmentHeadAsync(reservation)
                ? ReservationStatuses.AdminApproverApproval
                : ReservationStatuses.DepartmentHeadApproval;

        public async Task<ReservationAuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            GFMS_ReservationHeader reservation,
            ReservationOperation operation)
        {
            var actor = await GetActorAsync(user);
            var isOwner = IsOwner(reservation, actor);

            return operation switch
            {
                ReservationOperation.Read => CanRead(reservation, actor, isOwner)
                    ? ReservationAuthorizationResult.Allow()
                    : ReservationAuthorizationResult.Forbid(),
                ReservationOperation.CreateDetails => AuthorizeOwnerState(
                    isOwner, reservation.Status, ReservationStatuses.Draft),
                ReservationOperation.Edit => AuthorizeOwnerState(
                    isOwner,
                    reservation.Status,
                    ReservationStatuses.Draft,
                    ReservationStatuses.DepartmentHeadApproval,
                    ReservationStatuses.ForRescheduling),
                ReservationOperation.Delete => AuthorizeOwnerState(
                    isOwner, reservation.Status, ReservationStatuses.Draft),
                ReservationOperation.Cancel => AuthorizeOwnerState(
                    isOwner,
                    reservation.Status,
                    ReservationStatuses.Draft,
                    ReservationStatuses.DepartmentHeadApproval,
                    ReservationStatuses.AdminApproverApproval,
                    ReservationStatuses.AdminManagerApproval,
                    ReservationStatuses.Reserved,
                    ReservationStatuses.Approved,
                    ReservationStatuses.ForRescheduling,
                    ReservationStatuses.Cancelled),
                ReservationOperation.Assign => AuthorizeAssignment(reservation, actor),
                ReservationOperation.Approve => AuthorizeWorkflowTransition(reservation, actor, false),
                ReservationOperation.Reject => AuthorizeWorkflowTransition(reservation, actor, true),
                _ => ReservationAuthorizationResult.Forbid()
            };
        }

        private static bool IsOwner(GFMS_ReservationHeader reservation, ReservationActor actor)
        {
            return ReservationOwnership.IsOwner(
                reservation,
                actor.Email,
                actor.EmployeeEmail);
        }

        private static bool CanRead(
            GFMS_ReservationHeader reservation,
            ReservationActor actor,
            bool isOwner)
        {
            if (isOwner || actor.AssignedRoles.Contains(ReservationRoles.SystemAdmin))
            {
                return true;
            }

            if (actor.AssignedRoles.Contains(ReservationRoles.DepartmentHead))
            {
                return Matches(reservation.ApproverEmail, actor.Email) ||
                    Matches(reservation.ApproverEmail, actor.EmployeeEmail) ||
                    Matches(reservation.Approver, actor.EmployeeName) ||
                    (reservation.ReservationType != "FN" && reservation.ReservationType != "GH" && IsManagedRequest(reservation, actor));
            }

            return actor.AssignedRoles.Contains(ReservationRoles.AdminApprover) ||
                actor.AssignedRoles.Contains(ReservationRoles.AdminManager);
        }

        private static ReservationAuthorizationResult AuthorizeOwnerState(
            bool isOwner,
            string? actualStatus,
            params string[] allowedStatuses)
        {
            if (!isOwner)
            {
                return ReservationAuthorizationResult.Forbid();
            }

            return allowedStatuses.Contains(actualStatus, StringComparer.Ordinal)
                ? ReservationAuthorizationResult.Allow(actualStatus)
                : ReservationAuthorizationResult.InvalidState(string.Join(" or ", allowedStatuses));
        }

        private static ReservationAuthorizationResult AuthorizeAssignment(
            GFMS_ReservationHeader reservation,
            ReservationActor actor)
        {
            if (!actor.AssignedRoles.Contains(ReservationRoles.AdminApprover) &&
                !actor.AssignedRoles.Contains(ReservationRoles.AdminManager))
            {
                return ReservationAuthorizationResult.Forbid();
            }

            var expectedRole = reservation.ReservationType switch
            {
                "FN" => reservation.Status switch
                {
                    ReservationStatuses.AdminApproverApproval => ReservationRoles.AdminApprover,
                    ReservationStatuses.AdminManagerApproval => ReservationRoles.AdminApprover,
                    _ => null
                },
                "GH" when reservation.Status is ReservationStatuses.AdminApproverApproval or ReservationStatuses.AdminManagerApproval =>
                    ReservationRoles.AdminApprover,
                "DRM" when reservation.Status == ReservationStatuses.AdminApproverApproval =>
                    ReservationRoles.AdminApprover,
                _ => null
            };

            if (expectedRole == null)
            {
                return ReservationAuthorizationResult.InvalidState();
            }

            return actor.AssignedRoles.Contains(expectedRole)
                ? ReservationAuthorizationResult.Allow(reservation.Status)
                : ReservationAuthorizationResult.Forbid();
        }

        private static ReservationAuthorizationResult AuthorizeWorkflowTransition(
            GFMS_ReservationHeader reservation,
            ReservationActor actor,
            bool reject)
        {
            if (!actor.AssignedRoles.Contains(ReservationRoles.DepartmentHead) &&
                !actor.AssignedRoles.Contains(ReservationRoles.AdminApprover) &&
                !actor.AssignedRoles.Contains(ReservationRoles.AdminManager))
            {
                return ReservationAuthorizationResult.Forbid();
            }

            var transition = GetTransition(reservation.ReservationType, reservation.Status);
            if (transition == null)
            {
                return ReservationAuthorizationResult.InvalidState();
            }

            if (!actor.AssignedRoles.Contains(transition.Value.RequiredRole))
            {
                return ReservationAuthorizationResult.Forbid();
            }

            if (transition.Value.RequiredRole == ReservationRoles.DepartmentHead &&
                !(Matches(reservation.ApproverEmail, actor.Email) ||
                  Matches(reservation.ApproverEmail, actor.EmployeeEmail) ||
                  Matches(reservation.Approver, actor.EmployeeName) ||
                  (reservation.ReservationType != "FN" && reservation.ReservationType != "GH" && IsManagedRequest(reservation, actor))))
            {
                return ReservationAuthorizationResult.Forbid();
            }

            return ReservationAuthorizationResult.Allow(
                reservation.Status,
                reject ? ReservationStatuses.Disapproved : transition.Value.NextStatus);
        }

        private static (string RequiredRole, string NextStatus)? GetTransition(
            string? reservationType,
            string? status)
        {
            if (status == ReservationStatuses.DepartmentHeadApproval)
            {
                return (ReservationRoles.DepartmentHead, ReservationStatuses.AdminApproverApproval);
            }

            if (status == ReservationStatuses.AdminApproverApproval)
            {
                return (ReservationRoles.AdminApprover, reservationType is "FN" or "GH"
                    ? ReservationStatuses.Approved
                    : ReservationStatuses.AdminManagerApproval);
            }

            if (status == ReservationStatuses.AdminManagerApproval)
            {
                return (reservationType is "FN" or "GH"
                    ? ReservationRoles.AdminApprover
                    : ReservationRoles.AdminManager, ReservationStatuses.Approved);
            }

            return null;
        }

        private static bool Matches(string? left, string? right) =>
            !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool IsManagedRequest(
            GFMS_ReservationHeader reservation,
            ReservationActor actor) =>
            actor.ManagedEmployeeEmails.Any(email => Matches(reservation.RequestorEmail, email));
    }
}
