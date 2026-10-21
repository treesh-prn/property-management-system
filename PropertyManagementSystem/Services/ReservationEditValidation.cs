namespace PropertyManagementSystem.Services;

public static class ReservationEditValidation
{
    public static string? Validate(DateTime start, DateTime end, string? purpose, DateTime now)
    {
        if (start == default || end == default || start.Year < 1753 || end.Year < 1753)
            return "Enter a valid start and end date and time.";
        if (end <= start) return "End date and time must be later than start date and time.";
        if (start <= now) return "Choose a start date and time in the future. Past schedules cannot be saved.";
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Trim().Length > 50)
            return "Enter a purpose between 1 and 50 characters.";
        return null;
    }
}
