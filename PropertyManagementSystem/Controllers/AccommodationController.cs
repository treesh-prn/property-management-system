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
using Org.BouncyCastle.Asn1.Cmp;
using Newtonsoft.Json;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class AccommodationController : Controller
    {
        private readonly DAL _context;
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;

        public AccommodationController(DAL context, IRoleService roleService, EmailService emailService, EmployeeService employeeService)
        {
            _context = context;
            _roleService = roleService;
            _emailService = emailService;
            _employeeService = employeeService;
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

            var newReservationCount = _context.ReservationHeaders.Count(r => r.Status == "PENDING" || r.Status == "DRAFT");
            var occupiedRoomsCount = _context.ReservationHeaders.Count(r => r.Status == "APPROVED");
            var availableRoomsCount = _context.ReservationHeaders.Count(r => r.Status == "PENDING");


            var viewModel = new ReservationViewModel
            {
                //NewReservationsCount = newReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }
        public IActionResult Guesthouse()
        {
            return View();
        }

        public IActionResult ReservationList()
        {

            return View();

        }
        public async Task<IActionResult> ViewReservationAsync([DataSourceRequest] DataSourceRequest request)
        {
            var currUser = User.Identity.Name;
            Console.WriteLine("Current User: " + currUser);

            string currentRole = await _roleService.GetActiveRoleAsync(User);

            bool isRequestor = currentRole == "GFMS.Requestor";
            bool isDeptHead = currentRole == "GFMS.Department Head";

            var reservationsQuery = from resDetails in _context.ReservationDetails
                                    join resHeader in _context.ReservationHeaders on resDetails.ReservationId equals resHeader.ReservationId
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
                                        resDetails.RecurrenceRule,
                                        resDetails.Purpose,
                                        resDetails.RoomId,
                                        resDetails.FacilityId,
                                        RoomName = room.RoomName,
                                        FacilityName = facility.FacilityName
                                    };

            if (isRequestor || isDeptHead)
            {
                reservationsQuery = reservationsQuery.Where(r => r.RequestorEmail == currUser && r.Status != "DISAPPROVED");
            }
            reservationsQuery = reservationsQuery.Where(r => r.ReservationType == "GH");
            var reservationList = reservationsQuery.ToList();

            var groupedReservations = reservationList.GroupBy(r => r.ReservationId)
                                                     .Select(group =>
                                                     {
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
                                                             StartDate = firstItem.StartDate,
                                                             RoomId = firstItem.RoomId,
                                                             RecurrenceRule = firstItem.RecurrenceRule,
                                                             FacilityId = firstItem.FacilityId,
                                                             RoomName = firstItem.RoomName,
                                                             FacilityName = firstItem.FacilityName,
                                                         };
                                                     }).ToList();

            return Json(groupedReservations.ToDataSourceResult(request));
        }


        private string GenerateReservationCode()
        {
            int currentYear = DateTime.Now.Year;
            int sequenceNumber = 1;

            var lastReservation = _context.ReservationHeaders
                .Where(r => r.ReservationType == "GH")
                .OrderByDescending(r => r.DateSubmitted)
                .FirstOrDefault();

            if (lastReservation != null)
            {
                string[] codeParts = lastReservation.ReservationCode?.Split('-');
                if (codeParts?.Length == 4 &&
                    int.TryParse(codeParts[2], out int lastYear) &&
                    int.TryParse(codeParts[3], out int lastSequenceNumber))
                {
                    if (lastYear == currentYear)
                    {
                        sequenceNumber = lastSequenceNumber + 1;
                    }
                }
            }

            return $"RSQ-GH-{currentYear}-{sequenceNumber:D4}";
        }

        [HttpPost]
        public async Task<IActionResult> CreateReservation(int _company, string _requestor, string _requestorEmail, int _department, bool _isPersonal, string _employeeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Console.WriteLine("User Id: " + userId);
            Console.WriteLine("Department Id: " + _department);

            var (managerEmail, managerFullName) = await _employeeService.GetManagerInfo(userId);

            if (managerEmail == null || managerFullName == null)
            {
                return NotFound("Manager not found for the given department.");
            }

            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            var reservationCode = GenerateReservationCode();

            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader()
            {
                GUID = unique,
                ChargeToCompany = _company,
                Requestor = _requestor,
                RequestorID = _employeeId,
                RequestorEmail = _requestorEmail,
                Approver = managerFullName,
                ApproverEmail = managerEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = "DRAFT",
                Department = _department,
                ReservationCode = reservationCode,
                ReservationType = "GH",
                isPersonal = _isPersonal
            };

            _context.ReservationHeaders.Add(_reservation);
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewReservationDetails", new { Id = unique });
        }
        public IActionResult ViewReservationDetails(string Id)
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
                var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);

                var reservation = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Employees = employee,
                    Department = department,
                    ReservationId = reservationId,
                };

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

        public async Task<IActionResult> CreateReservationDetailsAsync(int _reservationId, string[] _guests, string[] _company, string[] _relationship, DateTime _startDate, DateTime _endDate, string RecurrenceRule, string _purpose)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);


            Console.WriteLine($"Start Date: {_startDate}");
            Console.WriteLine($"End Date: {_endDate}");

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            var guid = _context.ReservationHeaders.Where(rh => rh.ReservationId == _reservationId).Select(rh => rh.GUID).FirstOrDefault();
            DateTime now = DateTime.Now;

            if (_endDate < _startDate)
            {
                TempData["AlertMessage"] = "End date cannot be earlier than the start date.";
                TempData["AlertSubMessage"] = "Please select a valid end date.";
                TempData["AlertType"] = "Warning";

                TempData["_startDate"] = _startDate.ToString("yyyy-MM-ddTHH:mm:ss");
                TempData["_endDate"] = _endDate.ToString("yyyy-MM-ddTHH:mm:ss");
                TempData["_purpose"] = _purpose;
                TempData["_recurrenceRule"] = RecurrenceRule;
                TempData["_guests"] = JsonConvert.SerializeObject(_guests);
                TempData["_company"] = JsonConvert.SerializeObject(_company);
                TempData["_relationship"] = JsonConvert.SerializeObject(_relationship);

                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }
            else if (_startDate.Date == now.Date)
            {
                if (currentRole == ("GFMS.Requestor") && !User.IsInRole("GFMS.Admin Approver"))
                {
                    TempData["AlertMessage"] = "Requestors cannot file a reservation for the same day.";
                    TempData["AlertSubMessage"] = "Please select a future date.";
                    TempData["AlertType"] = "Warning";

                    TempData["_startDate"] = _startDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    TempData["_endDate"] = _endDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    TempData["_purpose"] = _purpose;
                    TempData["_recurrenceRule"] = RecurrenceRule;
                    TempData["_guests"] = JsonConvert.SerializeObject(_guests);
                    TempData["_company"] = JsonConvert.SerializeObject(_company);
                    TempData["_relationship"] = JsonConvert.SerializeObject(_relationship);

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
                else if (_startDate <= now && !User.IsInRole("GFMS.Admin Approver"))
                {
                    TempData["AlertMessage"] = "You cannot file a reservation for the same day if the start time has already passed.";
                    TempData["AlertSubMessage"] = "Please select a future start time.";
                    TempData["AlertType"] = "Warning";

                    TempData["_startDate"] = _startDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    TempData["_endDate"] = _endDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    TempData["_purpose"] = _purpose;
                    TempData["_recurrenceRule"] = RecurrenceRule;
                    TempData["_guests"] = JsonConvert.SerializeObject(_guests);
                    TempData["_company"] = JsonConvert.SerializeObject(_company);
                    TempData["_relationship"] = JsonConvert.SerializeObject(_relationship);

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
            }

            try
            {
                if (_guests != null && _company != null && _relationship != null)
                {
                    int length = Math.Min(Math.Min(_guests.Length, _company.Length), _relationship.Length);
                    Console.WriteLine("length:" + length);
                    for (int i = 0; i < length; i++)
                    {
                        GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                        {
                            ReservationId = _reservationId,
                            GuestName = _guests[i].ToUpper(),
                            Company = _company[i].ToUpper(),
                            Relationship = _relationship[i].ToUpper(),
                            StartDate = _startDate,
                            EndDate = _endDate,
                            RecurrenceRule = RecurrenceRule,
                            Purpose = _purpose,
                            DateAdded = DateTime.UtcNow,
                        };

                        _context.ReservationDetails.Add(reservationDetails);
                        var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == _reservationId);
                        var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);
                        if (reservationHeader != null)
                        {
                            if (User.IsInRole("GFMS.Admin Approver") && currentRole == "GFMS.Requestor")
                            {
                                reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";

                            }
                            else if (currentRole == "GFMS.Requestor" && employee.DepartmentId == 33)
                            {
                                reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                                
                            }
                            else if (User.IsInRole("GFMS.Admin Manager") && currentRole == "GFMS.Requestor")
                            {
                                reservationHeader.Status = "APPROVED";

                            }
                            else if (User.IsInRole("GFMS.Department Head") && currentRole == "GFMS.Requestor")
                            {
                                reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";

                            }
                            else if (currentRole == "GFMS.Requestor")
                            {
                                reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";
                            }
                            else
                            {
                                reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";

                            }
                        }
                    }
                    _context.SaveChanges();
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

        public async Task<ActionResult> getGuestHouseReservationAsync([DataSourceRequest] DataSourceRequest request, string gridType)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH")
                .Where(x => x.Status != "DELETED");

            if (gridType == "personal")
            {
                query = query.Where(x => x.Requestor == employee.Fullname && x.isPersonal == true);
            }
            else if (gridType == "corporate")
            {
                query = query.Where(x => x.Requestor == employee.Fullname && x.isPersonal == false);
            }
            else if (gridType == "companyRequest")
            {
                if (currentRole == ("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL" && x.isPersonal == false);
                }
                else if (currentRole == ("GFMS.Admin Approver"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" && x.isPersonal == false);
                }
                else if (currentRole == ("GFMS.Admin Manager"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL" && x.isPersonal == false);
                }
            }
            else if (gridType == "personalRequest")
            {
                if (currentRole == ("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL" && x.isPersonal == true);
                }
                else if (currentRole == ("GFMS.Admin Approver"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" && x.isPersonal == true);
                }
                else if (currentRole == ("GFMS.Admin Manager"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL" && x.isPersonal == true);
                }

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

        public async Task<ActionResult> getGHApprovalsAsync([DataSourceRequest] DataSourceRequest request, int reservationId)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);

            var employee = employeeTask.Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH");

            if (currentRole == ("GFMS.Admin Approver"))
            {
                query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" || x.Status == "RESERVED");
            }
            else if (currentRole == ("GFMS.Admin Manager"))
            {
                query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL");
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
                    StartDate = detail.StartDate,
                    EndDate = detail.EndDate ?? DateTime.MinValue,
                    FacilityId = detail.FacilityId,
                    FacilityName = facility != null ? facility.FacilityName : "Not assigned",
                    RoomId = detail.RoomId,
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
        public IActionResult UpdateReservationDetails(int ReservationId, List<string> _guests, List<string> _company, List<string> _relationship, DateTime _startDate, DateTime _endDate, string _purpose)
        {
            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == ReservationId);

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }
            var guid = _context.ReservationHeaders.Where(rh => rh.ReservationId == ReservationId).Select(rh => rh.GUID).FirstOrDefault();
            DateTime now = DateTime.Now;

            if (_endDate < _startDate)
            {
                TempData["AlertMessage"] = "End date cannot be earlier than the start date.";
                TempData["AlertSubMessage"] = "Please select a valid end date.";
                TempData["AlertType"] = "Warning";


                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }
            else if (_startDate.Date == now.Date && _startDate < now)
            {
                TempData["AlertMessage"] = "You cannot file a reservation for the same day if the start time has already passed.";
                TempData["AlertSubMessage"] = "Please select a future start time.";
                TempData["AlertType"] = "Warning";

                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }

            Console.WriteLine("Reservation Id: " + ReservationId);
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == ReservationId)
                .ToList();

            for (int i = 0; i < existingReservationDetails.Count; i++)
            {
                var existingDetail = existingReservationDetails[i];


                var index = _guests.FindIndex(g => string.Equals(g, existingDetail.GuestName, StringComparison.OrdinalIgnoreCase));


                if (index != -1)
                {
                    existingDetail.GuestName = _guests[index].ToUpper();
                    existingDetail.Company = _company[index].ToUpper();
                    existingDetail.Relationship = _relationship[index].ToUpper();
                    existingDetail.StartDate = _startDate;
                    existingDetail.EndDate = _endDate;
                    existingDetail.Purpose = _purpose;


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
                        GuestName = _guests[i].ToUpper(),
                        Company = _company[i].ToUpper(),
                        Relationship = _relationship[i].ToUpper(),
                        StartDate = _startDate,
                        EndDate = _endDate,
                        Purpose = _purpose
                    };

                    _context.ReservationDetails.Add(newDetail);
                }
            }

            if (reservationHeader != null)
            {
                var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);

                if (employee != null)
                {
                    var department = _context.Departments.FirstOrDefault(d => d.Id == employee.DepartmentId);
                    var company = department != null
                        ? _context.Companies.FirstOrDefault(c => c.Id == department.CompanyId)
                        : null;

                    if (reservationHeader.Status == "FOR ADMIN APPROVER'S APPROVAL" &&
                        department != null && department.DepartmentCode != "ADMIN" &&
                        company != null && company.CompanyCode != "FDPPC")
                    {
                        reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";
                        _context.ReservationHeaders.Update(reservationHeader);
                    }
                }
            }

            TempData["AlertMessage"] = "Reservation details updated successfully!";
            TempData["AlertSubMessage"] = "Your reservation has been updated";
            TempData["AlertType"] = "Success";
            _context.SaveChanges();
            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        /*Delete*/
        [HttpPost]
        public IActionResult DeleteReservationDetail(int reservationId)
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

                if (reservationHeader != null)
                {
                    reservationHeader.Status = "DELETED";
                }

                _context.SaveChanges();

                return RedirectToAction("Index", "Accommodation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost]
        public async Task<IActionResult> UpdateReservationStatusAsync(int ReservationId, string action, string Remarks, int _facilityId, int _roomId, int ReservationDetailId)
        {
            Console.WriteLine("Reservation Id: " + ReservationId);
            Console.WriteLine("Reservation DetailId: " + ReservationDetailId);
            string currentRole = await _roleService.GetActiveRoleAsync(User);
            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            var reDetails = _context.ReservationDetails.Where(detail => detail.ReservationDetailId == ReservationDetailId);
            var guid = reservation.GUID;
            var empName = _context.Employees.FirstOrDefault(e => e.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (reservation == null || empName == null)
            {
                return BadRequest("Invalid reservation or employee.");
            }

            if (action == "Approve")
            {
                if (reservation.Status == "FOR DEPARTMENT HEAD'S APPROVAL")
                {
                    reservation.Status = "FOR ADMIN APPROVER'S APPROVAL";
                }
                else if (reservation.Status == "FOR ADMIN APPROVER'S APPROVAL")
                {
                    foreach (var detail in reDetails)
                    {
                        if ((detail.FacilityId == 0 || detail.RoomId == 0) || (detail.FacilityId == null || detail.RoomId == null))
                        {
                            TempData["AlertMessage"] = "No Rooms and Facility Assigned";
                            TempData["AlertSubMessage"] = "Select a room and facility for this reservation.";
                            TempData["AlertType"] = "Warning";

                            return RedirectToAction("ViewReservationDetails", new { id = guid });
                        }
                        break;
                    }

                    reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";
                    reservation.ApprovedBy = empName.Fullname;

                    TempData["AlertMessage"] = "Room details assigned successfully!";
                    TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                    TempData["AlertType"] = "success";
                }
                else if (reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
                {
                    foreach (var detail in reDetails)
                    {
                        if ((detail.FacilityId == 0 || detail.RoomId == 0) || (detail.FacilityId == null || detail.RoomId == null))
                        {
                            TempData["AlertMessage"] = "No Rooms and Facility Assigned";
                            TempData["AlertSubMessage"] = "Select a room and facility for this reservation.";
                            TempData["AlertType"] = "Warning";

                            return RedirectToAction("ViewReservationDetails", new { id = guid });
                        }
                        break;
                    }

                    TempData["AlertMessage"] = "Approved!";
                    TempData["AlertSubMessage"] = "Reservation has been approved";
                    TempData["AlertType"] = "success";
                    reservation.Status = "APPROVED";
                    reservation.ApprovedBy = empName.Fullname;
                }
            }
            else if (action == "Reject")
            {
                reservation.Status = "DISAPPROVED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName.Fullname;
            }
            else if (action == "Assign")
            {

                reDetails.FirstOrDefault().FacilityId = _facilityId;
                reDetails.FirstOrDefault().RoomId = _roomId;

                _context.SaveChanges();
                return RedirectToAction("ViewReservationDetails", new { id = reservation.GUID });
            }
            else if (action == "Revert")
            {
                if (reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
                {
                    return RedirectToAction("ReservationList", "Accommodation");
                }
                var resDetails = _context.ReservationDetails
                                    .Where(detail => detail.ReservationId == ReservationId)
                                    .ToList();

                foreach (var detail in resDetails)
                {
                    detail.FacilityId = 0;
                    detail.RoomId = 0;
                }

                _context.SaveChanges();

                return RedirectToAction("ReservationList", "Accommodation");
            }
            else if (action == "Cancel")
            {
                reservation.Status = "CANCELLED";
                reservation.Remarks = Remarks;
            }

            else
            {
                return BadRequest("Invalid action.");
            }

            _context.SaveChanges();

            return RedirectToAction("ReservationList", "Accommodation");
        }
    }
}
