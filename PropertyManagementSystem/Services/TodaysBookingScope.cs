using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public static class TodaysBookingScope
{
    public static bool CanView(string? activeRole) => activeRole is
        ReservationRoles.DepartmentHead or ReservationRoles.AdminApprover or ReservationRoles.SystemAdmin;

    public static IQueryable<GFMS_ReservationHeader> ForTodaysBookingViewer(
        this IQueryable<GFMS_ReservationHeader> reservations, string? activeRole, int? departmentId)
    {
        var approved = reservations.Where(reservation => reservation.Status == ReservationStatuses.Approved);
        if (activeRole is ReservationRoles.AdminApprover or ReservationRoles.SystemAdmin)
            return approved;
        if (activeRole == ReservationRoles.DepartmentHead && departmentId.HasValue)
            return approved.Where(reservation => reservation.Department == departmentId.Value);
        return approved.Where(_ => false);
    }
}
