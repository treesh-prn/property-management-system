    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
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
            private readonly ILogger<HomeController> _logger;
            private readonly IRoleService _roleService;
            private readonly UserManager<ApplicationUser> _userManager;
            private readonly SignInManager<ApplicationUser> _signInManager;
            private readonly DAL _context;
            private readonly EmployeeService _employeeService;

            public HomeController(ILogger<HomeController> logger, DAL context, UserManager<ApplicationUser> userManager, EmployeeService employeeService, IRoleService roleService, SignInManager<ApplicationUser> signInManager)
            {
                _logger = logger;
                _context = context;
                _userManager = userManager;
                _employeeService = employeeService;
                _roleService = roleService;
                _signInManager = signInManager;
            }

            public async Task<IActionResult> Index()
            {
                var today = DateTime.Today;
                var cutoffTime = new DateTime(today.Year, today.Month, today.Day, 16, 0, 0);
                var newFNReservationCount = 0;
                var newGHReservationCount = 0;
                var newDRMReservationCount = 0;
                string currentRole = await _roleService.GetActiveRoleAsync(User);

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

                Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
                var employee = employeeTask.Result;
                var query = _context.ReservationHeaders.AsQueryable();
                var roleStatusMap = new Dictionary<string, string>
    {
        { "GFMS.Admin Manager", "FOR ADMIN MANAGER'S APPROVAL" },
        { "GFMS.Admin Approver", "FOR ADMIN APPROVER'S APPROVAL" },
        { "GFMS.Department Head", "FOR DEPARTMENT HEAD'S APPROVAL" }
    };

                var reservationTypes = new[] { "FN", "GH", "DRM" };
                var reservationCounts = new Dictionary<string, int>();

                if (roleStatusMap.ContainsKey(currentRole))
                {
                    string status = roleStatusMap[currentRole];

                    foreach (var type in reservationTypes)
                    {
                        reservationCounts[type] = _context.ReservationHeaders.Count(r =>
                            r.ReservationType == type &&
                            r.Status == status &&
                            r.DateFiled <= cutoffTime);
                    }
                }
                else if (currentRole == "GFMS.Requestor")
                {
                    foreach (var type in reservationTypes)
                    {
                        reservationCounts[type] = _context.ReservationHeaders.Count(r =>
                            r.ReservationType == type &&
                            r.Requestor == employee.Fullname &&
                            r.Status != "DRAFT" &&
                            r.Status != "CANCELLED" &&
                            r.Status != "DISAPPROVED" &&
                            r.Status != "APPROVED");
                    }
                }
                // Assign the results to the respective variables
                newFNReservationCount = reservationCounts["FN"];
                newGHReservationCount = reservationCounts["GH"];
                newDRMReservationCount = reservationCounts["DRM"];


                var todaysBookings = await (from detail in _context.ReservationDetails
                                            where detail.StartDate.HasValue && detail.EndDate.HasValue
                                                  && detail.StartDate.Value.Date <= today
                                                  && detail.EndDate.Value.Date >= today
                                            join header in _context.ReservationHeaders on detail.ReservationId equals header.ReservationId
                                            where header.Status == "APPROVED"
                                            where header.ReservationType != "DRM"
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
