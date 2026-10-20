using System.Security.Claims;
using System.Data;
using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public class FunctionRoomReservationService
    {
        private const string ReservationType = "FN";
        private const int FunctionRoomType = 1;
        private const string FunctionRoomClassification = "FUNCTION ROOM";

        private readonly DAL _context;
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;
        private readonly IReservationAuthorizationService _authorization;
        private readonly IReservationAuditService _audit;
        private readonly IReservationAssignmentService _assignments;
        private readonly IReservationCodeService _reservationCodes;
        private readonly IReservationProcessingPolicy _processingPolicy;
        private readonly IReservationScheduleStatusService _scheduleStatus;
        private readonly IReservationEquipmentAllocationService _equipmentAllocations;

        public FunctionRoomReservationService(
            DAL context,
            IRoleService roleService,
            EmailService emailService,
            EmployeeService employeeService,
            IReservationAuthorizationService authorization,
            IReservationAuditService audit,
            IReservationAssignmentService assignments,
            IReservationCodeService reservationCodes,
            IReservationProcessingPolicy processingPolicy,
            IReservationScheduleStatusService scheduleStatus,
            IReservationEquipmentAllocationService equipmentAllocations)
        {
            _context = context;
            _roleService = roleService;
            _emailService = emailService;
            _employeeService = employeeService;
            _authorization = authorization;
            _audit = audit;
            _assignments = assignments;
            _reservationCodes = reservationCodes;
            _processingPolicy = processingPolicy;
            _scheduleStatus = scheduleStatus;
            _equipmentAllocations = equipmentAllocations;
        }

        public Task<string?> GetActiveRoleAsync(ClaimsPrincipal user) => _roleService.GetActiveRoleAsync(user);

        public async Task<IReadOnlyList<int>> GetMyReservationYearsAsync(ClaimsPrincipal user)
        {
            var employee = await _roleService.GetUserName(user);
            var requestorEmail = user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value
                ?? user.Identity?.Name;

            return await ReservationHistoryYearService.GetAvailableYearsAsync(
                _context,
                ReservationType,
                requestorEmail,
                employee?.Email,
                employee?.Fullname);
        }

        public FunctionRoomIndexViewData GetIndexViewData()
        {
            var rooms = _context.Rooms
                .AsNoTracking()
                .Include(r => r.Facility)
                .Where(x => x.RoomType == FunctionRoomType)
                .Select(room => new GFMS_Rooms
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    RoomType = room.RoomType,
                    FacilityId = room.FacilityId
                })
                .ToList();

            var facilities = _context.Facilities
                .AsNoTracking()
                .Where(facility => facility.Rooms.Any(room => room.RoomType == FunctionRoomType))
                .Select(facility => new GFMS_Facility
                {
                    FacilityId = facility.FacilityId,
                    FacilityName = facility.FacilityName,
                })
                .ToList();

            var now = DateTime.Now;
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var pendingReservationCount = _context.ReservationHeaders.Count(r =>
                r.ReservationType == ReservationType &&
                (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL" ||
                 r.Status == "FOR ADMIN MANAGER'S APPROVAL" ||
                 r.Status == "FOR ADMIN APPROVER'S APPROVAL"));

            var approvedSchedules =
                from detail in _context.ReservationDetails
                join header in _context.ReservationHeaders on detail.ReservationId equals header.ReservationId
                where header.ReservationType == ReservationType
                      && header.Status == "APPROVED"
                      && detail.RoomId.HasValue
                      && detail.StartDate.HasValue
                      && detail.EndDate.HasValue
                select new
                {
                    header.ReservationId,
                    RoomId = detail.RoomId.Value,
                    Start = detail.StartDate.Value,
                    End = detail.EndDate.Value
                };

            var todaysBookingCount = approvedSchedules
                .Where(item => item.Start < tomorrow && item.End > today)
                .Select(item => item.ReservationId)
                .Distinct()
                .Count();
            var occupiedRoomsCount = approvedSchedules
                .Where(item => item.Start < now && item.End > now)
                .Select(item => item.RoomId)
                .Distinct()
                .Count();
            var activeRoomCount = _context.Rooms.Count(room => room.RoomType == FunctionRoomType && room.isActive);

            return new FunctionRoomIndexViewData
            {
                Rooms = rooms,
                Facilities = facilities,
                Summary = new ReservationViewModel
                {
                    NewFNReservationsCount = pendingReservationCount,
                    ActiveGuestCount = todaysBookingCount,
                    OccupiedRoomsCount = occupiedRoomsCount,
                    AvailableRoomsCount = Math.Max(activeRoomCount - occupiedRoomsCount, 0)
                }
            };
        }

        public async Task<DataSourceResult> GetCalendarReservationsAsync(DataSourceRequest request, ClaimsPrincipal user)
        {
            var actor = await _authorization.GetActorAsync(user);
            var visibleHeaders = _context.ReservationHeaders
                .AsNoTracking()
                .Where(header => header.ReservationType == ReservationType)
                .ForCalendarViewer(actor.ActiveRole, actor.Email, actor.EmployeeEmail);

            var reservationsQuery =
                from resDetails in _context.ReservationDetails.AsNoTracking()
                join resHeader in visibleHeaders
                    on resDetails.ReservationId equals resHeader.ReservationId
                join room in _context.Rooms.AsNoTracking()
                    on resDetails.RoomId equals (int?)room.RoomId
                join facility in _context.Facilities.AsNoTracking()
                    on resDetails.FacilityId equals (int?)facility.FacilityId
                select new
                {
                    resDetails.ReservationDetailId,
                    resDetails.ReservationId,
                    resHeader.GUID,
                    resHeader.Requestor,
                    resHeader.ChargeToCompany,
                    resHeader.Status,
                    StartDate = resDetails.StartDate,
                    EndDate = resDetails.EndDate,
                    resDetails.Purpose,
                    resDetails.RoomId,
                    resDetails.FacilityId,
                    room.RoomName,
                    facility.FacilityName,
                    resHeader.RequestorEmail
                };

            var reservations = reservationsQuery
                .AsEnumerable()
                .Select(r => new ReservationViewModel
                {
                    ReservationDetailId = r.ReservationDetailId,
                    ReservationId = r.ReservationId,
                    GUID = r.GUID,
                    Requestor = r.Requestor,
                    ChargeToCompany = r.ChargeToCompany,
                    Status = r.Status,
                    Start = r.StartDate.HasValue ? DateTime.SpecifyKind(r.StartDate.Value, DateTimeKind.Utc) : DateTime.MinValue,
                    End = r.EndDate.HasValue ? DateTime.SpecifyKind(r.EndDate.Value, DateTimeKind.Utc) : DateTime.MinValue,
                    Title = r.Purpose,
                    Purpose = r.Purpose,
                    StartDate = r.StartDate.HasValue ? r.StartDate.Value.ToShortTimeString() : "",
                    RoomId = r.RoomId ?? 0,
                    FacilityId = r.FacilityId ?? 0,
                    RoomName = r.RoomName,
                    FacilityName = r.FacilityName,
                })
                .ToList();

            return reservations.ToDataSourceResult(request);
        }

        public List<object> GetFacilities()
        {
            return _context.Facilities
                .AsNoTracking()
                .Where(f => f.FacilityClassification == FunctionRoomClassification)
                .OrderBy(f => f.FacilityName)
                .Select(f => new
                {
                    f.FacilityId,
                    f.FacilityName,
                    FacilityClass = f.FacilityClassification
                })
                .Cast<object>()
                .ToList();
        }

        public List<object> GetRooms(int? facilityId)
        {
            var rooms = _context.Rooms
                .AsNoTracking()
                .Where(room => room.RoomType == FunctionRoomType);
            if (facilityId.HasValue)
            {
                rooms = rooms.Where(p => p.FacilityId == facilityId.Value);
            }

            return rooms
                .Select(p => new { p.RoomId, p.RoomName })
                .Cast<object>()
                .ToList();
        }

        public List<object> GetDraftAvailableFacilities()
        {
            // Keep facility discovery independent from schedule availability,
            // but never offer inactive facilities or facilities with no active
            // Function Room to an administrator creating a reservation.
            return _context.Facilities
                .AsNoTracking()
                .Where(facility =>
                    facility.FacilityClassification == FunctionRoomClassification &&
                    facility.isActive != false &&
                    facility.Rooms.Any(room =>
                        room.RoomType == FunctionRoomType && room.isActive))
                .OrderBy(facility => facility.FacilityName)
                .Select(facility => new
                {
                    facility.FacilityId,
                    facility.FacilityName,
                    FacilityClass = facility.FacilityClassification
                })
                .Cast<object>()
                .ToList();
        }

        public List<object> GetDraftAvailableRooms(
            int? facilityId,
            DateTime startDate,
            DateTime endDate)
        {
            if (!facilityId.HasValue ||
                !IsWithinSqlDateRange(startDate, endDate) ||
                endDate <= startDate)
                return new List<object>();

            return _context.Rooms
                .AsNoTracking()
                .Where(room =>
                    room.FacilityId == facilityId.Value &&
                    room.RoomType == FunctionRoomType &&
                    room.isActive &&
                    room.Facility.isActive != false)
                .AsEnumerable()
                .Where(room => !HasOverlappingFunctionRoomReservation(
                    room.RoomId, startDate, endDate, 0))
                .OrderBy(room => room.RoomName)
                .Select(room => new { room.RoomId, room.RoomName, room.Capacity })
                .Cast<object>()
                .ToList();
        }

        public List<object> GetAvailableFacilities(
            int reservationDetailId,
            ClaimsPrincipal? user = null,
            bool includeOccupied = false)
        {
            if (user != null && !CanAssignFunctionRoom(user))
            {
                return new List<object>();
            }

            var requestedSchedule = GetRequestedSchedule(reservationDetailId);
            if (requestedSchedule == null)
            {
                return new List<object>();
            }

            var availableFacilityIds = _context.Rooms
                .AsNoTracking()
                .Where(room =>
                    room.RoomType == FunctionRoomType &&
                    room.isActive &&
                    room.Facility.isActive != false)
                .AsEnumerable()
                .Where(room => includeOccupied || !HasOverlappingFunctionRoomReservation(
                        room.RoomId,
                        requestedSchedule.Value.StartDate,
                        requestedSchedule.Value.EndDate,
                        reservationDetailId))
                .Select(room => room.FacilityId)
                .Distinct()
                .ToList();

            return _context.Facilities
                .AsNoTracking()
                .Where(f =>
                    f.FacilityClassification == FunctionRoomClassification &&
                    f.isActive != false)
                .Where(f => availableFacilityIds.Contains(f.FacilityId))
                .OrderBy(f => f.FacilityName)
                .Select(f => new
                {
                    f.FacilityId,
                    f.FacilityName,
                    FacilityClass = f.FacilityClassification
                })
                .Cast<object>()
                .ToList();
        }

        public List<object> GetAvailableRooms(
            int? facilityId,
            int reservationDetailId,
            ClaimsPrincipal? user = null,
            bool includeOccupied = false)
        {
            if (user != null && !CanAssignFunctionRoom(user))
            {
                return new List<object>();
            }

            var requestedSchedule = GetRequestedSchedule(reservationDetailId);
            if (!facilityId.HasValue || requestedSchedule == null)
            {
                return new List<object>();
            }

            var currentDetail = _context.ReservationDetails.AsNoTracking()
                .FirstOrDefault(detail => detail.ReservationDetailId == reservationDetailId);
            var rooms = _context.Rooms
                .AsNoTracking()
                .Where(room =>
                    room.FacilityId == facilityId.Value &&
                    room.RoomType == FunctionRoomType &&
                    room.isActive &&
                    room.Facility.isActive != false)
                .OrderBy(room => room.RoomName)
                .ToList();

            var conflicts = _context.ReservationDetails.AsNoTracking()
                .Where(detail =>
                    detail.ReservationDetailId != reservationDetailId &&
                    detail.RoomId.HasValue &&
                    detail.StartDate.HasValue && detail.EndDate.HasValue &&
                    detail.StartDate.Value < requestedSchedule.Value.EndDate &&
                    detail.EndDate.Value > requestedSchedule.Value.StartDate)
                .Join(_context.ReservationHeaders.AsNoTracking(),
                    detail => detail.ReservationId,
                    header => header.ReservationId,
                    (detail, header) => new { detail, header })
                .Where(item => item.header.ReservationType == ReservationType &&
                    new[]
                    {
                        ReservationStatuses.DepartmentHeadApproval,
                        ReservationStatuses.AdminApproverApproval,
                        ReservationStatuses.AdminManagerApproval,
                        ReservationStatuses.Reserved,
                        ReservationStatuses.Approved
                    }.Contains(item.header.Status!))
                .ToList()
                .GroupBy(item => item.detail.RoomId!.Value)
                .ToDictionary(group => group.Key, group => group.ToList());

            return rooms
                .Select(room =>
                {
                    conflicts.TryGetValue(room.RoomId, out var roomConflicts);
                    roomConflicts ??= new();
                    var conflict = roomConflicts.Count == 1 ? roomConflicts[0] : null;
                    var canSwap = includeOccupied && conflict != null &&
                        currentDetail?.RoomId.HasValue == true &&
                        currentDetail.FacilityId.HasValue &&
                        !HasThirdPartyConflictForSwap(
                            currentDetail.RoomId.Value,
                            conflict.detail.StartDate!.Value,
                            conflict.detail.EndDate!.Value,
                            currentDetail.ReservationId,
                            conflict.detail.ReservationId);
                    var conflictDescription = conflict == null ? null :
                        $"Occupied by {conflict.header.ReservationCode ?? "another request"} " +
                        $"({conflict.detail.StartDate:MMM d, h:mm tt}–{conflict.detail.EndDate:h:mm tt})";
                    return new FunctionRoomAssignmentOption
                    {
                        RoomId = room.RoomId,
                        FacilityId = room.FacilityId,
                        RoomName = room.RoomName,
                        DisplayName = conflictDescription == null
                            ? $"{room.RoomName} — Available"
                            : $"{room.RoomName} — {conflictDescription}",
                        Capacity = room.Capacity,
                        IsOccupied = roomConflicts.Count > 0,
                        CanSwap = canSwap,
                        ConflictingReservationDetailId = canSwap
                            ? conflict!.detail.ReservationDetailId : null,
                        ConflictDescription = roomConflicts.Count > 1
                            ? "Unavailable because multiple reservations overlap this schedule."
                            : conflictDescription
                    };
                })
                .Where(room => !room.IsOccupied || includeOccupied)
                .Cast<object>()
                .ToList();
        }

        private bool HasThirdPartyConflictForSwap(
            int roomId,
            DateTime startDate,
            DateTime endDate,
            int firstReservationId,
            int secondReservationId)
        {
            var activeStatuses = new[]
            {
                ReservationStatuses.DepartmentHeadApproval,
                ReservationStatuses.AdminApproverApproval,
                ReservationStatuses.AdminManagerApproval,
                ReservationStatuses.Reserved,
                ReservationStatuses.Approved
            };
            return _context.ReservationDetails.AsNoTracking()
                .Where(detail => detail.ReservationId != firstReservationId &&
                    detail.ReservationId != secondReservationId &&
                    detail.RoomId == roomId && detail.StartDate.HasValue && detail.EndDate.HasValue &&
                    detail.StartDate.Value < endDate && detail.EndDate.Value > startDate)
                .Join(_context.ReservationHeaders.AsNoTracking(), d => d.ReservationId,
                    h => h.ReservationId, (d, h) => h)
                .Any(header => activeStatuses.Contains(header.Status!));
        }

        public async Task<DataSourceResult> GetReservationGrid(
            DataSourceRequest request,
            string gridType,
            ClaimsPrincipal user,
            int? year = null)
        {
            var employee = await _roleService.GetUserName(user);
            var assignedRoles = await _roleService.GetRolesAsync(user);
            var requestorEmail = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value
                ?? user.Identity?.Name
                ?? string.Empty;
            var employeeEmail = employee?.Email ?? string.Empty;
            var query = _context.ReservationHeaders
                .AsNoTracking()
                .Where(x => x.ReservationType == ReservationType)
                .Where(x => x.Status != "DELETED");

            if (gridType == "myReservation")
            {
                var selectedYear = NormalizeReservationYear(year);
                if (selectedYear.HasValue)
                {
                    var yearStart = new DateTime(selectedYear.Value, 1, 1);
                    var nextYearStart = yearStart.AddYears(1);
                    query = query.Where(header =>
                        header.Status == ReservationStatuses.ForRescheduling ||
                        _context.ReservationDetails.Any(detail =>
                            detail.ReservationId == header.ReservationId &&
                            detail.StartDate >= yearStart && detail.StartDate < nextYearStart) ||
                        (!_context.ReservationDetails.Any(detail => detail.ReservationId == header.ReservationId) &&
                         header.DateFiled >= yearStart && header.DateFiled < nextYearStart));
                }

                // Some valid Identity accounts do not yet have a matching HR employee
                // row. Use the immutable requestor email as the primary scope and keep
                // the employee-name fallback for legacy records.
                var requestorName = employee?.Fullname;
                query = query.Where(x =>
                    (!string.IsNullOrEmpty(requestorEmail) && x.RequestorEmail == requestorEmail) ||
                    (!string.IsNullOrEmpty(employeeEmail) && x.RequestorEmail == employeeEmail) ||
                    (!string.IsNullOrEmpty(requestorName) && x.Requestor == requestorName));
            }
            else if (gridType == "reservationApproval")
            {
                query = query.ForAssignedApprovalRoles(
                    assignedRoles,
                    employee,
                    _context.Employees.AsNoTracking(),
                    requestorEmail);
            }
            else
            {
                query = query.Where(_ => false);
            }

            var reservationHeaders = query.ToList();
            var reservationViewModels = reservationHeaders.Select(header =>
            {
                var department = _context.Departments.AsNoTracking().FirstOrDefault(d => d.Id == header.Department);
                var detailId = _context.ReservationDetails
                    .AsNoTracking()
                    .Where(rd => rd.ReservationId == header.ReservationId)
                    .OrderBy(rd => rd.StartDate)
                    .Select(rd => (int?)rd.ReservationDetailId)
                    .FirstOrDefault() ?? 0;

                return new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    ReservationCode = header.ReservationCode,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    Status = header.Status,
                    GUID = header.GUID,
                    CanDelete = header.Status == "DRAFT",
                    ChargeToCompany = header.ChargeToCompany,
                    ReservationType = header.ReservationType,
                    DepartmentName = department?.DepartmentCode,
                    ReservationDetailId = detailId
                };
            }).ToList();

            return reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.Status,
                model.CanDelete,
                model.GUID,
                model.ChargeToCompany,
                model.ReservationType,
                model.ReservationDetailId
            });
        }

        private static int? NormalizeReservationYear(int? year)
        {
            if (year == 0) return null;
            return year is >= 2000 && year <= 9999 ? year : DateTime.Today.Year;
        }

        public async Task<FunctionRoomOperationResult> CreateReservation(
            CreateFunctionRoomReservationRequest request,
            ClaimsPrincipal user)
        {
            var actor = await _authorization.GetActorAsync(user);
            if (!await _authorization.CanCreateAsync(user))
            {
                return FunctionRoomOperationResult.Fail(
                    "Access denied.", ReservationOperationFailure.Forbidden);
            }
            var userId = actor.UserId;


            if (string.IsNullOrWhiteSpace(actor.EmployeeEmail) ||
                string.IsNullOrWhiteSpace(actor.EmployeeName) ||
                !actor.DepartmentId.HasValue ||
                !actor.CompanyId.HasValue)
            {
                return FunctionRoomOperationResult.Fail(
                    "The authenticated account is not linked to an employee record.",
                    ReservationOperationFailure.Forbidden);
            }

            var guid = Guid.NewGuid().ToString("N").ToUpper();
            var reservation = new GFMS_ReservationHeader
            {
                GUID = guid,
                ChargeToCompany = actor.CompanyId.Value,
                Requestor = actor.EmployeeName,
                RequestorEmail = actor.EmployeeEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = null,
                Status = "DRAFT",
                Department = actor.DepartmentId.Value,
                ReservationType = ReservationType
            };

            var submissionStatus = await _authorization.GetSubmissionStatusAsync(reservation, actor);
            if (submissionStatus == ReservationStatuses.DepartmentHeadApproval &&
                string.IsNullOrWhiteSpace(reservation.ApproverEmail))
                return FunctionRoomOperationResult.Fail(
                    "Department Head not configured for the authenticated employee's department.",
                    ReservationOperationFailure.NotFound);

            await _reservationCodes.CreateAsync(reservation, "RSQ-FN");

            return new FunctionRoomOperationResult
            {
                Success = true,
                RedirectGuid = guid
            };
        }

        public ReservationViewModel? GetReservationDetailsViewModel(string guid)
        {
            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == guid);
            if (reservationHeader == null)
            {
                return null;
            }

            if (!string.Equals(reservationHeader.ReservationType, ReservationType, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var reservationDetails = _context.ReservationDetails
                .Where(x => x.ReservationId == reservationHeader.ReservationId)
                .OrderBy(x => x.StartDate)
                .ToList();

            GFMS_Facility? facility = null;
            GFMS_Rooms? room = null;
            var equipmentAllocations = _equipmentAllocations.GetForReservation(
                reservationHeader.ReservationId);
            var equipmentNames = equipmentAllocations
                .Select(item => item.EquipmentName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var firstDetail = reservationDetails.FirstOrDefault();
            if (firstDetail != null)
            {
                firstDetail.EquipmentNeeded = string.Join(",",
                    equipmentAllocations.Where(item => !item.IsAutomatic)
                        .Select(item => item.EquipmentId));
                facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == firstDetail.FacilityId);
                room = _context.Rooms.FirstOrDefault(r => r.RoomId == firstDetail.RoomId);
                if (equipmentNames.Count == 0 && firstDetail.FacilityId.HasValue && firstDetail.RoomId.HasValue)
                {
                    equipmentNames = _context.FacilityEquipmentLinks.AsNoTracking()
                        .Where(link => link.IsActive && link.LinkType == EquipmentLinkTypes.Included &&
                            link.FacilityId == firstDetail.FacilityId.Value &&
                            (!link.RoomId.HasValue || link.RoomId == firstDetail.RoomId.Value))
                        .Join(_context.Equipments.AsNoTracking(), link => link.EquipmentId,
                            equipment => equipment.Id, (link, equipment) => equipment.EquipmentName)
                        .Where(name => name != null)
                        .Select(name => name!)
                        .Distinct()
                        .ToList();
                }
            }

            var department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);

            return new ReservationViewModel
            {
                ReservationHeader = reservationHeader,
                ReservationDetails = reservationDetails,
                Department = department,
                Facility = facility,
                Room = room,
                EquipmentNames = equipmentNames,
                IncludedEquipments = equipmentAllocations.Where(item => item.IsAutomatic).ToList(),
                AdditionalEquipments = equipmentAllocations.Where(item => !item.IsAutomatic).ToList()
            };
        }

        public async Task<FunctionRoomOperationResult> CreateReservationDetails(
            CreateFunctionRoomReservationDetailsRequest request,
            ClaimsPrincipal user)
        {
            var reservationHeader = _context.ReservationHeaders
                .FirstOrDefault(rh => rh.ReservationId == request._reservationId);
            if (reservationHeader == null || reservationHeader.ReservationType != ReservationType)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            var authorization = await _authorization.AuthorizeAsync(
                user, reservationHeader, ReservationOperation.CreateDetails);
            if (!authorization.Succeeded)
            {
                return AuthorizationFailure(authorization);
            }

            var guid = reservationHeader.GUID;

            if (!IsWithinSqlDateRange(request._startDate, request._endDate))
            {
                return ValidationFailure(
                    guid,
                    "Enter a valid reservation schedule.",
                    "Select valid start and end dates.");
            }

            if (request._endDate <= request._startDate)
            {
                return ValidationFailure(
                    guid,
                    "The reservation schedule is invalid.",
                    "End date and time must be later than start date and time.");
            }

            if (request._numOfGuests <= 0)
            {
                return ValidationFailure(
                    guid,
                    "Enter a valid attendee count.",
                    "The number of attendees must be greater than zero.");
            }

            var actor = await _authorization.GetActorAsync(user);
            var canAssignDraft = actor.ActiveRole is ReservationRoles.AdminApprover or ReservationRoles.AdminManager;
            if (!canAssignDraft && !string.IsNullOrWhiteSpace(request._recurrenceRule))
            {
                return ValidationFailure(
                    guid,
                    "Recurring reservations are restricted.",
                    "Only an Admin Approver or Admin Manager can create a recurring Function Room reservation.");
            }

            var expansion = WeeklyRecurrenceExpander.Expand(
                request._startDate,
                request._endDate,
                canAssignDraft ? request._recurrenceRule : null);
            if (!expansion.Succeeded)
            {
                return ValidationFailure(
                    guid,
                    "The recurring schedule is invalid.",
                    expansion.Message ?? "Review the recurrence settings and try again.");
            }

            if (canAssignDraft && (!request._facilityId.HasValue || !request._roomId.HasValue))
            {
                return ValidationFailure(
                    guid,
                    "Select a facility and room.",
                    "Admin Approvers and Admin Managers must assign an available Function Room before submitting.");
            }

            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            string? capacityWarning = null;
            if (canAssignDraft)
            {
                foreach (var occurrence in expansion.Occurrences)
                {
                    if (!IsWithinSqlDateRange(occurrence.Start, occurrence.End))
                    {
                        return ValidationFailure(
                            guid,
                            "The recurring schedule is outside the supported date range.",
                            $"Review the occurrence starting {occurrence.Start:MMM d, yyyy h:mm tt}.");
                    }

                    var assignmentValidation = await _assignments.ValidateAsync(new ReservationSlotRequest(
                        reservationHeader.ReservationId,
                        0,
                        request._facilityId!.Value,
                        request._roomId!.Value,
                        ReservationType,
                        occurrence.Start,
                        occurrence.End,
                        request._numOfGuests,
                        FunctionRoomType));
                    if (!assignmentValidation.Succeeded)
                    {
                        return ValidationFailure(
                            guid,
                            "The selected room is unavailable for part of this recurring schedule.",
                            $"{occurrence.Start:MMM d, yyyy h:mm tt}: " +
                            (assignmentValidation.Message ?? "Choose another facility, room, or schedule."));
                    }
                }

                var selectedRoom = _context.Rooms
                    .AsNoTracking()
                    .Where(room => room.RoomId == request._roomId.Value)
                    .Select(room => new { room.RoomName, room.Capacity })
                    .FirstOrDefault();
                if (selectedRoom != null && request._numOfGuests > selectedRoom.Capacity)
                {
                    capacityWarning = $"The request has {request._numOfGuests} attendees, which exceeds " +
                        $"{selectedRoom.RoomName}'s capacity of {selectedRoom.Capacity}. The room was assigned as requested.";
                }
            }

            var dateAdded = DateTime.UtcNow;
            var reservationDetails = expansion.Occurrences
                .Select(occurrence => new GFMS_ReservationDetails
                {
                    ReservationId = request._reservationId,
                    NumberOfGuest = request._numOfGuests,
                    FacilityId = canAssignDraft ? request._facilityId : null,
                    RoomId = canAssignDraft ? request._roomId : null,
                    StartDate = occurrence.Start,
                    EndDate = occurrence.End,
                    Purpose = request._purpose,
                    DateAdded = dateAdded,
                    ContactNumber = request._contactNumber
                })
                .ToList();

            _context.ReservationDetails.AddRange(reservationDetails);

            var previousStatus = reservationHeader.Status;
            reservationHeader.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
            reservationHeader.Status = await GetSubmittedStatusAsync(actor, reservationHeader);

            _context.SaveChanges();
            var manualEquipmentIds = (request._equipment ?? new List<string>())
                .Select(value => int.TryParse(value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
            foreach (var reservationDetail in reservationDetails)
            {
                var equipmentResult = await _equipmentAllocations.SyncAsync(
                    reservationDetail,
                    manualEquipmentIds);
                if (!equipmentResult.Succeeded)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                    return ValidationFailure(
                        guid,
                        "Equipment is unavailable for part of this recurring schedule.",
                        $"{reservationDetail.StartDate:MMM d, yyyy h:mm tt}: " +
                        (equipmentResult.Message ?? "Review the equipment selection and try again."));
                }
            }
            _context.SaveChanges();
            if (transaction != null) await transaction.CommitAsync();
            await AddSubmissionAuditAsync(reservationHeader, user, previousStatus, "SUBMIT");

            if (capacityWarning != null)
            {
                return FunctionRoomOperationResult.Ok(
                    "Request submitted with a capacity warning",
                    capacityWarning,
                    "warning");
            }

            return FunctionRoomOperationResult.Ok(
                expansion.Occurrences.Count > 1
                    ? "Recurring reservation submitted successfully!"
                    : "Reservation details created successfully!",
                expansion.Occurrences.Count > 1
                    ? $"{expansion.Occurrences.Count} weekly occurrences were saved and checked for conflicts."
                    : "Your reservation has been saved",
                "success");
        }

        public async Task<FunctionRoomOperationResult> EditReservationDetails(
            EditFunctionRoomReservationDetailsRequest request,
            ClaimsPrincipal user)
        {
            var reservationDetail = _context.ReservationDetails.FirstOrDefault(rd => rd.ReservationDetailId == request.ReservationDetailId);
            if (reservationDetail == null)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation detail not found.", ReservationOperationFailure.NotFound);
            }

            var reservationHeader = _context.ReservationHeaders
                .FirstOrDefault(h => h.ReservationId == reservationDetail.ReservationId);
            if (reservationHeader == null || reservationHeader.ReservationType != ReservationType)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            var authorization = await _authorization.AuthorizeAsync(
                user, reservationHeader, ReservationOperation.Edit);
            if (!authorization.Succeeded)
            {
                return AuthorizationFailure(authorization);
            }

            var previousStatus = reservationHeader.Status;
            var isResubmission = previousStatus == ReservationStatuses.ForRescheduling;
            var assignmentCommitted = false;

            ValidateSqlDateRange(request.StartDate, request.EndDate);

            if (request.StartDate >= request.EndDate)
            {
                return FunctionRoomOperationResult.Fail("The start date must be earlier than the end date.");
            }

            if (isResubmission && request.StartDate <= DateTime.Now)
            {
                return FunctionRoomOperationResult.Fail(
                    "Choose a future start date before resubmitting this request.");
            }

            if (!isResubmission && (request._facilityId.HasValue || request._roomId.HasValue))
            {
                if (!request._facilityId.HasValue || !request._roomId.HasValue)
                    return FunctionRoomOperationResult.Fail("Select both a facility and a room.");

                var assignment = await _assignments.AssignAsync(new ReservationAssignmentCommand(
                    new ReservationSlotRequest(
                        reservationHeader.ReservationId,
                        reservationDetail.ReservationDetailId,
                        request._facilityId.Value,
                        request._roomId.Value,
                        ReservationType,
                        request.StartDate,
                        request.EndDate,
                        request.NumberOfGuest,
                        FunctionRoomType),
                    reservationHeader.Status!,
                    reservationHeader.RowVersion.ToArray(),
                    reservationDetail.RowVersion.ToArray(),
                    ManualEquipmentIds: (request.EquipmentNeeded ?? Array.Empty<string>())
                        .Select(value => int.TryParse(value, out var id) ? id : 0)
                        .Where(id => id > 0)
                        .ToArray()));
                if (!assignment.Succeeded) return AssignmentFailure(assignment);
                reservationHeader = assignment.Reservation!;
                reservationDetail = assignment.Detail!;
                assignmentCommitted = true;
            }

            reservationDetail.NumberOfGuest = request.NumberOfGuest;
            reservationDetail.FacilityId = isResubmission ? null : request._facilityId;
            reservationDetail.RoomId = isResubmission ? null : request._roomId;
            reservationDetail.StartDate = request.StartDate;
            reservationDetail.EndDate = request.EndDate;
            reservationDetail.ContactNumber = request._contactNumber;
            reservationDetail.Purpose = request._purpose;
            await using var equipmentTransaction = !assignmentCommitted && _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;
            if (!assignmentCommitted)
            {
                var equipmentResult = await _equipmentAllocations.SyncAsync(
                    reservationDetail,
                    (request.EquipmentNeeded ?? Array.Empty<string>())
                        .Select(value => int.TryParse(value, out var id) ? id : 0)
                        .Where(id => id > 0));
                if (!equipmentResult.Succeeded)
                    return FunctionRoomOperationResult.Fail(
                        equipmentResult.Message ?? "The requested equipment is not available.");
            }

            if (isResubmission)
            {
                reservationHeader.Status = await _authorization.GetSubmissionStatusAsync(reservationHeader);
                reservationHeader.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
                reservationHeader.Remarks = null;
                reservationHeader.ApprovedBy = null;
            }

            _context.SaveChanges();
            if (equipmentTransaction != null) await equipmentTransaction.CommitAsync();
            if (isResubmission)
            {
                await AddSubmissionAuditAsync(
                    reservationHeader, user, previousStatus, "RESCHEDULE_AND_RESUBMIT");
            }
            else
            {
                await _audit.AddAsync(
                    reservationHeader,
                    user,
                    "EDIT",
                    previousStatus,
                    reservationHeader.Status);
            }

            return FunctionRoomOperationResult.Ok(
                "Reservation details updated successfully!",
                "Your reservation has been updated",
                "success");
        }

        public async Task<FunctionRoomOperationResult> DeleteReservation(
            int reservationId,
            ClaimsPrincipal user)
        {
            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rd => rd.ReservationId == reservationId);
            if (reservationHeader == null)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            if (reservationHeader.ReservationType != ReservationType)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            var authorization = await _authorization.AuthorizeAsync(
                user, reservationHeader, ReservationOperation.Delete);
            if (!authorization.Succeeded)
            {
                return AuthorizationFailure(authorization);
            }

            var previousStatus = reservationHeader.Status;
            reservationHeader.Status = "DELETED";
            _context.SaveChanges();
            await _audit.AddAsync(
                reservationHeader, user, "DELETE", previousStatus, reservationHeader.Status);
            return FunctionRoomOperationResult.Ok();
        }

        public async Task<FunctionRoomOperationResult> UpdateReservationStatus(
            UpdateFunctionRoomReservationStatusRequest request,
            ClaimsPrincipal user)
        {
            if (request.ReservationId <= 0 || string.IsNullOrEmpty(request.action))
            {
                return FunctionRoomOperationResult.Fail("Invalid parameters.");
            }

            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == request.ReservationId);
            if (reservation == null)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            if (reservation.ReservationType != ReservationType)
            {
                return FunctionRoomOperationResult.Fail(
                    "Reservation not found.", ReservationOperationFailure.NotFound);
            }

            GFMS_ReservationDetails? reservationDetails = null;
            if (request.ReservationDetailId > 0)
            {
                reservationDetails = _context.ReservationDetails.FirstOrDefault(r =>
                    r.ReservationId == reservation.ReservationId &&
                    r.ReservationDetailId == request.ReservationDetailId);
                if (reservationDetails == null)
                {
                    return FunctionRoomOperationResult.Fail(
                        "Reservation detail not found.", ReservationOperationFailure.NotFound);
                }
            }

            var employee = _context.Employees.FirstOrDefault(e => e.UserId == user.FindFirstValue(ClaimTypes.NameIdentifier));

            var operation = request.action switch
            {
                "Approve" => ReservationOperation.Approve,
                "Reject" => ReservationOperation.Reject,
                "Cancel" => ReservationOperation.Cancel,
                _ => (ReservationOperation?)null
            };
            if (!operation.HasValue)
            {
                return FunctionRoomOperationResult.Fail("Invalid action.");
            }

            if (operation.Value is ReservationOperation.Approve or ReservationOperation.Reject)
            {
                var overdueTransition = await _scheduleStatus.RefreshAsync(reservation, DateTime.Now);
                if (overdueTransition != null)
                {
                    return FunctionRoomOperationResult.Fail(
                        overdueTransition.Message,
                        ReservationOperationFailure.Conflict);
                }
            }

            var authorization = await _authorization.AuthorizeAsync(user, reservation, operation.Value);
            if (!authorization.Succeeded)
            {
                return AuthorizationFailure(authorization);
            }

            if (operation.Value is ReservationOperation.Approve or ReservationOperation.Reject &&
                !await _processingPolicy.IsEligibleAsync(reservation, DateTime.Now))
            {
                return FunctionRoomOperationResult.Fail(
                    "Requests submitted after 4:00 PM can be processed on the next day.",
                    ReservationOperationFailure.Conflict);
            }

            if (operation.Value == ReservationOperation.Cancel &&
                reservation.Status == ReservationStatuses.Cancelled)
            {
                return FunctionRoomOperationResult.Ok(
                    "Request already cancelled",
                    "No additional changes were made.",
                    "info");
            }

            if (operation.Value == ReservationOperation.Cancel &&
                reservationDetails?.StartDate < DateTime.Now.AddHours(1))
            {
                return FunctionRoomOperationResult.Fail(
                    "This request can no longer be cancelled because its schedule starts in less than one hour.");
            }

            if (operation.Value == ReservationOperation.Reject &&
                string.IsNullOrWhiteSpace(request.Remarks))
            {
                return FunctionRoomOperationResult.Fail(
                    "Remarks are required when rejecting a reservation.");
            }

            var previousStatus = reservation.Status;

            if (request.action == "Approve" && reservation.Status == "FOR DEPARTMENT HEAD'S APPROVAL")
            {
                reservation.Status = authorization.NextStatus;
                _context.SaveChanges();
                await _audit.AddAsync(reservation, user, "APPROVE", previousStatus, reservation.Status);
                return FunctionRoomOperationResult.Ok("Approved!", "Reservation has been approved", "success");
            }

            if (request.action == "Approve" &&
                reservation.Status is "FOR ADMIN APPROVER'S APPROVAL" or "FOR ADMIN MANAGER'S APPROVAL")
            {
                if (reservationDetails == null ||
                    !request._facilityId.HasValue || !request._roomId.HasValue ||
                    !reservationDetails.StartDate.HasValue || !reservationDetails.EndDate.HasValue)
                    return FunctionRoomOperationResult.Fail("Select a room and a valid reservation schedule.");

                var selectedRoom = _context.Rooms
                    .AsNoTracking()
                    .Where(room => room.RoomId == request._roomId.Value)
                    .Select(room => new { room.RoomName, room.Capacity })
                    .FirstOrDefault();
                var capacityWarning = selectedRoom != null &&
                    reservationDetails.NumberOfGuest > selectedRoom.Capacity
                        ? $"The request has {reservationDetails.NumberOfGuest} attendees, which exceeds " +
                          $"{selectedRoom.RoomName}'s capacity of {selectedRoom.Capacity}. The assignment was saved as requested."
                        : null;

                var assignmentCommand = new ReservationAssignmentCommand(
                    new ReservationSlotRequest(
                        reservation.ReservationId,
                        reservationDetails.ReservationDetailId,
                        request._facilityId.Value,
                        request._roomId.Value,
                        ReservationType,
                        reservationDetails.StartDate.Value,
                        reservationDetails.EndDate.Value,
                        reservationDetails.NumberOfGuest,
                        FunctionRoomType),
                    previousStatus!,
                    reservation.RowVersion.ToArray(),
                    reservationDetails.RowVersion.ToArray(),
                    authorization.NextStatus,
                    employee?.Fullname,
                    ParseEquipmentIds(request.EquipmentNeeded));

                FunctionRoomSwapResult? swap = null;
                ReservationCommitResult committed;
                if (request._swapWithReservationDetailId.HasValue)
                {
                    if (reservation.Status != ReservationStatuses.AdminManagerApproval)
                        return FunctionRoomOperationResult.Fail(
                            "Only the Admin Manager can swap two occupied Function Room assignments.",
                            ReservationOperationFailure.Forbidden);

                    swap = await _assignments.SwapFunctionRoomAsync(new FunctionRoomSwapCommand(
                        assignmentCommand,
                        request._swapWithReservationDetailId.Value));
                    committed = swap.Commit;
                }
                else
                {
                    committed = await _assignments.AssignAsync(assignmentCommand);
                }
                if (!committed.Succeeded) return AssignmentFailure(committed);
                reservation = committed.Reservation!;
                reservationDetails = committed.Detail!;
                _context.SaveChanges();
                if (swap?.ConflictingReservation != null && swap.ConflictingDetail != null)
                {
                    await _audit.AddAsync(
                        reservation, user, "SWAP_ASSIGNMENT", previousStatus, reservation.Status,
                        $"Swapped with reservation {swap.ConflictingReservation.ReservationCode ?? swap.ConflictingReservation.ReservationId.ToString()}; " +
                        $"new room ID {reservationDetails.RoomId}.");
                    await _audit.AddAsync(
                        swap.ConflictingReservation, user, "SWAP_ASSIGNMENT",
                        swap.ConflictingReservation.Status, swap.ConflictingReservation.Status,
                        $"Room reassigned during manager swap with reservation {reservation.ReservationCode ?? reservation.ReservationId.ToString()}; " +
                        $"new room ID {swap.ConflictingDetail.RoomId}.");
                }
                else
                {
                    await _audit.AddAsync(reservation, user, "ASSIGN", previousStatus, reservation.Status);
                }
                await _audit.AddAsync(reservation, user, "APPROVE", previousStatus, reservation.Status);
                if (capacityWarning != null)
                {
                    return FunctionRoomOperationResult.Ok(
                        "Approved with a capacity warning",
                        capacityWarning,
                        "warning");
                }

                return FunctionRoomOperationResult.Ok(
                    reservation.Status == ReservationStatuses.Approved ? "Approved!" : "Facility and room assigned!",
                    reservation.Status == ReservationStatuses.Approved
                        ? "The final room assignment and reservation were approved."
                        : "The facility and room were saved with this approval.",
                    "success");
            }

            if (request.action == "Reject")
            {
                reservation.Status = "DISAPPROVED";
                reservation.Remarks = request.Remarks!.Trim();
                reservation.ApprovedBy = employee?.Fullname;
                _context.SaveChanges();
                await _audit.AddAsync(reservation, user, "REJECT", previousStatus, reservation.Status);
                await _emailService.SendDisapprovalNotification(reservation, reservation.RequestorEmail);
                return FunctionRoomOperationResult.Ok(
                    "Reservation disapproved",
                    "The request status and remarks were saved.",
                    "success");
            }

            if (request.action == "Cancel")
            {
                reservation.Status = ReservationStatuses.Cancelled;
                reservation.Remarks = request.Remarks;
                await _equipmentAllocations.ReleaseReservationAsync(reservation.ReservationId);
                _context.SaveChanges();
                await _audit.AddAsync(reservation, user, "CANCEL", previousStatus, reservation.Status);
                return FunctionRoomOperationResult.Ok(
                    "Reservation request cancelled",
                    "The request was removed from active approvals and room schedules.",
                    "success");
            }

            return FunctionRoomOperationResult.Fail("Invalid action.");
        }

        private static void ValidateSqlDateRange(DateTime startDate, DateTime endDate)
        {
            if (!IsWithinSqlDateRange(startDate, endDate))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }
        }

        private static bool IsWithinSqlDateRange(DateTime startDate, DateTime endDate)
        {
            var minDate = new DateTime(1753, 1, 1);
            var maxDate = new DateTime(9999, 12, 31);
            return startDate >= minDate && startDate <= maxDate &&
                   endDate >= minDate && endDate <= maxDate;
        }

        private static FunctionRoomOperationResult ValidationFailure(
            string? redirectGuid,
            string message,
            string subMessage)
        {
            return new FunctionRoomOperationResult
            {
                Success = false,
                Failure = ReservationOperationFailure.Validation,
                ErrorMessage = subMessage,
                RedirectGuid = redirectGuid,
                AlertMessage = message,
                AlertSubMessage = subMessage,
                AlertType = "Validation error"
            };
        }

        private async Task<string> GetSubmittedStatusAsync(
            ReservationActor actor,
            GFMS_ReservationHeader reservation)
        {
            return await _authorization.GetSubmissionStatusAsync(reservation, actor);
        }

        private async Task AddSubmissionAuditAsync(
            GFMS_ReservationHeader reservation,
            ClaimsPrincipal user,
            string? previousStatus,
            string action)
        {
            var bypassed = reservation.Status == ReservationStatuses.AdminApproverApproval &&
                await _authorization.ShouldBypassDepartmentHeadAsync(reservation);
            await _audit.AddAsync(
                reservation,
                user,
                bypassed ? $"{action}_DEPARTMENT_HEAD_BYPASSED" : action,
                previousStatus,
                reservation.Status,
                bypassed
                    ? "Department Head review was not required because the assigned approver also holds the Admin Manager role."
                    : null);
        }

        private bool IsValidFunctionRoomSelection(int? facilityId, int? roomId)
        {
            return facilityId.HasValue &&
                roomId.HasValue &&
                facilityId.Value > 0 &&
                roomId.Value > 0 &&
                _context.Rooms
                    .AsNoTracking()
                    .Any(room =>
                        room.RoomId == roomId.Value &&
                        room.FacilityId == facilityId.Value &&
                        room.RoomType == FunctionRoomType);
        }

        private bool IsAvailableFunctionRoomSelection(int? facilityId, int? roomId, GFMS_ReservationDetails reservationDetails)
        {
            if (!IsValidFunctionRoomSelection(facilityId, roomId) ||
                !reservationDetails.StartDate.HasValue ||
                !reservationDetails.EndDate.HasValue)
            {
                return false;
            }

            return !HasOverlappingFunctionRoomReservation(
                roomId!.Value,
                reservationDetails.StartDate.Value,
                reservationDetails.EndDate.Value,
                reservationDetails.ReservationDetailId);
        }

        private bool HasOverlappingFunctionRoomReservation(
            int roomId,
            DateTime requestStartDateTime,
            DateTime requestEndDateTime,
            int currentReservationDetailId)
        {
            var activeStatuses = new[]
            {
                "FOR DEPARTMENT HEAD'S APPROVAL",
                "FOR ADMIN APPROVER'S APPROVAL",
                "FOR ADMIN MANAGER'S APPROVAL",
                "APPROVED"
            };

            return _context.ReservationDetails
                .AsNoTracking()
                .Join(
                    _context.ReservationHeaders.AsNoTracking(),
                    detail => detail.ReservationId,
                    header => header.ReservationId,
                    (detail, header) => new { detail, header })
                .Any(x =>
                    x.detail.ReservationDetailId != currentReservationDetailId &&
                    x.detail.RoomId == roomId &&
                    x.header.ReservationType == ReservationType &&
                    activeStatuses.Contains(x.header.Status) &&
                    x.detail.StartDate.HasValue &&
                    x.detail.EndDate.HasValue &&
                    x.detail.StartDate.Value < requestEndDateTime &&
                    x.detail.EndDate.Value > requestStartDateTime);
        }

        private (DateTime StartDate, DateTime EndDate)? GetRequestedSchedule(int reservationDetailId)
        {
            var detail = _context.ReservationDetails
                .AsNoTracking()
                .Where(rd => rd.ReservationDetailId == reservationDetailId)
                .Select(rd => new { rd.StartDate, rd.EndDate })
                .FirstOrDefault();

            if (detail?.StartDate == null || detail.EndDate == null)
            {
                return null;
            }

            return (detail.StartDate.Value, detail.EndDate.Value);
        }

        private static bool CanAssignFunctionRoom(ClaimsPrincipal user)
        {
            return user.IsInRole("GFMS.Admin Approver") || user.IsInRole("GFMS.Admin Manager");
        }

        private static FunctionRoomOperationResult AuthorizationFailure(
            ReservationAuthorizationResult authorization) =>
            authorization.Failure == ReservationAuthorizationFailure.Forbidden
                ? FunctionRoomOperationResult.Fail(
                    "Access denied.", ReservationOperationFailure.Forbidden)
                : FunctionRoomOperationResult.Fail(
                    "The reservation state changed. Refresh and try again.",
                    ReservationOperationFailure.Conflict);

        private static FunctionRoomOperationResult AssignmentFailure(ReservationCommitResult result) =>
            FunctionRoomOperationResult.Fail(
                result.Message ?? "The room could not be assigned.",
                result.Failure switch
                {
                    ReservationCommitFailure.NotFound => ReservationOperationFailure.NotFound,
                    ReservationCommitFailure.Conflict => ReservationOperationFailure.Conflict,
                    _ => ReservationOperationFailure.Validation
                });

        private static List<int> ParseEquipmentIds(string? equipmentNeeded)
        {
            if (string.IsNullOrWhiteSpace(equipmentNeeded))
            {
                return new List<int>();
            }

            return equipmentNeeded
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => int.TryParse(e.Trim(), out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }

    }
}
