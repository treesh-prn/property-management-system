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
using static Kendo.Mvc.UI.UIPrimitives;
using Microsoft.Reporting.Map.WebForms.BingMaps;

namespace PropertyManagementSystem.Controllers
{
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
        public IActionResult ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            var currUser = User.Identity.Name;
            Console.WriteLine("Current User: " + currUser);
            bool isRequestor = User.IsInRole("GFMS.Requestor");
            bool isDeptHead = User.IsInRole("GFMS.Department Head");

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
                                        resDetails.Purpose,
                                        resDetails.RoomId,
                                        resDetails.FacilityId,
                                        RoomName = room.RoomName,
                                        FacilityName = facility.FacilityName
                                    };

            if (isRequestor || isDeptHead)
            {
                reservationsQuery = reservationsQuery.Where(r => r.RequestorEmail == currUser && r.ReservationType == "GH");
            }
            reservationsQuery = reservationsQuery.Where(r => r.ReservationType == "GH");
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
                                                             RoomId = firstItem.RoomId,
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
        public async Task<IActionResult> CreateReservation(int _company, string _requestor, string _requestorEmail, int _department)
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
                RequestorEmail = _requestorEmail,
                Approver = managerFullName,
                ApproverEmail = managerEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = "DRAFT",
                Department = _department,
                ReservationCode = reservationCode,
                ReservationType = "GH"
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

                var reservation = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
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

        public IActionResult CreateReservationDetails(int _reservationId, string[] _guests, string[] _company, string[] _relationship, DateTime _startDate, DateTime _endDate, string _purpose)
        {
            Console.WriteLine($"Start Date: {_startDate}");
            Console.WriteLine($"End Date: {_endDate}");

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            var guid = _context.ReservationHeaders.Where(rh => rh.ReservationId == _reservationId).Select(rh => rh.GUID).FirstOrDefault();

            if (_endDate < _startDate)
            {
                TempData["AlertMessage"] = "End date cannot be earlier than the start date.";
                TempData["AlertSubMessage"] = "Please select a valid end date.";
                TempData["AlertType"] = "Warning";


                return RedirectToAction("ViewReservationDetails", new { id = guid });
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
                            GuestName = _guests[i],
                            Company = _company[i],
                            Relationship = _relationship[i],
                            StartDate = _startDate,
                            EndDate = _endDate,
                            Purpose = _purpose,
                            DateAdded = DateTime.UtcNow,
                        };

                        _context.ReservationDetails.Add(reservationDetails);
                        var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == _reservationId);
                        if (reservationHeader != null)
                        {
                            if (User.IsInRole("GFMS.Admin Approver"))
                            {
                                reservationHeader.Status = "FOR ADMIN MANAGER'S APPROVAL";
                               
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

        public ActionResult getGuestHouseReservation([DataSourceRequest] DataSourceRequest request, string gridType)
        {
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH")
                .Where(x => x.Status != "DELETED");

            if (gridType == "myReservation")
            {
                query = query.Where(x => x.Requestor == employee.Fullname);
            }
            else if (gridType == "reservationApproval")
            {
                if (User.IsInRole("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL");
                }
                else if (User.IsInRole("GFMS.Admin Approver"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL");
                }
                else if (User.IsInRole("GFMS.Admin Manager"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL");
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

        public ActionResult getGHApprovals([DataSourceRequest] DataSourceRequest request, int reservationId)
        {
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "GH");

            if (User.IsInRole("GFMS.Admin Approver"))
            {
                query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" || x.Status == "RESERVED");
            }
            else if (User.IsInRole("GFMS.Admin Manager"))
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
                    StartDate = detail.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
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
        public ActionResult UpdateFacility([DataSourceRequest] DataSourceRequest request, ReservationViewModel model)
        {
            if (model != null && ModelState.IsValid)
            {
                var reservationDetail = _context.ReservationDetails.FirstOrDefault(r => r.ReservationDetailId == model.ReservationDetailId);
                if (reservationDetail != null)
                {
                    reservationDetail.FacilityId = model.FacilityId;
                    _context.SaveChanges();
                }
            }
            return Json(new[] { model }.ToDataSourceResult(request, ModelState));
        }

        [HttpPost]
        public IActionResult UpdateReservationDetails(int ReservationId, List<string> _guests, List<string> _company, List<string> _relationship, DateTime startDate, DateTime endDate, string purpose)
        {

            if (startDate < new DateTime(1753, 1, 1) || startDate > new DateTime(9999, 12, 31) ||
                endDate < new DateTime(1753, 1, 1) || endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }
            var guid = _context.ReservationHeaders.Where(rh => rh.ReservationId == ReservationId).Select(rh => rh.GUID).FirstOrDefault();

            if (endDate < startDate)
            {
                TempData["AlertMessage"] = "End date cannot be earlier than the start date.";
                TempData["AlertSubMessage"] = "Please select a valid end date.";
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
                    existingDetail.GuestName = _guests[index];
                    existingDetail.Company = _company[index];
                    existingDetail.Relationship = _relationship[index];
                    existingDetail.StartDate = startDate;
                    existingDetail.EndDate = endDate;
                    existingDetail.Purpose = purpose;


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
                        Company = _company[i],
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
            _context.SaveChanges();
            return RedirectToAction("ReservationList", "Accommodation");
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
        public IActionResult UpdateReservationStatus(int ReservationId, string action, string Remarks, int _facilityId, int _roomId, int ReservationDetailId)
        {
            Console.WriteLine("Reservation Id: " + ReservationId);

            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
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
                    

                    reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";
                    reservation.ApprovedBy = empName.Fullname;

                    TempData["AlertMessage"] = "Room details assigned successfully!";
                    TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                    TempData["AlertType"] = "success";
                }
                else if (reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
                {

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
            else if(action == "Assign")
            {
                var reDetails = _context.ReservationDetails.Where(detail => detail.ReservationDetailId == ReservationDetailId);
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
                    var reDetails = _context.ReservationDetails
                                        .Where(detail => detail.ReservationId == ReservationId)
                                        .ToList(); 

                foreach (var detail in reDetails)
                {
                    detail.FacilityId = 0;
                    detail.RoomId = 0;
                }

                _context.SaveChanges(); // Save changes after modifying records

                return RedirectToAction("ReservationList", "Accommodation");
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
