using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using System.Diagnostics;
using System.Security.Claims;



namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private const int FunctionRoomType = 1;
        private static readonly string[] ActiveFunctionRoomStatuses =
        {
            "FOR DEPARTMENT HEAD'S APPROVAL",
            "FOR ADMIN APPROVER'S APPROVAL",
            "FOR ADMIN MANAGER'S APPROVAL",
            "APPROVED"
        };

        private readonly ILogger<HomeController> _logger;
        private readonly IRoleService _roleService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly DAL _context;
        private readonly EmployeeService _employeeService;
        private readonly IReservationScheduleStatusService _scheduleStatus;

        public HomeController(ILogger<HomeController> logger, DAL context, UserManager<ApplicationUser> userManager, EmployeeService employeeService, IRoleService roleService, SignInManager<ApplicationUser> signInManager, IReservationScheduleStatusService scheduleStatus)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
            _employeeService = employeeService;
            _roleService = roleService;
            _signInManager = signInManager;
            _scheduleStatus = scheduleStatus;
        }

        public async Task<IActionResult> Index()
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var today = DateTime.Today;
            var newFNReservationCount = 0;
            var newGHReservationCount = 0;
            var newDRMReservationCount = 0;

            var rooms = _context.Rooms
          .Include(r => r.Facility)
          .Where(x => x.RoomType == 1)
          .Select(room => new GFMS_Rooms
          {
              RoomId = room.RoomId,
              RoomName = room.RoomName,
              RoomType = room.RoomType,
              FacilityId = room.FacilityId
          })
          .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 1))
           .Select(facility => new GFMS_Facility
           {
               FacilityId = facility.FacilityId,
               FacilityName = facility.FacilityName,
           })
            .ToList();

            var employee = await _roleService.GetUserName(User);
            var assignedApprovalRoles = (await _roleService.GetRolesAsync(User))
                .Where(role => role != ReservationRoles.SystemAdmin)
                .ToArray();
            if (assignedApprovalRoles.Any(role => role is
                ReservationRoles.DepartmentHead or
                ReservationRoles.AdminApprover or
                ReservationRoles.AdminManager))
            {
                var identityEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? User.Identity?.Name;

                newFNReservationCount = await _context.ReservationHeaders
                    .Where(r => r.ReservationType == "FN")
                    .ForAssignedApprovalRoles(assignedApprovalRoles, employee, _context.Employees.AsNoTracking(), identityEmail)
                    .CountAsync();

                newGHReservationCount = await _context.ReservationHeaders
                    .Where(r => r.ReservationType == "GH")
                    .ForAssignedApprovalRoles(assignedApprovalRoles, employee, _context.Employees.AsNoTracking(), identityEmail)
                    .CountAsync();

                newDRMReservationCount = await _context.ReservationHeaders
                    .Where(r => r.ReservationType == "DRM")
                    .ForAssignedApprovalRoles(assignedApprovalRoles, employee, _context.Employees.AsNoTracking(), identityEmail)
                    .CountAsync();
            }

            var todaysBookings = await (from detail in _context.ReservationDetails
                                        where detail.StartDate.HasValue && detail.EndDate.HasValue
                                              && detail.StartDate.Value.Date <= today
                                              && detail.EndDate.Value.Date >= today
                                        join header in _context.ReservationHeaders on detail.ReservationId equals header.ReservationId
                                             where header.Status == "APPROVED"
                                        select new
                                        {
                                            header.ReservationId,
                                            header.ReservationType,
                                            detail.StartDate,
                                            detail.EndDate,
                                            detail.Purpose
                                        }).Distinct()
                                 .ToListAsync();

            var viewModel = new ReservationViewModel
            {
                NewFNReservationsCount = newFNReservationCount,
                NewGHReservationsCount = newGHReservationCount,
                NewDRMReservationsCount = newDRMReservationCount,
                TodaysBookings = todaysBookings,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetFunctionRoomDashboardReservations([DataSourceRequest] DataSourceRequest request)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var employee = await _roleService.GetUserName(User);
            var activeRole = await _roleService.GetActiveRoleAsync(User);
            var identityEmail = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;
            var visibleHeaders = _context.ReservationHeaders
                .AsNoTracking()
                .Where(header => header.ReservationType == "FN")
                .ForCalendarViewer(activeRole, identityEmail, employee?.Email);
            var reservations =
                (from detail in _context.ReservationDetails.AsNoTracking()
                 join header in visibleHeaders
                     on detail.ReservationId equals header.ReservationId
                 join room in _context.Rooms.AsNoTracking()
                     on detail.RoomId equals (int?)room.RoomId
                 join facility in _context.Facilities.AsNoTracking()
                     on detail.FacilityId equals (int?)facility.FacilityId
                 where detail.StartDate.HasValue
                       && detail.EndDate.HasValue
                 select new
                 {
                     detail.ReservationDetailId,
                     detail.ReservationId,
                     header.GUID,
                     header.ReservationCode,
                     header.Requestor,
                     header.Status,
                     detail.StartDate,
                     detail.EndDate,
                     detail.Purpose,
                     detail.RoomId,
                     detail.FacilityId,
                     room.RoomName,
                     facility.FacilityName
                 })
                .AsEnumerable()
                .Select(r => new ReservationViewModel
                {
                    ReservationDetailId = r.ReservationDetailId,
                    ReservationId = r.ReservationId,
                    GUID = r.GUID,
                    ReservationCode = r.ReservationCode,
                    Requestor = r.Requestor,
                    Status = r.Status,
                    Start = DateTime.SpecifyKind(r.StartDate!.Value, DateTimeKind.Utc),
                    End = DateTime.SpecifyKind(r.EndDate!.Value, DateTimeKind.Utc),
                    Title = $"{r.ReservationCode} - {r.RoomName}",
                    Purpose = r.Purpose,
                    StartDate = r.StartDate.Value.ToShortTimeString(),
                    RoomId = r.RoomId ?? 0,
                    FacilityId = r.FacilityId ?? 0,
                    RoomName = r.RoomName,
                    FacilityName = r.FacilityName
                })
                .ToList();

            return Json(reservations.ToDataSourceResult(request));
        }

        public IActionResult GetFunctionRoomFacilities()
        {
            var facilities = _context.Facilities
                .AsNoTracking()
                .Where(f => f.Rooms.Any(room => room.RoomType == FunctionRoomType))
                .OrderBy(f => f.FacilityName)
                .Select(f => new
                {
                    f.FacilityId,
                    f.FacilityName
                })
                .ToList();

            return Json(facilities);
        }

        public IActionResult GetFunctionRoomRooms(int? facilityId)
        {
            var rooms = _context.Rooms
                .AsNoTracking()
                .Where(room => room.RoomType == FunctionRoomType);

            if (facilityId.HasValue && facilityId.Value > 0)
            {
                rooms = rooms.Where(room => room.FacilityId == facilityId.Value);
            }

            return Json(rooms
                .OrderBy(room => room.RoomName)
                .Select(room => new
                {
                    room.RoomId,
                    room.RoomName
                })
                .ToList());
        }

        public IActionResult CheckFunctionRoomAvailability(int? facilityId, int? roomId, DateTime? startDateTime, DateTime? endDateTime, int? month, int? year)
        {
            var calendarDate = new DateTime(year ?? DateTime.Today.Year, month ?? DateTime.Today.Month, 1);
            var monthStart = calendarDate;
            var monthEnd = calendarDate.AddMonths(1);
            var hasValidRange = startDateTime.HasValue && endDateTime.HasValue && startDateTime.Value < endDateTime.Value;

            var bookingsQuery =
                from detail in _context.ReservationDetails.AsNoTracking()
                join header in _context.ReservationHeaders.AsNoTracking()
                    on detail.ReservationId equals header.ReservationId
                join room in _context.Rooms.AsNoTracking()
                    on detail.RoomId equals (int?)room.RoomId
                join facility in _context.Facilities.AsNoTracking()
                    on detail.FacilityId equals (int?)facility.FacilityId
                where header.ReservationType == "FN"
                      && ActiveFunctionRoomStatuses.Contains(header.Status)
                      && detail.StartDate.HasValue
                      && detail.EndDate.HasValue
                      && detail.StartDate.Value < monthEnd
                      && detail.EndDate.Value > monthStart
                select new
                {
                    detail.RoomId,
                    detail.FacilityId,
                    header.ReservationCode,
                    header.Status,
                    detail.Purpose,
                    detail.StartDate,
                    detail.EndDate,
                    room.RoomName,
                    facility.FacilityName
                };

            if (facilityId.HasValue && facilityId.Value > 0)
            {
                bookingsQuery = bookingsQuery.Where(x => x.FacilityId == facilityId.Value);
            }

            if (roomId.HasValue && roomId.Value > 0)
            {
                bookingsQuery = bookingsQuery.Where(x => x.RoomId == roomId.Value);
            }

            var bookings = bookingsQuery
                .OrderBy(x => x.StartDate)
                .AsEnumerable()
                .Select(x => new
                {
                    x.ReservationCode,
                    x.Status,
                    x.Purpose,
                    x.RoomName,
                    x.FacilityName,
                    StartDate = x.StartDate!.Value,
                    EndDate = x.EndDate!.Value,
                    Schedule = $"{x.StartDate.Value:MMM dd, h:mm tt} - {x.EndDate.Value:h:mm tt}"
                })
                .ToList();

            var fullyBookedDates = bookings
                .SelectMany(booking => EachDate(booking.StartDate.Date, booking.EndDate.Date))
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            var availableDates = EachDate(monthStart, monthEnd.AddDays(-1))
                .Where(date => !fullyBookedDates.Contains(date))
                .ToList();

            var availableRooms = new List<object>();
            if (facilityId.HasValue && facilityId.Value > 0)
            {
                availableRooms = _context.Rooms
                    .AsNoTracking()
                    .Where(room => room.RoomType == FunctionRoomType && room.FacilityId == facilityId.Value)
                    .OrderBy(room => room.RoomName)
                    .AsEnumerable()
                    .Where(room => !hasValidRange || !HasOverlappingFunctionRoomReservation(room.RoomId, startDateTime!.Value, endDateTime!.Value))
                    .Select(room => new
                    {
                        room.RoomId,
                        room.RoomName
                    })
                    .Cast<object>()
                    .ToList();
            }

            bool? isAvailable = null;
            if (roomId.HasValue && roomId.Value > 0 && hasValidRange)
            {
                isAvailable = !HasOverlappingFunctionRoomReservation(roomId.Value, startDateTime!.Value, endDateTime!.Value);
            }

            return Json(new
            {
                IsAvailable = isAvailable,
                HasValidRange = hasValidRange,
                AvailableRooms = availableRooms,
                FullyBookedDates = fullyBookedDates,
                AvailableDates = availableDates,
                Bookings = bookings
            });
        }

        public IActionResult CheckRoomAvailability(int facilityId, int roomId, int month, int year)
        {
            // Get the bookings for the selected facility and room in the selected month/year
            var bookings = _context.ReservationDetails
                .Where(r => r.FacilityId == facilityId && r.RoomId == roomId)
                .Where(r => r.StartDate.Value.Month == month && r.StartDate.Value.Year == year)
                .Select(r => new
                {
                    r.StartDate,
                    r.EndDate
                }).ToList();

            // Get the list of fully booked dates by checking for overlapping bookings
            var fullyBookedDates = bookings
                .Where(b => IsOverlapping(b.StartDate.Value, b.EndDate.Value))
                .Select(b => b.StartDate.Value.Date) // Just get the date portion for comparison
                .Distinct()
                .ToList();

            // Calculate available dates by excluding fully booked dates
            var availableDates = GetAllDatesInMonth(month, year)
                .Where(date => !fullyBookedDates.Any(fbd => fbd == date.Date))
                .ToList();

            return Json(new
            {
                FullyBookedDates = fullyBookedDates,
                AvailableDates = availableDates,
                Bookings = bookings
            });
        }

        // Helper method to check if a booking overlaps with the requested date
        private bool IsOverlapping(DateTime startDate, DateTime endDate)
        {
            // The requested range (for checking overlap)
            DateTime requestedStart = new DateTime(startDate.Year, startDate.Month, startDate.Day, 7, 0, 0);
            DateTime requestedEnd = new DateTime(startDate.Year, startDate.Month, startDate.Day, 18, 0, 0);

            // Check if the booking overlaps with the requested date range
            return !(endDate <= requestedStart || startDate >= requestedEnd);
        }

        // Helper method to get all dates in the month
        private List<DateTime> GetAllDatesInMonth(int month, int year)
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            var dates = new List<DateTime>();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                dates.Add(date);
            }

            return dates;
        }

        private bool HasOverlappingFunctionRoomReservation(int roomId, DateTime requestStartDateTime, DateTime requestEndDateTime)
        {
            return _context.ReservationDetails
                .AsNoTracking()
                .Join(
                    _context.ReservationHeaders.AsNoTracking(),
                    detail => detail.ReservationId,
                    header => header.ReservationId,
                    (detail, header) => new { detail, header })
                .Any(x =>
                    x.detail.RoomId == roomId &&
                    x.header.ReservationType == "FN" &&
                    ActiveFunctionRoomStatuses.Contains(x.header.Status) &&
                    x.detail.StartDate.HasValue &&
                    x.detail.EndDate.HasValue &&
                    x.detail.StartDate.Value < requestEndDateTime &&
                    x.detail.EndDate.Value > requestStartDateTime);
        }

        private static IEnumerable<DateTime> EachDate(DateTime startDate, DateTime endDate)
        {
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                yield return date;
            }
        }


        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
        
        public async Task<IActionResult> ChangeRole(string role)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrWhiteSpace(role))
            {
                return Forbid();
            }

            var userRoles = await _roleService.GetRolesAsync(User);
            if (!userRoles.Contains(role))
            {
                return Forbid();
            }

            var _currentRole = await _userManager.GetClaimsAsync(user);
            foreach (var claim in _currentRole.Where(c => c.Type == "GFMS.Active Role"))
            {
                await _userManager.RemoveClaimAsync(user, claim);
            }


            var newCurrent = new Claim("GFMS.Active Role", role);

            await _userManager.AddClaimAsync(user, newCurrent);
            await _signInManager.SignOutAsync();
            await _signInManager.SignInAsync(user, isPersistent: false);

            return RedirectToAction("Index", "Home");

        }
    }
}
