using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Net.NetworkInformation;
using System;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Services;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using static Kendo.Mvc.UI.UIPrimitives;
using Microsoft.Reporting.Map.WebForms.BingMaps;
using PropertyManagementSystem.Attributes;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize(
        ReservationRoles.Requestor,
        ReservationRoles.DepartmentHead,
        ReservationRoles.AdminManager,
        ReservationRoles.AdminApprover,
        ReservationRoles.SystemAdmin)]
    public class AccommodationController : Controller
    {
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

        public AccommodationController(
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
        public IActionResult GetFacility()
        {
            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClassification = c.FacilityClassification })
                .Where(p => p.FacilityClassification == "GUESTHOUSE")
                .OrderBy(o => o.FacilityName).ToList());
        }
        public IActionResult GetRoom(int? facilityId)
        {
            Console.WriteLine("Facility Id: " + facilityId);
            var rooms = _context.Rooms.AsQueryable();
            if (facilityId.HasValue)
            {
                rooms = rooms.Where(p => p.FacilityId == facilityId.Value);
            }
            return Json(rooms.Select(p => new { RoomId = p.RoomId, RoomName = p.RoomName }).ToList());
        }
        public IActionResult Index()
        {
            var rooms = _context.Rooms
           .Include(r => r.Facility)
           .Where(x => x.RoomType == 0)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 0))
           .Select(facility => new GFMS_Facility
           {
               FacilityId = facility.FacilityId,
               FacilityName = facility.FacilityName,
           })
           .ToList();

            var now = DateTime.Now;
            var pendingReservationCount = _context.ReservationHeaders.Count(r =>
                r.ReservationType == "GH" &&
                (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL" ||
                 r.Status == "FOR ADMIN MANAGER'S APPROVAL" ||
                 r.Status == "FOR ADMIN APPROVER'S APPROVAL"));

            var currentStays =
                from detail in _context.ReservationDetails
                join header in _context.ReservationHeaders on detail.ReservationId equals header.ReservationId
                where header.ReservationType == "GH"
                      && header.Status == "APPROVED"
                      && detail.StartDate.HasValue
                      && detail.EndDate.HasValue
                      && detail.StartDate.Value <= now
                      && detail.EndDate.Value >= now
                select new
                {
                    header.ReservationId,
                    detail.ReservationDetailId,
                    detail.RoomId
                };

            var activeGuestCount = currentStays
                .Select(stay => stay.ReservationDetailId)
                .Distinct()
                .Count();
            var occupiedRoomsCount = currentStays
                .Where(stay => stay.RoomId.HasValue)
                .Select(stay => stay.RoomId)
                .Distinct()
                .Count();
            var activeRoomCount = _context.Rooms.Count(room => room.RoomType == 0 && room.isActive);


            var viewModel = new ReservationViewModel
            {
                NewGHReservationsCount = pendingReservationCount,
                ActiveGuestCount = activeGuestCount,
                OccupiedRoomsCount = occupiedRoomsCount,
                AvailableRoomsCount = Math.Max(activeRoomCount - occupiedRoomsCount, 0)
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }
        public IActionResult Guesthouse()
        {
            return View();
        }

        public async Task<IActionResult> ReservationList()
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var employee = await _roleService.GetUserName(User);
            var requestorEmail = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            ViewData["ReservationYears"] = await ReservationHistoryYearService.GetAvailableYearsAsync(
                _context, "GH", requestorEmail, employee?.Email, employee?.Fullname);

            return View();

        }
        [HttpGet]
        public async Task<IActionResult> ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var actor = await _authorization.GetActorAsync(User);
            var visibleHeaders = _context.ReservationHeaders
                .AsNoTracking()
                .Where(header => header.ReservationType == "GH")
                .ForCalendarViewer(actor.ActiveRole, actor.Email, actor.EmployeeEmail);

            var reservationsQuery = from resDetails in _context.ReservationDetails.AsNoTracking()
                                    join resHeader in visibleHeaders on resDetails.ReservationId equals resHeader.ReservationId
                                    join room in _context.Rooms on resDetails.RoomId equals room.RoomId
                                    join facility in _context.Facilities on resDetails.FacilityId equals facility.FacilityId
                                    select new
                                    {
                                        resDetails.ReservationId,
                                        resHeader.GUID,
                                        resHeader.Requestor,
                                        resHeader.RequestorEmail,
                                        resHeader.ChargeToCompany,
                                        resHeader.Status,
                                        resHeader.ReservationType,
                                        resDetails.StartDate,
                                        resDetails.EndDate,
                                        resDetails.Purpose,
                                        resDetails.RoomId,
                                        resDetails.FacilityId,
                                        RoomName = room.RoomName,
                                        FacilityName = facility.FacilityName
                                    };

            var reservationList = reservationsQuery.ToList();

            var groupedReservations = reservationList.GroupBy(r => r.ReservationId)
                                                     .Select(group => {
                                                         var firstItem = group.First();
                                                         DateTime startDate;
                                                         DateTime endDate;

                                                         var Start = firstItem.StartDate.HasValue && DateTime.TryParse(firstItem.StartDate.ToString(), out startDate)
                                                             ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc)
                                                             : DateTime.MinValue;

                                                         var End = firstItem.EndDate.HasValue && DateTime.TryParse(firstItem.EndDate.ToString(), out endDate)
                                                             ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
                                                             : DateTime.MinValue;

                                                         return new ReservationViewModel
                                                         {
                                                             ReservationId = group.Key,
                                                             GUID = firstItem.GUID,
                                                             Requestor = firstItem.Requestor,
                                                             ChargeToCompany = firstItem.ChargeToCompany,
                                                             Status = firstItem.Status,
                                                             Start = Start,
                                                             End = End,
                                                             Title = firstItem.Purpose,
                                                             Purpose = firstItem.Purpose,
                                                             StartDate = firstItem.StartDate.HasValue ? firstItem.StartDate.Value.ToShortTimeString() : "",
                                                             RoomId = firstItem.RoomId ?? 0,
                                                             FacilityId = firstItem.FacilityId ?? 0,
                                                             RoomName = firstItem.RoomName,
                                                             FacilityName = firstItem.FacilityName,
                                                         };
                                                     }).ToList();

            return Json(groupedReservations.ToDataSourceResult(request));
        }


        [HttpPost]
        public async Task<IActionResult> CreateReservation(int _company, string _requestor, string _requestorEmail, int _department)
        {
            var actor = await _authorization.GetActorAsync(User);
            if (!await _authorization.CanCreateAsync(User)) return Forbid();
            var userId = actor.UserId;
            Console.WriteLine("User Id: " + userId);
            Console.WriteLine("Department Id: " + _department);

            if (string.IsNullOrWhiteSpace(actor.EmployeeEmail) ||
                string.IsNullOrWhiteSpace(actor.EmployeeName) ||
                !actor.DepartmentId.HasValue ||
                !actor.CompanyId.HasValue)
            {
                return Forbid();
            }

            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader()
            {
                GUID = unique,
                ChargeToCompany = actor.CompanyId.Value,
                Requestor = actor.EmployeeName,
                RequestorEmail = actor.EmployeeEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = null,
                Status = "DRAFT",
                Department = actor.DepartmentId.Value,
                ReservationType = "GH"
            };

            var submissionStatus = await _authorization.GetSubmissionStatusAsync(_reservation, actor);
            if (submissionStatus == ReservationStatuses.DepartmentHeadApproval &&
                string.IsNullOrWhiteSpace(_reservation.ApproverEmail))
                return NotFound("Department Head not configured for the authenticated employee's department.");

            await _reservationCodes.CreateAsync(_reservation, "RSQ-GH");

            return RedirectToAction("ViewReservationDetails", new { Id = unique });
        }
        public async Task<IActionResult> ViewReservationDetails(string Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return BadRequest("GUID cannot be null or empty");
            }

            try
            {
                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }
                if (reservationHeader.ReservationType != "GH") return NotFound();
                await _scheduleStatus.RefreshAsync(reservationHeader, DateTime.Now);
                var authorization = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.Read);
                if (!authorization.Succeeded) return Forbid();

                int reservationId = reservationHeader.ReservationId;

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();

                Gen_Department department = null;


                if (reservationDetails.Any())
                {
                    foreach (var detail in reservationDetails)
                    {
                        // Fetch facility if FacilityId is valid
                        if (detail.FacilityId > 0)
                        {
                            detail.Facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == detail.FacilityId);
                        }

                        // Fetch room if RoomId is valid
                        if (detail.RoomId > 0)
                        {
                            detail.Room = _context.Rooms.FirstOrDefault(r => r.RoomId == detail.RoomId);
                        }
                    }
                }

                department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);

                var availableRoomsByDetail = new Dictionary<int, List<RoomAssignmentOption>>();
                var occupiedRoomsByDetail = new Dictionary<int, List<OccupiedRoomAssignmentOption>>();
                foreach (var detail in reservationDetails)
                {
                    availableRoomsByDetail[detail.ReservationDetailId] =
                        await _assignments.GetAvailableRoomsAsync(
                            detail.ReservationDetailId, "GH");
                    occupiedRoomsByDetail[detail.ReservationDetailId] =
                        await _assignments.GetOccupiedRoomsAsync(
                            detail.ReservationDetailId, "GH");
                }

                var reservation = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Department = department,
                    ReservationId = reservationId,
                    AvailableRoomAssignmentsByDetail = availableRoomsByDetail,
                    OccupiedRoomAssignmentsByDetail = occupiedRoomsByDetail
                };
                var equipment = _equipmentAllocations.GetForReservation(reservationId);
                reservation.IncludedEquipments = equipment.Where(item => item.IsAutomatic).ToList();
                reservation.AdditionalEquipments = equipment.Where(item => !item.IsAutomatic).ToList();

                return View(reservation);
            }
            catch (InvalidCastException ex)
            {

                Console.WriteLine($"InvalidCastException: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return RedirectToAction("Error", "Error");
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Exception: {ex.Message}");
                return RedirectToAction("Error", "Error");
            }
        }

        public async Task<IActionResult> CreateReservationDetails(int _reservationId, string[] _guests, string[] _company, string[] _relationship, DateTime _startDate, DateTime _endDate, string _purpose)
        {
            Console.WriteLine($"Start Date: {_startDate}");
            Console.WriteLine($"End Date: {_endDate}");

            var reservationHeader = _context.ReservationHeaders
                .FirstOrDefault(rh => rh.ReservationId == _reservationId && rh.ReservationType == "GH");
            if (reservationHeader == null) return NotFound();
            var authorization = await _authorization.AuthorizeAsync(
                User, reservationHeader, ReservationOperation.CreateDetails);
            var denied = AuthorizationFailure(authorization);
            if (denied != null) return denied;
            var guid = reservationHeader.GUID;

            if (!IsValidSqlSchedule(_startDate, _endDate))
            {
                return GuestScheduleValidationFailure(
                    guid,
                    "Select valid arrival and departure dates.");
            }

            if (_guests == null || _company == null || _relationship == null ||
                _guests.Length == 0 || _guests.Length != _company.Length ||
                _guests.Length != _relationship.Length)
            {
                return GuestCompanyValidationFailure(
                    guid,
                    "Complete the guest name, company, and relationship fields for every guest.");
            }

            var normalizedCompanies = await NormalizeGuestCompaniesAsync(_company);
            if (normalizedCompanies == null)
            {
                return GuestCompanyValidationFailure(
                    guid,
                    "Select a company from the list or choose N/A for every guest.");
            }

            if (_endDate <= _startDate)
            {
                return GuestScheduleValidationFailure(
                    guid,
                    "Departure date and time must be later than arrival date and time.");
            }

            try
            {
                var previousStatus = reservationHeader.Status;
                var submissionStatus = await _authorization.GetSubmissionStatusAsync(reservationHeader, await _authorization.GetActorAsync(User));
                if (_guests != null && _company != null && _relationship != null)
                {
                    int length = Math.Min(Math.Min(_guests.Length, _company.Length), _relationship.Length);
                    Console.WriteLine("length:" + length);
                    for (int i = 0; i < length; i++)
                    {
                        GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                        {
                            ReservationId = _reservationId,
                            GuestName = _guests[i],
                            Company = normalizedCompanies[i],
                            Relationship = _relationship[i],
                            StartDate = _startDate,
                            EndDate = _endDate,
                            Purpose = _purpose,
                            DateAdded = DateTime.UtcNow,
                        };

                        _context.ReservationDetails.Add(reservationDetails);
                        if (reservationHeader != null)
                        {
                            reservationHeader.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
                            reservationHeader.Status = submissionStatus;
                        }
                    }

                    _context.SaveChanges();
                    await AddSubmissionAuditAsync(
                        reservationHeader, previousStatus, "SUBMIT");
                    TempData["AlertMessage"] = "Reservation details created successfully!";
                    TempData["AlertSubMessage"] = "Your reservation has been saved";
                    TempData["AlertType"] = "success";
                }
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while saving changes: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                TempData["AlertMessage"] = "An error occurred while saving changes.";
                TempData["AlertSubMessage"] = "Something went wrong. Please try again.";
                TempData["AlertType"] = "error";
                throw;
            }
            return RedirectToAction("ReservationList", "Accommodation");
        }

        [HttpGet]
        public async Task<ActionResult> getGuestHouseReservation(
            [DataSourceRequest] DataSourceRequest? request,
            string? gridType = "myReservation",
            int? year = null)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var employee = await _roleService.GetUserName(User);
            var assignedRoles = await _roleService.GetRolesAsync(User);
            var requestorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name
                ?? string.Empty;
            var employeeEmail = employee?.Email ?? string.Empty;
            request ??= new DataSourceRequest();
            gridType = string.IsNullOrWhiteSpace(gridType) ? "myReservation" : gridType;
            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH")
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
            var reservationViewModels = new List<ReservationViewModel>();
            foreach (var header in reservationHeaders)
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList();

                var viewModel = new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    ReservationCode = header.ReservationCode,
                    Status = header.Status,
                    GUID = header.GUID,
                    CanDelete = header.Status == "DRAFT",
                    ChargeToCompany = header.ChargeToCompany,
                    ReservationType = header.ReservationType,
                    DepartmentName = department?.DepartmentCode,
                    ReservationDetailId = details.FirstOrDefault()?.ReservationDetailId ?? 0
                };

                reservationViewModels.Add(viewModel);
            }

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.ReservationCode,
                model.Status,
                model.CanDelete,
                model.GUID,
                model.ChargeToCompany,
                model.ReservationType,
                model.ReservationDetailId
            });
            return Json(result);
        }

        private static int? NormalizeReservationYear(int? year)
        {
            if (year == 0) return null;
            return year is >= 2000 && year <= 9999 ? year : DateTime.Today.Year;
        }

        public async Task<ActionResult> getGHApprovals([DataSourceRequest] DataSourceRequest request, int reservationId)
        {
            var requestedHeader = _context.ReservationHeaders
                .FirstOrDefault(x => x.ReservationId == reservationId && x.ReservationType == "GH");
            if (requestedHeader == null) return NotFound();
            var access = await _authorization.AuthorizeAsync(User, requestedHeader, ReservationOperation.Read);
            if (!access.Succeeded) return Forbid();

            var activeRole = await _roleService.GetActiveRoleAsync(User);

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH");

            if (activeRole == ReservationRoles.AdminApprover)
            {
                query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" || x.Status == "RESERVED");
            }
            else if (activeRole == ReservationRoles.AdminManager)
            {
                query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL");
            }
            else if (activeRole != ReservationRoles.SystemAdmin)
            {
                return Forbid();
            }

            // You can now use the reservationId parameter directly to filter
            var reservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == reservationId) // Filter based on the selected ReservationId
                .ToList();

            var reservationViewModels = new List<ReservationViewModel>();

            foreach (var detail in reservationDetails)
            {
                var header = query.FirstOrDefault(x => x.ReservationId == detail.ReservationId);
                if (header == null) continue;

                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == detail.FacilityId);
                var room = _context.Rooms.FirstOrDefault(r => r.RoomId == detail.RoomId);

                var viewModel = new ReservationViewModel
                {
                    ReservationDetailId = detail.ReservationDetailId,
                    GuestName = detail.GuestName,
                    Company = detail.Company,
                    Relationship = detail.Relationship,
                    StartDate = detail.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    EndDate = detail.EndDate ?? DateTime.MinValue,
                    FacilityId = detail.FacilityId ?? 0,
                    FacilityName = facility != null ? facility.FacilityName : "Not assigned",
                    RoomId = detail.RoomId ?? 0,
                    RoomName = room != null ? room.RoomName : "Not assigned"
                };

                reservationViewModels.Add(viewModel);
            }

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationDetailId,
                model.GuestName,
                model.Company,
                model.Relationship,
                model.StartDate,
                model.EndDate,
                model.FacilityId,
                model.FacilityName,
                model.RoomId,
                model.RoomName,
            });

            return Json(result);
        }


        /*Update*/
        [HttpPost]
        public async Task<IActionResult> UpdateFacility([DataSourceRequest] DataSourceRequest request, ReservationViewModel model)
        {
            if (model != null && ModelState.IsValid)
            {
                var reservationDetail = _context.ReservationDetails.FirstOrDefault(r => r.ReservationDetailId == model.ReservationDetailId);
                if (reservationDetail != null)
                {
                    var header = _context.ReservationHeaders.FirstOrDefault(h =>
                        h.ReservationId == reservationDetail.ReservationId && h.ReservationType == "GH");
                    if (header == null) return NotFound();
                    var authorization = await _authorization.AuthorizeAsync(
                        User, header, ReservationOperation.Assign);
                    var denied = AuthorizationFailure(authorization);
                    if (denied != null) return denied;
                    var facilityValidation = await _assignments.ValidateFacilityAsync(
                        model.FacilityId, "GH");
                    var facilityFailure = CommitFailure(facilityValidation);
                    if (facilityFailure != null) return facilityFailure;
                    reservationDetail.FacilityId = model.FacilityId;
                    _context.SaveChanges();
                    await _audit.AddAsync(header, User, "ASSIGN", header.Status, header.Status,
                        $"ReservationDetailId={reservationDetail.ReservationDetailId}");
                }
                else return NotFound();
            }
            return Json(new[] { model }.ToDataSourceResult(request, ModelState));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateReservationDetails(int ReservationId, List<string> _guests, List<string> _company, List<string> _relationship, DateTime startDate, DateTime endDate, string purpose)
        {

            var header = _context.ReservationHeaders
                .FirstOrDefault(h => h.ReservationId == ReservationId && h.ReservationType == "GH");
            if (header == null) return NotFound();
            var authorization = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Edit);
            var denied = AuthorizationFailure(authorization);
            if (denied != null) return denied;
            var previousStatus = header.Status;
            var isResubmission = previousStatus == ReservationStatuses.ForRescheduling;
            var guid = header.GUID;
            purpose = purpose?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > 50)
            {
                TempData["AlertMessage"] = "Enter a valid reservation purpose.";
                TempData["AlertSubMessage"] = "Purpose is required and must not exceed 50 characters.";
                TempData["AlertType"] = "warning";
                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }

            if (!IsValidSqlSchedule(startDate, endDate))
            {
                return GuestScheduleValidationFailure(
                    guid,
                    "Select valid arrival and departure dates.");
            }

            if (endDate <= startDate)
            {
                return GuestScheduleValidationFailure(
                    guid,
                    "Departure date and time must be later than arrival date and time.");
            }

            if (isResubmission && startDate <= DateTime.Now)
            {
                TempData["AlertMessage"] = "Choose a future reservation schedule.";
                TempData["AlertSubMessage"] = "The revised start date and time must be later than the current time.";
                TempData["AlertType"] = "warning";
                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }

            Console.WriteLine("Reservation Id: " + ReservationId);
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == ReservationId)
                .ToList();

            if (_guests == null || _company == null || _relationship == null ||
                _guests.Count == 0 || _guests.Count != _company.Count ||
                _guests.Count != _relationship.Count)
            {
                return GuestCompanyValidationFailure(
                    guid,
                    "Complete the guest name, company, and relationship fields for every guest.");
            }

            var normalizedCompanies = await NormalizeGuestCompaniesAsync(
                _company,
                existingReservationDetails.Select(detail => detail.Company));
            if (normalizedCompanies == null)
            {
                return GuestCompanyValidationFailure(
                    guid,
                    "Select a company from the list or choose N/A for every guest.");
            }

            if (!isResubmission && existingReservationDetails.Any(d => d.RoomId.HasValue || d.FacilityId.HasValue))
            {
                var rescheduled = await _assignments.RescheduleAsync(
                    ReservationId,
                    "GH",
                    header.Status!,
                    header.RowVersion.ToArray(),
                    startDate,
                    endDate);
                var rescheduleFailure = CommitFailure(rescheduled);
                if (rescheduleFailure != null) return rescheduleFailure;
                header = rescheduled.Reservation!;
            }

            for (int i = 0; i < existingReservationDetails.Count; i++)
            {
                var existingDetail = existingReservationDetails[i];


                var index = _guests.FindIndex(g => string.Equals(g, existingDetail.GuestName, StringComparison.OrdinalIgnoreCase));


                if (index != -1)
                {
                    existingDetail.GuestName = _guests[index];
                    existingDetail.Company = normalizedCompanies[index];
                    existingDetail.Relationship = _relationship[index];
                    existingDetail.StartDate = startDate;
                    existingDetail.EndDate = endDate;
                    existingDetail.Purpose = purpose;
                    if (isResubmission)
                    {
                        existingDetail.FacilityId = null;
                        existingDetail.RoomId = null;
                    }


                    _context.ReservationDetails.Update(existingDetail);
                }
                else
                {
                    _context.ReservationDetails.Remove(existingDetail);
                }
            }


            for (int i = 0; i < _guests.Count; i++)
            {
                var existingDetail = existingReservationDetails.FirstOrDefault(rd => string.Equals(rd.GuestName, _guests[i], StringComparison.OrdinalIgnoreCase));
                if (existingDetail == null)
                {
                    GFMS_ReservationDetails newDetail = new GFMS_ReservationDetails
                    {
                        ReservationId = ReservationId,
                        GuestName = _guests[i],
                        Company = normalizedCompanies[i],
                        Relationship = _relationship[i],
                        StartDate = startDate,
                        EndDate = endDate,
                        Purpose = purpose
                    };

                    _context.ReservationDetails.Add(newDetail);
                }
            }
            TempData["AlertMessage"] = "Reservation details updated successfully!";
            TempData["AlertSubMessage"] = "Your reservation has been updated";
            TempData["AlertType"] = "success";
            if (isResubmission)
            {
                header.Status = await _authorization.GetSubmissionStatusAsync(header, await _authorization.GetActorAsync(User));
                header.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
                header.Remarks = null;
                header.ApprovedBy = null;
                TempData["AlertMessage"] = "Reservation resubmitted successfully!";
                TempData["AlertSubMessage"] = "The revised schedule was returned to the first approval stage.";
            }
            _context.SaveChanges();
            if (isResubmission)
            {
                await AddSubmissionAuditAsync(
                    header, previousStatus, "RESCHEDULE_AND_RESUBMIT");
            }
            else
            {
                await _audit.AddAsync(
                    header, User, "EDIT", previousStatus, header.Status);
            }
            return RedirectToAction("ReservationList", "Accommodation");
        }

        private async Task AddSubmissionAuditAsync(
            GFMS_ReservationHeader reservation,
            string? previousStatus,
            string action)
        {
            var bypassed = reservation.Status == ReservationStatuses.AdminApproverApproval &&
                await _authorization.ShouldBypassDepartmentHeadAsync(reservation);
            await _audit.AddAsync(
                reservation,
                User,
                bypassed ? $"{action}_DEPARTMENT_HEAD_BYPASSED" : action,
                previousStatus,
                reservation.Status,
                bypassed
                    ? "Department Head review was not required because the assigned approver also holds the Admin Manager role."
                    : null);
        }

        /*Delete*/
        [HttpPost]
        public async Task<IActionResult> DeleteReservationDetail(int reservationId)
        {
            try
            {
                Console.WriteLine(reservationId);

                var reservationHeader = _context.ReservationHeaders
                    .FirstOrDefault(rd => rd.ReservationId == reservationId);

                if (reservationHeader == null)
                {
                    return NotFound("Reservation detail not found.");
                }
                if (reservationHeader.ReservationType != "GH") return NotFound();
                var authorization = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.Delete);
                var denied = AuthorizationFailure(authorization);
                if (denied != null) return denied;

                var previousStatus = reservationHeader.Status;
                reservationHeader.Status = ReservationStatuses.Deleted;
                _context.SaveChanges();
                await _audit.AddAsync(
                    reservationHeader, User, "DELETE", previousStatus, reservationHeader.Status);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReservation(
            int ReservationId,
            string HeaderRowVersion,
            string? Remarks)
        {
            var reservation = await _context.ReservationHeaders.FirstOrDefaultAsync(header =>
                header.ReservationId == ReservationId && header.ReservationType == "GH");
            if (reservation == null) return NotFound();

            var authorization = await _authorization.AuthorizeAsync(
                User, reservation, ReservationOperation.Cancel);
            if (!authorization.Succeeded)
            {
                return authorization.Failure == ReservationAuthorizationFailure.Forbidden
                    ? Forbid()
                    : RedirectCancellationFailure(
                        reservation.GUID,
                        "This reservation can no longer be cancelled in its current state.");
            }

            if (reservation.Status == ReservationStatuses.Cancelled)
            {
                TempData["AlertMessage"] = "Reservation already cancelled";
                TempData["AlertSubMessage"] = "No additional changes were made.";
                TempData["AlertType"] = "warning";
                return RedirectToAction("ViewReservationDetails", new { id = reservation.GUID });
            }

            var firstStart = await _context.ReservationDetails
                .Where(detail => detail.ReservationId == ReservationId && detail.StartDate.HasValue)
                .MinAsync(detail => detail.StartDate);
            if (firstStart.HasValue && firstStart.Value < DateTime.Now.AddHours(1))
            {
                return RedirectCancellationFailure(
                    reservation.GUID,
                    "Online cancellation closes one hour before arrival. Contact the property administrator for assistance.");
            }

            try
            {
                _context.Entry(reservation).Property(item => item.RowVersion).OriginalValue =
                    Convert.FromBase64String(HeaderRowVersion);
                var previousStatus = reservation.Status;
                reservation.Status = ReservationStatuses.Cancelled;
                reservation.Remarks = string.IsNullOrWhiteSpace(Remarks)
                    ? "Cancelled by requestor."
                    : Remarks.Trim()[..Math.Min(Remarks.Trim().Length, 50)];
                reservation.ApprovedBy = null;
                await _equipmentAllocations.ReleaseReservationAsync(reservation.ReservationId);
                await _context.SaveChangesAsync();
                await _audit.AddAsync(
                    reservation,
                    User,
                    "CANCEL",
                    previousStatus,
                    reservation.Status,
                    reservation.Remarks);
            }
            catch (FormatException)
            {
                return RedirectCancellationFailure(
                    reservation.GUID,
                    "The reservation changed. Refresh the page and try again.");
            }
            catch (DbUpdateConcurrencyException)
            {
                return RedirectCancellationFailure(
                    reservation.GUID,
                    "The reservation changed while cancellation was being processed. Refresh and try again.");
            }

            TempData["AlertMessage"] = "Reservation cancelled";
            TempData["AlertSubMessage"] = "The assigned guesthouse rooms have been released.";
            TempData["AlertType"] = "success";
            return RedirectToAction("ReservationList", "Accommodation");
        }

        private IActionResult RedirectCancellationFailure(string? guid, string message)
        {
            TempData["AlertMessage"] = "Unable to cancel reservation";
            TempData["AlertSubMessage"] = message;
            TempData["AlertType"] = "warning";
            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignGuestRoom(
            int ReservationId,
            int ReservationDetailId,
            int RoomId,
            int? ConflictingReservationDetailId,
            string HeaderRowVersion,
            string DetailRowVersion,
            string? ConflictingDetailRowVersion)
        {
            var reservation = await _context.ReservationHeaders.FirstOrDefaultAsync(header =>
                header.ReservationId == ReservationId && header.ReservationType == "GH");
            var detail = await _context.ReservationDetails.FirstOrDefaultAsync(item =>
                item.ReservationDetailId == ReservationDetailId && item.ReservationId == ReservationId);
            var room = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(item => item.RoomId == RoomId);
            if (reservation == null || detail == null || room == null) return NotFound();

            var actor = await _authorization.GetActorAsync(User);
            if (actor.ActiveRole != ReservationRoles.AdminManager) return Forbid();
            if (reservation.Status != ReservationStatuses.AdminManagerApproval)
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "This reservation is no longer awaiting Admin Manager review.");
            if (!detail.StartDate.HasValue || !detail.EndDate.HasValue)
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "The reservation schedule is incomplete.");

            byte[] expectedHeaderVersion;
            byte[] expectedDetailVersion;
            try
            {
                expectedHeaderVersion = Convert.FromBase64String(HeaderRowVersion);
                expectedDetailVersion = Convert.FromBase64String(DetailRowVersion);
            }
            catch (FormatException)
            {
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "The reservation changed. Refresh and try again.");
            }

            var command = new ReservationAssignmentCommand(
                new ReservationSlotRequest(
                    reservation.ReservationId,
                    detail.ReservationDetailId,
                    room.FacilityId,
                    room.RoomId,
                    "GH",
                    detail.StartDate.Value,
                    detail.EndDate.Value,
                    Math.Max(detail.NumberOfGuest, 1),
                    ReservationRoomTypes.Guesthouse),
                reservation.Status,
                expectedHeaderVersion,
                expectedDetailVersion);

            ReservationCommitResult committed;
            GuestRoomSwapResult? swap = null;
            if (ConflictingReservationDetailId.HasValue)
            {
                byte[] expectedConflictVersion;
                try
                {
                    expectedConflictVersion = Convert.FromBase64String(ConflictingDetailRowVersion ?? string.Empty);
                }
                catch (FormatException)
                {
                    return RedirectRoomAssignmentFailure(reservation.GUID,
                        "The occupied-room information changed. Refresh and try again.");
                }

                swap = await _assignments.SwapGuestRoomAsync(new GuestRoomSwapCommand(
                    command,
                    ConflictingReservationDetailId.Value,
                    expectedConflictVersion));
                committed = swap.Commit;
            }
            else
            {
                committed = await _assignments.AssignAsync(command);
            }

            if (!committed.Succeeded)
                return RedirectRoomAssignmentFailure(reservation.GUID, committed.Message);

            if (swap?.ConflictingReservation != null && swap.ConflictingDetail != null)
            {
                await _audit.AddAsync(reservation, User, "SWAP_ASSIGNMENT", reservation.Status, reservation.Status,
                    $"Guest detail {detail.ReservationDetailId} swapped with " +
                    $"{swap.ConflictingReservation.ReservationCode ?? swap.ConflictingReservation.ReservationId.ToString()}." );
                await _audit.AddAsync(swap.ConflictingReservation, User, "SWAP_ASSIGNMENT",
                    swap.ConflictingReservation.Status, swap.ConflictingReservation.Status,
                    $"Guest detail {swap.ConflictingDetail.ReservationDetailId} swapped with " +
                    $"{reservation.ReservationCode ?? reservation.ReservationId.ToString()}." );
                TempData["AlertMessage"] = "Rooms swapped successfully";
                TempData["AlertSubMessage"] = "Both guest assignments were updated together. Review the assignments before final approval.";
            }
            else
            {
                await _audit.AddAsync(reservation, User, "REASSIGN", reservation.Status, reservation.Status,
                    $"Guest detail {detail.ReservationDetailId} moved to room {room.RoomId}.");
                TempData["AlertMessage"] = "Room reassigned successfully";
                TempData["AlertSubMessage"] = "The new room is conflict-free. Review the assignment before final approval.";
            }
            TempData["AlertType"] = "success";
            return RedirectToAction("ViewReservationDetails", new { id = reservation.GUID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnGuesthouseForRescheduling(
            int ReservationId,
            string HeaderRowVersion)
        {
            var reservation = await _context.ReservationHeaders.FirstOrDefaultAsync(header =>
                header.ReservationId == ReservationId && header.ReservationType == "GH");
            if (reservation == null) return NotFound();

            var actor = await _authorization.GetActorAsync(User);
            if (actor.ActiveRole != ReservationRoles.AdminManager) return Forbid();
            if (reservation.Status != ReservationStatuses.AdminManagerApproval)
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "This reservation is no longer awaiting Admin Manager review.");

            try
            {
                _context.Entry(reservation).Property(item => item.RowVersion).OriginalValue =
                    Convert.FromBase64String(HeaderRowVersion);
                var previousStatus = reservation.Status;
                reservation.Status = ReservationStatuses.ForRescheduling;
                reservation.Remarks = "Room unavailable; please reschedule.";
                reservation.ApprovedBy = actor.EmployeeName;
                var details = await _context.ReservationDetails
                    .Where(detail => detail.ReservationId == ReservationId)
                    .ToListAsync();
                foreach (var item in details)
                {
                    item.RoomId = null;
                    item.FacilityId = null;
                }
                await _context.SaveChangesAsync();
                await _audit.AddAsync(reservation, User, "RETURN_FOR_RESCHEDULING",
                    previousStatus, reservation.Status, reservation.Remarks);
            }
            catch (FormatException)
            {
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "The reservation changed. Refresh and try again.");
            }
            catch (DbUpdateConcurrencyException)
            {
                return RedirectRoomAssignmentFailure(reservation.GUID,
                    "The reservation changed while it was being processed. Refresh and try again.");
            }

            TempData["AlertMessage"] = "Returned for rescheduling";
            TempData["AlertSubMessage"] = "The requestor can now select a new schedule and resubmit the request.";
            TempData["AlertType"] = "warning";
            return RedirectToAction("ReservationList", "Accommodation");
        }

        private IActionResult RedirectRoomAssignmentFailure(string? guid, string? message)
        {
            TempData["AlertMessage"] = "Room reassignment could not be completed";
            TempData["AlertSubMessage"] = message ?? "Review the room assignments and try again.";
            TempData["AlertType"] = "warning";
            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatus(
            int ReservationId,
            string action,
            string Remarks,
            int _facilityId,
            int _roomId,
            int ReservationDetailId,
            List<GuestRoomAssignmentInput>? Assignments)
        {
            var reservation = _context.ReservationHeaders
                .FirstOrDefault(r => r.ReservationId == ReservationId && r.ReservationType == "GH");
            if (reservation == null) return NotFound();

            var actor = await _authorization.GetActorAsync(User);
            var previousStatus = reservation.Status;

            var operation = action switch
            {
                "Approve" => ReservationOperation.Approve,
                "Reject" => ReservationOperation.Reject,
                _ => (ReservationOperation?)null
            };
            if (!operation.HasValue) return BadRequest("Invalid action.");

            var overdueTransition = await _scheduleStatus.RefreshAsync(reservation, DateTime.Now);
            if (overdueTransition != null)
            {
                TempData["AlertMessage"] = "This request is no longer eligible for approval.";
                TempData["AlertSubMessage"] = overdueTransition.Message;
                TempData["AlertType"] = "warning";
                return RedirectToAction("ReservationList", "Accommodation");
            }

            var authorization = await _authorization.AuthorizeAsync(User, reservation, operation.Value);
            var failure = AuthorizationFailure(authorization);
            if (failure != null) return failure;

            if (action == "Reject" && string.IsNullOrWhiteSpace(Remarks))
            {
                return BadRequest("Remarks are required when rejecting a reservation.");
            }

            if (!await _processingPolicy.IsEligibleAsync(reservation, DateTime.Now))
            {
                return Conflict("Requests submitted after 4:00 PM can be processed on the next day.");
            }

            if (action == "Approve" &&
                previousStatus is ReservationStatuses.AdminApproverApproval or ReservationStatuses.AdminManagerApproval)
            {
                IReadOnlyCollection<ReservationRoomAssignmentSelection>? softAssignments = null;
                if (previousStatus is ReservationStatuses.AdminApproverApproval or ReservationStatuses.AdminManagerApproval)
                {
                    softAssignments = (Assignments ?? new List<GuestRoomAssignmentInput>())
                        .Select(item => new ReservationRoomAssignmentSelection(
                            item.ReservationDetailId,
                            item.RoomId))
                        .ToList();
                }

                var committed = await _assignments.ApproveAssignedReservationAsync(
                    ReservationId,
                    "GH",
                    previousStatus,
                    reservation.RowVersion.ToArray(),
                    authorization.NextStatus!,
                    actor.EmployeeName,
                    softAssignments);
                if (!committed.Succeeded)
                {
                    TempData["AlertMessage"] = "Room assignment unavailable";
                    TempData["AlertSubMessage"] = committed.Failure == ReservationCommitFailure.Conflict
                        ? $"{committed.Message} Select another available room, then try approving again."
                        : committed.Message;
                    TempData["AlertType"] = "warning";
                    return RedirectToAction("ViewReservationDetails", new { id = reservation.GUID });
                }
                reservation = committed.Reservation!;

                if (softAssignments != null)
                {
                    await _audit.AddAsync(
                        reservation,
                        User,
                        "ASSIGN",
                        previousStatus,
                        reservation.Status,
                        $"Assignments={string.Join(",", softAssignments.Select(item => $"{item.ReservationDetailId}:{item.RoomId}"))}");
                }
            }
            else
            {
                reservation.Status = authorization.NextStatus;
                reservation.ApprovedBy = actor.EmployeeName;
                if (action == "Reject") reservation.Remarks = Remarks.Trim();

                try
                {
                    _context.SaveChanges();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Conflict("The reservation state changed. Refresh and try again.");
                }
            }
            await _audit.AddAsync(
                reservation,
                User,
                action == "Approve" ? "APPROVE" : "REJECT",
                previousStatus,
                reservation.Status);

            TempData["AlertMessage"] = action == "Approve" ? "Approved!" : "Rejected";
            TempData["AlertSubMessage"] = "Reservation status has been updated";
            TempData["AlertType"] = action == "Approve" ? "success" : "warning";
            return RedirectToAction("ReservationList", "Accommodation");
        }

        private IActionResult? AuthorizationFailure(ReservationAuthorizationResult authorization)
        {
            if (authorization.Succeeded) return null;
            return authorization.Failure == ReservationAuthorizationFailure.Forbidden
                ? Forbid()
                : Conflict("The reservation state changed. Refresh and try again.");
        }

        private IActionResult? CommitFailure(ReservationCommitResult result)
        {
            if (result.Succeeded) return null;
            return result.Failure switch
            {
                ReservationCommitFailure.NotFound => NotFound(result.Message),
                ReservationCommitFailure.Conflict => Conflict(result.Message),
                _ => BadRequest(result.Message)
            };
        }

        private async Task<List<string>?> NormalizeGuestCompaniesAsync(
            IReadOnlyCollection<string> submittedCompanies,
            IEnumerable<string?>? legacyCompanies = null)
        {
            var canonicalCompanies = await _context.Companies
                .AsNoTracking()
                .Where(company => company.Company != null && company.Company != "")
                .Select(company => company.Company.Trim())
                .ToListAsync();
            var allowed = canonicalCompanies
                .GroupBy(company => company, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            allowed["N/A"] = "N/A";

            var legacy = (legacyCompanies ?? Enumerable.Empty<string?>())
                .Where(company => !string.IsNullOrWhiteSpace(company))
                .Select(company => company!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normalized = new List<string>(submittedCompanies.Count);
            foreach (var submitted in submittedCompanies)
            {
                var value = submitted?.Trim();
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (allowed.TryGetValue(value, out var canonical))
                {
                    normalized.Add(canonical);
                }
                else if (legacy.Contains(value))
                {
                    normalized.Add(value);
                }
                else
                {
                    return null;
                }
            }

            return normalized;
        }

        private IActionResult GuestCompanyValidationFailure(string? guid, string message)
        {
            TempData["AlertMessage"] = "Review the guest company selections.";
            TempData["AlertSubMessage"] = message;
            TempData["AlertType"] = "warning";
            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        private static bool IsValidSqlSchedule(DateTime arrival, DateTime departure)
        {
            var minimum = new DateTime(1753, 1, 1);
            var maximum = new DateTime(9999, 12, 31);
            return arrival >= minimum && arrival <= maximum &&
                departure >= minimum && departure <= maximum;
        }

        private IActionResult GuestScheduleValidationFailure(string? guid, string message)
        {
            TempData["AlertMessage"] = "Review the reservation schedule.";
            TempData["AlertSubMessage"] = message;
            TempData["AlertType"] = "warning";
            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }
    }
}
