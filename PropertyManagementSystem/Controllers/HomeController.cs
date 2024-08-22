using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using System.Diagnostics;



namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IRoleService _roleService;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DAL _context;
        private readonly EmployeeService _employeeService;

        public HomeController(ILogger<HomeController> logger, DAL context, UserManager<IdentityUser> userManager, EmployeeService employeeService, IRoleService roleService)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
            _employeeService = employeeService;
            _roleService = roleService;
        }

        public async Task<IActionResult> Index()
        {
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

            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;
            var query = _context.ReservationHeaders.AsQueryable();
            if(User.IsInRole("GFMS.Admin Manager"))
            {
                newFNReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN" && (r.Status == "FOR ADMIN MANAGER'S APPROVAL"));
                newGHReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "GH" && (r.Status == "FOR ADMIN MANAGER'S APPROVAL"));
                newDRMReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "DRM" && (r.Status == "FOR ADMIN MANAGER'S APPROVAL"));
            }
            else if (User.IsInRole("GFMS.Admin Approver"))
            {
                newFNReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN" && (r.Status == "FOR ADMIN APPROVER'S APPROVAL"));
                newGHReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "GH" && (r.Status == "FOR ADMIN APPROVER'S APPROVAL"));
                newDRMReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "DRM" && (r.Status == "FOR ADMIN APPROVER'S APPROVAL"));
            }
            else if(User.IsInRole("GFMS.Department Head"))
            {
                newFNReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN" && (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL"));
                newGHReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "GH" && (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL"));
                newDRMReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "DRM" && (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL"));
            }


            var viewModel = new ReservationViewModel
            {
                NewFNReservationsCount = newFNReservationCount,
                NewGHReservationsCount = newGHReservationCount,
                NewDRMReservationsCount = newDRMReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
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
    }
}
