using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class DashboardApprovalScope
    {
        public static IQueryable<GFMS_ReservationHeader> ForAssignedApprovalRoles(
            this IQueryable<GFMS_ReservationHeader> reservations,
            IEnumerable<string> assignedRoles,
            Gen_Employee? employee,
            IQueryable<Gen_Employee> employees,
            string? identityEmail = null)
        {
            var roles = assignedRoles.ToHashSet(StringComparer.Ordinal);
            if (roles.Contains(ReservationRoles.SystemAdmin))
            {
                return reservations;
            }

            var canApproveAsDepartmentHead = roles.Contains(ReservationRoles.DepartmentHead) &&
                employee != null;
            var canApproveAsAdminApprover = roles.Contains(ReservationRoles.AdminApprover);
            var canApproveAsAdminManager = roles.Contains(ReservationRoles.AdminManager);
            var employeeEmail = employee?.Email;
            var employeeName = employee?.Fullname;
            var employeeGuid = employee?.GUID;

            return reservations.Where(reservation =>
                (canApproveAsAdminApprover &&
                 reservation.Status == ReservationStatuses.AdminApproverApproval) ||
                (canApproveAsAdminManager &&
                 reservation.Status == ReservationStatuses.AdminManagerApproval) ||
                (canApproveAsDepartmentHead &&
                 reservation.Status == ReservationStatuses.DepartmentHeadApproval &&
                 ((!string.IsNullOrEmpty(identityEmail) && reservation.ApproverEmail == identityEmail) ||
                  (!string.IsNullOrEmpty(employeeEmail) && reservation.ApproverEmail == employeeEmail) ||
                  (!string.IsNullOrEmpty(employeeName) && reservation.Approver == employeeName) ||
                  (employeeGuid.HasValue && employees.Any(requestor =>
                      requestor.ReportingTo == employeeGuid.Value &&
                      !string.IsNullOrEmpty(requestor.Email) &&
                      requestor.Email == reservation.RequestorEmail)))));
        }

        public static IQueryable<GFMS_ReservationHeader> ForDepartmentHead(
            this IQueryable<GFMS_ReservationHeader> reservations,
            Gen_Employee? employee,
            string? identityEmail = null)
        {
            if (employee?.DepartmentId == null)
            {
                return reservations.Where(_ => false);
            }

            var departmentId = employee.DepartmentId.Value;
            var employeeEmail = employee.Email;
            var employeeName = employee.Fullname;

            return reservations.Where(reservation =>
                reservation.Department == departmentId &&
                ((!string.IsNullOrEmpty(identityEmail) && reservation.ApproverEmail == identityEmail) ||
                 (!string.IsNullOrEmpty(employeeEmail) && reservation.ApproverEmail == employeeEmail) ||
                 (!string.IsNullOrEmpty(employeeName) && reservation.Approver == employeeName)));
        }
    }
}
