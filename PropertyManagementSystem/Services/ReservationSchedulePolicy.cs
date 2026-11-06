namespace PropertyManagementSystem.Services;

public static class ReservationSchedulePolicy
{
    public const string SameDayMessage = "Same-day reservations are available only to an active Admin Approver. Select tomorrow or a later date.";

    public static bool CanBookSameDay(ReservationActor actor) =>
        actor.ActiveRole == ReservationRoles.AdminApprover &&
        actor.AssignedRoles.Contains(ReservationRoles.AdminApprover);

    public static DateTime GetMinimumDate(ReservationActor actor, DateTime now) =>
        CanBookSameDay(actor) ? now.Date : now.Date.AddDays(1);

    public static string? ValidateStart(DateTime start, ReservationActor actor, DateTime now)
    {
        if (start <= now)
            return "Choose a start date and time in the future. Past schedules cannot be saved.";
        if (start.Date == now.Date && !CanBookSameDay(actor))
            return SameDayMessage;
        return null;
    }
}
