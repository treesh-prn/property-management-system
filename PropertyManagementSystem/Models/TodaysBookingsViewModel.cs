namespace PropertyManagementSystem.Models;

public sealed class TodaysBookingsViewModel
{
    public DateTime Date { get; set; }
    public string? Search { get; set; }
    public string? Type { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; } = 25;
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<TodaysBookingRow> Bookings { get; set; } = Array.Empty<TodaysBookingRow>();
}

public sealed record TodaysBookingRow(int ReservationId, string? Guid, string? Code, string? Type,
    string? Requestor, DateTime Start, DateTime End, string? Purpose, string Location)
{
    public string TypeName => Type switch
    {
        "FN" => "Function room", "GH" => "Accommodation", "DRM" => "Dorm & housing", _ => Type ?? "Booking"
    };
    public string? DetailsController => Type switch
    {
        "FN" => "FunctionRoom", "GH" => "Accommodation", "DRM" => "Dorm", _ => null
    };
}
