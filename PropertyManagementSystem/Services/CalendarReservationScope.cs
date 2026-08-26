using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class CalendarReservationScope
    {
        public static IQueryable<GFMS_ReservationHeader> ForCalendarViewer(
            this IQueryable<GFMS_ReservationHeader> reservations,
            string? activeRole,
            string? identityEmail,
            string? employeeEmail)
        {
            var approved = reservations.Where(header =>
                header.Status == ReservationStatuses.Approved);

            if (activeRole is ReservationRoles.AdminApprover or
                ReservationRoles.AdminManager or
                ReservationRoles.SystemAdmin)
            {
                return approved;
            }

            if (activeRole is not ReservationRoles.Requestor and
                not ReservationRoles.DepartmentHead)
            {
                return approved.Where(_ => false);
            }

            var normalizedIdentityEmail = identityEmail?.Trim();
            var normalizedEmployeeEmail = employeeEmail?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedIdentityEmail) &&
                string.IsNullOrWhiteSpace(normalizedEmployeeEmail))
            {
                return approved.Where(_ => false);
            }

            return approved.Where(header =>
                (!string.IsNullOrEmpty(normalizedIdentityEmail) &&
                 header.RequestorEmail == normalizedIdentityEmail) ||
                (!string.IsNullOrEmpty(normalizedEmployeeEmail) &&
                 header.RequestorEmail == normalizedEmployeeEmail));
        }
    }
}
