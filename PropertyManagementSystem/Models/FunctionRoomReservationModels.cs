namespace PropertyManagementSystem.Models
{
    public class FunctionRoomIndexViewData
    {
        public List<GFMS_Rooms> Rooms { get; set; } = new();
        public List<GFMS_Facility> Facilities { get; set; } = new();
        public ReservationViewModel Summary { get; set; } = new();
    }

    public class CreateFunctionRoomReservationRequest
    {
        public int _company { get; set; }
        public string? _requestor { get; set; }
        public string? _requestorEmail { get; set; }
        public int _department { get; set; }
    }

    public class CreateFunctionRoomReservationDetailsRequest
    {
        public int _reservationId { get; set; }
        public int _numOfGuests { get; set; }
        public int? _facilityId { get; set; }
        public int? _roomId { get; set; }
        public DateTime _startDate { get; set; }
        public DateTime _endDate { get; set; }
        public string? _recurrenceRule { get; set; }
        public string? _purpose { get; set; }
        public List<string>? _equipment { get; set; }
        public string? _contactNumber { get; set; }
        public string? _requestorEmail { get; set; }
    }

    public class EditFunctionRoomReservationDetailsRequest
    {
        public int ReservationDetailId { get; set; }
        public int ReservationId { get; set; }
        public int NumberOfGuest { get; set; }
        public int? _facilityId { get; set; }
        public int? _roomId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? _purpose { get; set; }
        public string[]? EquipmentNeeded { get; set; }
        public string? _contactNumber { get; set; }
    }

    public class UpdateFunctionRoomReservationStatusRequest
    {
        public int ReservationId { get; set; }
        public int ReservationDetailId { get; set; }
        public string? action { get; set; }
        public int? _facilityId { get; set; }
        public int? _roomId { get; set; }
        public int? _swapWithReservationDetailId { get; set; }
        public string? Remarks { get; set; }
        public string? EquipmentNeeded { get; set; }
    }

    public class FunctionRoomAssignmentOption
    {
        public int RoomId { get; set; }
        public int FacilityId { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public bool IsOccupied { get; set; }
        public bool CanSwap { get; set; }
        public int? ConflictingReservationDetailId { get; set; }
        public string? ConflictDescription { get; set; }
    }

    public class FunctionRoomOperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RedirectGuid { get; set; }
        public string? AlertMessage { get; set; }
        public string? AlertSubMessage { get; set; }
        public string? AlertType { get; set; }
        public ReservationOperationFailure Failure { get; set; }

        public static FunctionRoomOperationResult Ok(string? message = null, string? subMessage = null, string? alertType = null)
        {
            return new FunctionRoomOperationResult
            {
                Success = true,
                AlertMessage = message,
                AlertSubMessage = subMessage,
                AlertType = alertType
            };
        }

        public static FunctionRoomOperationResult Fail(
            string errorMessage,
            ReservationOperationFailure failure = ReservationOperationFailure.Validation)
        {
            return new FunctionRoomOperationResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Failure = failure
            };
        }
    }

    public enum ReservationOperationFailure
    {
        None,
        NotFound,
        Forbidden,
        Conflict,
        Validation
    }
}
