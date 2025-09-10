using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using System.Reflection.PortableExecutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System;
using static Kendo.Mvc.UI.UIPrimitives;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Identity;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class FunctionRoomController : Controller
    {
        private readonly DAL _context;
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;
        private readonly RoleManager<IdentityRole> _roleManager;

        public FunctionRoomController(DAL context, IRoleService roleService, EmailService emailService, EmployeeService employeeService, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _roleService = roleService;
            _emailService = emailService;
            _employeeService = employeeService;
            _roleManager = roleManager;
        }
        public IActionResult Index()
        {
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

            var newReservationCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN" && (r.Status == "PENDING" || r.Status == "DRAFT"));
            var occupiedRoomsCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN" && r.Status == "APPROVED");
            var availableRoomsCount = _context.ReservationHeaders.Count(r => r.ReservationType == "FN");

            var viewModel = new ReservationViewModel
            {
                //NewReservationsCount = newReservationCount,
                OccupiedRoomsCount = occupiedRoomsCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);

        }

        public IActionResult FunctionRoom()
        {
            return View();
        }

        public IActionResult ReservationList()
        {
            return View();

        }

        public async Task<IActionResult> ViewReservation([DataSourceRequest] DataSourceRequest request)
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
                                        StartDate = resDetails.StartDate,
                                        EndDate = resDetails.EndDate,
                                        resDetails.RecurrenceRule,
                                        resDetails.Purpose,
                                        resDetails.RoomId,
                                        resDetails.FacilityId,
                                        RoomName = room.RoomName,
                                        FacilityName = facility.FacilityName
                                    };

            if (isRequestor || isDeptHead)
            {
                reservationsQuery = reservationsQuery.Where(r => r.RequestorEmail == currUser && r.ReservationType == "FN");
            }
            reservationsQuery = reservationsQuery.Where(r => r.ReservationType == "FN" && r.Status != "DISAPPROVED");
            var reservations = reservationsQuery.AsEnumerable()
                                                .Select(r => new ReservationViewModel
                                                {
                                                    ReservationId = r.ReservationId,
                                                    GUID = r.GUID,
                                                    Requestor = r.Requestor,
                                                    ChargeToCompany = r.ChargeToCompany,
                                                    Status = r.Status,
                                                    Start = r.StartDate.HasValue && DateTime.TryParse(r.StartDate.ToString(), out DateTime startDate)
                                                            ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc)
                                                            : DateTime.MinValue,
                                                    End = r.EndDate.HasValue && DateTime.TryParse(r.EndDate.ToString(), out DateTime endDate)
                                                          ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
                                                          : DateTime.MinValue,
                                                    Title = r.Purpose,
                                                    Purpose = r.Purpose,
                                                    StartDate = r.StartDate,
                                                    RecurrenceRule = r.RecurrenceRule,
                                                    RoomId = r.RoomId,
                                                    FacilityId = r.FacilityId,
                                                    RoomName = r.RoomName,
                                                    FacilityName = r.FacilityName,
                                                }).ToList();

            return Json(reservations.ToDataSourceResult(request));
        }

        public IActionResult GetFacility()
        {

            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClass = c.FacilityClassification })
                .Where(p => p.FacilityClass == "FUNCTION ROOM")
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

        public async Task<ActionResult> GetFunctionRoomReservationAsync([DataSourceRequest] DataSourceRequest request, string gridType)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);

            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var today = DateTime.Today;
            var targetDateTime = new DateTime(today.Year, today.Month, today.Day, 16, 0, 0);

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "FN")
                .Where(x => x.Status != "DELETED");

            if (gridType == "myReservation")
            {
                query = query.Where(x => x.Requestor == employee.Fullname);
            }
            else if (gridType == "reservationApproval")
            {
                if (currentRole == "GFMS.Department Head")
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
                else if (currentRole == "GFMS.Admin Approver")
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
                else if (currentRole == "GFMS.Admin Manager")
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
            }
            else if (gridType == "approvedReservation")
            {
                query = query.Where(x => x.Status == "APPROVED");
            }

            var reservationHeaders = query.ToList();
            var reservationViewModels = reservationHeaders.Select(header =>
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails
                .Where(rd => rd.ReservationId == header.ReservationId)
                .Where(rd => rd.StartDate < today)
                .ToList();

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
                    ReservationDetailId = details.FirstOrDefault()?.ReservationDetailId ?? 0
                };
            }).ToList();

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
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

            return Json(result);
        }



        private string GenerateReservationCode()
        {
            int currentYear = DateTime.Now.Year;
            int sequenceNumber = 1;

            var lastReservation = _context.ReservationHeaders
                .Where(r => r.ReservationType == "FN")
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

            return $"RSQ-FN-{currentYear}-{sequenceNumber:D4}";
        }



        [HttpPost]
        public async Task<IActionResult> CreateReservation(int _company, string _requestor, string _requestorEmail, int _department, bool _isFreeOfCharge, string _employeeId)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Console.WriteLine("User Id: " + userId);
            Console.WriteLine("Department Id: " + _department);

            var (managerEmail, managerFullName) = await _employeeService.GetManagerInfo(userId);
            var role = await _roleService.GetActiveRoleAsync(User);

            Console.WriteLine("Role :" + role);

            if ((managerEmail == null || managerFullName == null) && currentRole != "GFMS.Admin Manager")
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
                ReservationType = "FN",
                isFreeOfCharge = _isFreeOfCharge
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
                GFMS_Facility facility = null;
                GFMS_Rooms rooms = null;
                List<string> equipmentNames = new List<string>();

                if (reservationDetails.Any())
                {
                    var firstDetail = reservationDetails.FirstOrDefault();
                    facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == firstDetail.FacilityId);
                    rooms = _context.Rooms.FirstOrDefault(r => r.RoomId == firstDetail.RoomId);
                    var equipmentIds = string.IsNullOrEmpty(firstDetail.EquipmentNeeded)
                        ? new List<int>() // Return an empty list if null
                        : firstDetail.EquipmentNeeded
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse)
                            .ToList();

                    equipmentNames = _context.Equipments
                        .Where(e => equipmentIds.Contains(e.Id))
                        .Select(e => e.EquipmentName)
                        .ToList();
                }

                department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);
                var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);
                var reservation = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Employees = employee,
                    Department = department,
                    Facility = facility,
                    Room = rooms,
                    EquipmentNames = equipmentNames
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
        public async Task<IActionResult> CreateReservationDetailsAsync(int _reservationId, int _numOfGuests, int _facilityId, int _roomId, DateTime _startDate, DateTime _endDate, string _purpose, List<string> _equipment, string _contactNumber, string RecurrenceRule, string _requestorEmail)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);

            var approverEmails = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Approver");
            var adminManagerEmail = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Manager");

            Console.WriteLine($"Requestor Email: {_requestorEmail}");
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

                TempData["_numOfGuests"] = _numOfGuests;
                TempData["_facilityId"] = _facilityId;
                TempData["_roomId"] = _roomId;
                TempData["_startDate"] = _startDate;
                TempData["_endDate"] = _endDate;
                TempData["_purpose"] = _purpose;
                TempData["_equipment"] = JsonConvert.SerializeObject(_equipment);
                TempData["_contactNumber"] = _contactNumber;
                TempData["_recurrenceRule"] = RecurrenceRule;


                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }
            else if (_startDate.Date == now.Date)
            {
                if (currentRole == ("GFMS.Requestor") && !User.IsInRole("GFMS.Admin Approver"))
                {
                    TempData["AlertMessage"] = "Requestors cannot file a reservation for the same day.";
                    TempData["AlertSubMessage"] = "Please select a future date.";
                    TempData["AlertType"] = "Warning";

                    TempData["_numOfGuests"] = _numOfGuests;
                    TempData["_facilityId"] = _facilityId;
                    TempData["_roomId"] = _roomId;
                    TempData["_startDate"] = _startDate;
                    TempData["_endDate"] = _endDate;
                    TempData["_purpose"] = _purpose;
                    TempData["_equipment"] = JsonConvert.SerializeObject(_equipment);
                    TempData["_contactNumber"] = _contactNumber;
                    TempData["_recurrenceRule"] = RecurrenceRule;


                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
                else if (_startDate <= now && !User.IsInRole("GFMS.Admin Approver"))
                {
                    TempData["AlertMessage"] = "You cannot file a reservation for the same day if the start time has already passed.";
                    TempData["AlertSubMessage"] = "Please select a future start time.";
                    TempData["AlertType"] = "Warning";

                    TempData["_numOfGuests"] = _numOfGuests;
                    TempData["_facilityId"] = _facilityId;
                    TempData["_roomId"] = _roomId;
                    TempData["_startDate"] = _startDate;
                    TempData["_endDate"] = _endDate;
                    TempData["_purpose"] = _purpose;
                    TempData["_equipment"] = JsonConvert.SerializeObject(_equipment);
                    TempData["_contactNumber"] = _contactNumber;
                    TempData["_recurrenceRule"] = RecurrenceRule;

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
            }
            try
            {
                var equipmentNeeded = _equipment?.Select(e => e.Trim()).ToList();
                Console.WriteLine("equipments: " + string.Join(", ", equipmentNeeded));
                GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                {
                    ReservationId = _reservationId,
                    NumberOfGuest = _numOfGuests,
                    FacilityId = _facilityId,
                    RoomId = _roomId,
                    StartDate = _startDate,
                    RecurrenceRule = RecurrenceRule,
                    EndDate = _endDate,
                    Purpose = _purpose,
                    DateAdded = DateTime.UtcNow,
                    ContactNumber = _contactNumber
                };

                _context.ReservationDetails.Add(reservationDetails);
                _context.SaveChanges();

                if (equipmentNeeded != null && equipmentNeeded.Any())
                {
                    foreach (var eqId in equipmentNeeded)
                    {
                        var reservationEquipment = new GFMS_ReservationEquipment
                        {
                            ReservationDetailId = reservationDetails.ReservationDetailId,
                            EquipmentId = Convert.ToInt32(eqId)
                        };

                        _context.ReservationEquipment.Add(reservationEquipment);
                    }
                    _context.SaveChanges();
                }

                var roomName = _context.Rooms.Where(r => r.RoomId == _roomId).Select(r => r.RoomName).FirstOrDefault();
                var facilityName = _context.Facilities.Where(f => f.FacilityId == _facilityId).Select(f => f.FacilityName).FirstOrDefault();
                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == _reservationId);
                var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);
                var department = _context.Departments.FirstOrDefault(e => e.Id == employee.DepartmentId);
                var company = _context.Companies.FirstOrDefault(e => e.Id == employee.CompanyId);
                var controllerName = "FunctionRoom";

                if (reservationHeader != null)
                {
                    if (User.IsInRole("GFMS.Admin Approver") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                        if (reservationHeader != null)
                        {
                            await _emailService.SendPendingRoomAssignmentEmail(
                                reservationHeader,
                                approverEmails,
                                controllerName,
                                department.DepartmentCode
                                );
                        }

                    }
                    else if (currentRole == "GFMS.Requestor" && department.DepartmentCode == "ADMIN" && company.CompanyCode == "FDPPC")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";

                        if (reservationHeader != null)
                        {
                           await _emailService.SendPendingRoomAssignmentEmail(
                           reservationHeader,
                           approverEmails,
                           controllerName,
                           department.DepartmentCode
                           );

                        }

                    }
                    else if (User.IsInRole("GFMS.Admin Manager") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "APPROVED";

                    }
                    else if (User.IsInRole("GFMS.Department Head") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";

                        if(reservationHeader != null)
                        {
                            await _emailService.SendPendingRoomAssignmentEmail(
                           reservationHeader,
                           approverEmails,
                           controllerName,
                           department.DepartmentCode
                           );

                        }
                    }
                    else
                    {
                        reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";

                        if (reservationHeader != null)
                        {
                            await _emailService.SendFNReservationApprovalEmail(
                                reservationHeader,
                                reservationHeader.ApproverEmail,
                                _startDate.ToString(),
                                _endDate.ToString(),
                                _numOfGuests,
                                facilityName,
                                roomName,
                                _purpose,
                                reservationHeader.GUID,
                                controllerName
                            );
                        }


                    }
                }
                _context.SaveChanges();
                TempData["AlertMessage"] = "Reservation details created successfully!";
                TempData["AlertSubMessage"] = "Your reservation has been saved";
                TempData["AlertType"] = "success";
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
            return RedirectToAction("ReservationList", "FunctionRoom");
        }

        /*EditReservation*/
        [HttpPost]
        public IActionResult EditReservationDetails(int ReservationDetailId, int ReservationId, int NumberOfGuest, int _facilityId, int _roomId, DateTime _startDate, DateTime _endDate, string _purpose, string[] EquipmentNeeded, string _contactNumber, string RecurrenceRule)
        {
            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == ReservationId);

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            var reservationDetail = _context.ReservationDetails.FirstOrDefault(rd => rd.ReservationDetailId == ReservationDetailId);
            if (reservationDetail == null)
            {
                return NotFound("Reservation detail not found.");
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
               if(!User.IsInRole("GFMS.Admin Approver"))
                {
                    TempData["AlertMessage"] = "You cannot file a reservation for the same day if the start time has already passed.";
                    TempData["AlertSubMessage"] = "Please select a future start time.";
                    TempData["AlertType"] = "Warning";

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
            }

            reservationDetail.NumberOfGuest = NumberOfGuest;
            reservationDetail.FacilityId = _facilityId;
            reservationDetail.RoomId = _roomId;
            reservationDetail.StartDate = _startDate;
            reservationDetail.EndDate = _endDate;
            reservationDetail.ContactNumber = _contactNumber;
            reservationDetail.Purpose = _purpose;
            reservationDetail.RecurrenceRule = RecurrenceRule;

            if (EquipmentNeeded != null && EquipmentNeeded.Any())
            {
                reservationDetail.EquipmentNeeded = string.Join(",", EquipmentNeeded.Select(e => e.Trim()));
            }
            else
            {
                reservationDetail.EquipmentNeeded = string.Empty;
            }

            try
            {
                if (reservationHeader != null)
                {
                    var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservationHeader.RequestorID);

                    if (employee != null)
                    {
                        var department = _context.Departments.FirstOrDefault(d => d.Id == employee.DepartmentId);

                        // Assuming department has a CompanyId or similar foreign key
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

                _context.ReservationDetails.Update(reservationDetail);
                _context.SaveChanges();

                TempData["AlertMessage"] = "Reservation details updated successfully!";
                TempData["AlertSubMessage"] = "Your reservation has been updated";
                TempData["AlertType"] = "Success";
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while updating changes: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }


            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        /*Delete*/
        [HttpPost]
        public IActionResult DeleteReservationDetail(int reservationId)
        {
            try
            {
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

                return RedirectToAction("Index", "FunctionRoom");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while updating reservation header status: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatusAsync(int ReservationId, int ReservationDetailId, string action, int _facilityId, int _roomId, string Remarks, string EquipmentNeeded)
        {
            Console.WriteLine("ReservationDetailId: " + ReservationDetailId);
            var approverEmails = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Approver");
            var adminManagerEmail = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Manager");
            foreach(var email in approverEmails)
            {
                Console.WriteLine("Admin Approver:" + email);
            }

            if (ReservationDetailId <= 0 || string.IsNullOrEmpty(action))
            {
                return BadRequest("Invalid parameters.");
            }

            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            var guid = reservation.GUID;
            var reservationDetails = _context.ReservationDetails.FirstOrDefault(r => r.ReservationId == reservation.ReservationId);
            var roomName = _context.Rooms.Where(r => r.RoomId == _roomId).Select(r => r.RoomName).FirstOrDefault();
            var facilityName = _context.Facilities.Where(f => f.FacilityId == _facilityId).Select(f => f.FacilityName).FirstOrDefault();
            var empName = _context.Employees.FirstOrDefault(e => e.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));
            var employee = _context.Employees.FirstOrDefault(e => e.EmployeeID == reservation.RequestorID);
            var department = _context.Departments.FirstOrDefault(e => e.Id == employee.DepartmentId);
            var company = _context.Companies.FirstOrDefault(e => e.Id == employee.CompanyId);

            var role = _roleManager.FindByNameAsync("GFMS.Admin Approver");
            var controllerName = "FunctionRoom";

            if (reservation == null)
            {
                return NotFound();
            }

            if (action == "Approve" && reservation.Status == "FOR DEPARTMENT HEAD'S APPROVAL")
            {
                Console.WriteLine("ReservationDetailId: " + ReservationDetailId);
                reservation.Status = "FOR ADMIN APPROVER'S APPROVAL";
                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                await _emailService.SendPendingRoomAssignmentEmail(
                    reservation,
                    approverEmails,
                    controllerName,
                    department.DepartmentCode
                    );
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN APPROVER'S APPROVAL")
            {
                if ((_facilityId == 0 || _roomId == 0) || (_facilityId == null || _roomId == null))
                {
                    TempData["AlertMessage"] = "No Rooms and Facility Assigned";
                    TempData["AlertSubMessage"] = "Select a room and facility for this reservation.";
                    TempData["AlertType"] = "Warning";

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
                reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";
                reservationDetails.FacilityId = _facilityId;
                reservationDetails.RoomId = _roomId;
                reservationDetails.EquipmentNeeded = EquipmentNeeded;
                reservation.ApprovedBy = empName.Fullname;

                TempData["AlertMessage"] = "Room details assigned successfully!";
                TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                TempData["AlertType"] = "success";

                await _emailService.RoomAssignmentApprovalEmail(
                       reservation,
                       reservation.RequestorEmail,
                       reservationDetails.StartDate.ToString(),
                       reservationDetails.EndDate.ToString(),
                       reservationDetails.NumberOfGuest,
                       facilityName,
                       roomName,
                       reservationDetails.Purpose,
                       reservation.GUID,
                       controllerName,
                       adminManagerEmail

                   );
            }
            else if (action == "Reassign")
            {
                int oldRoomId = reservationDetails.RoomId;

                var oldRoomEquipmentIds = _context.RoomEquipments
                                                  .Where(re => re.RoomId == oldRoomId)
                                                  .Select(re => re.EquipmentId.ToString())
                                                  .ToList();

                List<string> currentEquipmentList = string.IsNullOrEmpty(reservationDetails.EquipmentNeeded)
                    ? new List<string>()
                    : reservationDetails.EquipmentNeeded.Split(',').ToList();

                currentEquipmentList.RemoveAll(e => oldRoomEquipmentIds.Contains(e));

                var newRoomEquipmentIds = _context.RoomEquipments
                                                  .Where(re => re.RoomId == _roomId)
                                                  .Select(re => re.EquipmentId.ToString())
                                                  .ToList();

                currentEquipmentList.AddRange(newRoomEquipmentIds.Where(e => !currentEquipmentList.Contains(e)));

                reservationDetails.EquipmentNeeded = currentEquipmentList.Any() ? string.Join(",", currentEquipmentList) : null;
                reservationDetails.FacilityId = _facilityId;
                reservationDetails.RoomId = _roomId;


                _context.SaveChanges();

                return RedirectToAction("ViewReservationDetails", new { id = guid });
            }

            else if (action == "Approve" && reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
            {
                if ((reservationDetails.FacilityId == 0 || reservationDetails.RoomId == 0) || (reservationDetails.FacilityId == null || reservationDetails.RoomId == null))
                {
                    TempData["AlertMessage"] = "No Rooms and Facility Assigned";
                    TempData["AlertSubMessage"] = "Select a room and facility for this reservation.";
                    TempData["AlertType"] = "Warning";

                    return RedirectToAction("ViewReservationDetails", new { id = guid });
                }
                reservation.Status = "APPROVED";
                reservationDetails.FacilityId = _facilityId;
                reservationDetails.RoomId = _roomId;
                reservation.ApprovedBy = empName.Fullname;


                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                await _emailService.SendApprovalNotification(
                     reservation,
                     reservation.RequestorEmail,
                     reservation.GUID,
                     controllerName
                 );
            }
            else if (action == "Reject")
            {
                reservation.Status = "DISAPPROVED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName.Fullname;
                await _emailService.SendDisapprovalNotification(
                      reservation,
                      reservation.RequestorEmail,
                      reservation.GUID,
                      controllerName

                  );
            }
            else if (action == "Cancel")
            {
                reservation.Status = "CANCELLED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName.Fullname;

            }
            else
            {
                return BadRequest("Invalid action.");
            }
            _context.SaveChanges();

            return RedirectToAction("ReservationList", "FunctionRoom");
        }
    }
}
