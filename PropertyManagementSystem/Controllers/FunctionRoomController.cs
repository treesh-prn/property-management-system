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

namespace PropertyManagementSystem.Controllers
{

    public class FunctionRoomController : Controller
    {
        private readonly DAL _context;
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;

        public FunctionRoomController(DAL context, IRoleService roleService, EmailService emailService, EmployeeService employeeService)
        {
            _context = context;
            _roleService = roleService;
            _emailService = emailService;
            _employeeService = employeeService;
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
                                        StartDate = resDetails.StartDate,
                                        EndDate = resDetails.EndDate,
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
            reservationsQuery = reservationsQuery.Where(r => r.ReservationType == "FN");
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
                                                    StartDate = r.StartDate.HasValue ? r.StartDate.Value.ToShortTimeString() : "",
                                                    RoomId = r.RoomId,
                                                    FacilityId = r.FacilityId,
                                                    RoomName = r.RoomName,
                                                    FacilityName = r.FacilityName,
                                                }).ToList();

            return Json(reservations.ToDataSourceResult(request));
        }

        public IActionResult GetFacility(int ReservationId)
        {
            var resDetails = _context.ReservationDetails.Where(r => r.ReservationId == ReservationId);
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

        public ActionResult GetFunctionRoomReservation([DataSourceRequest] DataSourceRequest request, string gridType)
        {
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var today = DateTime.Today;
            var targetDateTime = new DateTime(today.Year, today.Month, today.Day, 16, 0, 0);

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "FN");

            if (gridType == "myReservation")
            {
                query = query.Where(x => x.Requestor == employee.Fullname);
            }
            else if (gridType == "reservationApproval")
            {
                if (User.IsInRole("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
                else if (User.IsInRole("GFMS.Admin Approver"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
                else if (User.IsInRole("GFMS.Admin Manager"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL" && x.DateSubmitted <= targetDateTime);
                }
            }

            var reservationHeaders = query.ToList();
            var reservationViewModels = reservationHeaders.Select(header =>
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList();

                return new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    ReservationCode = header.ReservationCode,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    Status = header.Status,
                    GUID = header.GUID,
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
                model.GUID,
                model.ChargeToCompany,
                model.ReservationType,
                model.ReservationDetailId
            });

            return Json(result);
        }


        private string GenerateReservationCode()
        {
            int year = DateTime.Now.Year;
            int sequenceNumber = 1;

            var lastReservation = _context.ReservationHeaders.Where(r => r.ReservationType == "FN").Count();
            if (lastReservation != null)
            {
                sequenceNumber = lastReservation + 1;
            }

            return $"RSQ-FN-{year}-{sequenceNumber:D4}";
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
                ReservationType = "FN"
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

                var reservation = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    NumberOfGuest = reservationDetails.Sum(rd => rd.NumberOfGuest)
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

        public async Task<IActionResult> CreateReservationDetailsAsync(int _reservationId, int _numOfGuests, int _facilityId, int _roomId, DateTime _startDate, DateTime _endDate, string _purpose, List<string> _equipment, string _contactNumber, string _requestorEmail)
        {
            Console.WriteLine($"Requestor Email: {_requestorEmail}");
            Console.WriteLine($"Start Date: {_startDate}");
            Console.WriteLine($"End Date: {_endDate}");

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
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
                    EndDate = _endDate,
                    Purpose = _purpose,
                    DateAdded = DateTime.UtcNow,
                    EquipmentNeeded = string.Join(",", equipmentNeeded.Select(e => e.Trim())),
                    ContactNumber = _contactNumber
                };

                _context.ReservationDetails.Add(reservationDetails);

                var roomName = _context.Rooms.Where(r => r.RoomId == _roomId).Select(r => r.RoomName).FirstOrDefault();
                var facilityName = _context.Facilities.Where(f => f.FacilityId == _facilityId).Select(f => f.FacilityName).FirstOrDefault();
                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == _reservationId);
                if (reservationHeader != null)
                {
                    if (User.IsInRole("GFMS.Admin Approver"))
                    {
                        reservationHeader.Status = "FOR ADMIN MANAGER'S APPROVAL";
                        /*if (reservationHeader != null)
                        {
                            await _emailService.SendReservationApprovalEmail(
                                reservationHeader,
                                reservationHeader.RequestorEmail,
                                _startDate.ToString(),
                                _endDate.ToString(),
                                _numOfGuests,
                                facilityName,
                                roomName,
                                _purpose
                            );
                        }*/
                    }
                    else if (User.IsInRole("GFMS.Department Head"))
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                        /*
                         if (reservationHeader != null)
                        {
                            await _emailService.SendReservationApprovalEmail(
                               reservationHeader,
                               reservationHeader.RequestorEmail,
                               _startDate.ToString(),
                               _endDate.ToString(),
                               _numOfGuests,
                               facilityName,
                               roomName,
                               _purpose
                           );
                        }
                         */

                    }
                    else
                    {
                        reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";
                        /*
                         if (reservationHeader != null)
                        {
                            await _emailService.SendReservationApprovalEmail(
                                reservationHeader,
                                reservationHeader.RequestorEmail,
                                _startDate.ToString(),
                                _endDate.ToString(),
                                _numOfGuests,
                                facilityName,
                                roomName,
                                _purpose
                            );
                        }
                         */
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
        public IActionResult EditReservationDetails(int ReservationDetailId, int ReservationId, int NumberOfGuest, int _facilityId, int _roomId, DateTime StartDate, DateTime EndDate, string Purpose, string[] EquipmentNeeded, string _contactNumber)
        {
            if (StartDate < new DateTime(1753, 1, 1) || StartDate > new DateTime(9999, 12, 31) ||
                EndDate < new DateTime(1753, 1, 1) || EndDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            var reservationDetail = _context.ReservationDetails.FirstOrDefault(rd => rd.ReservationDetailId == ReservationDetailId);
            if (reservationDetail == null)
            {
                return NotFound("Reservation detail not found.");
            }

            reservationDetail.NumberOfGuest = NumberOfGuest;
            reservationDetail.FacilityId = _facilityId;
            reservationDetail.RoomId = _roomId;
            reservationDetail.StartDate = StartDate;
            reservationDetail.EndDate = EndDate;
            reservationDetail.ContactNumber = _contactNumber;
            reservationDetail.Purpose = Purpose;

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
                TempData["AlertMessage"] = "Reservation details updated successfully!";
                TempData["AlertSubMessage"] = "Your reservation has been updated";
                TempData["AlertType"] = "success";
                _context.ReservationDetails.Update(reservationDetail);
                _context.SaveChanges();
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

            return RedirectToAction("ReservationList", "FunctionRoom");
        }

        /*Delete*/
        [HttpPost]
        public IActionResult DeleteReservationDetail(int ReservationId)
        {
            try
            {
                var reservationDetail = _context.ReservationDetails
                    .Include(rd => rd.ReservationHeader)
                    .FirstOrDefault(rd => rd.ReservationId == ReservationId);

                if (reservationDetail == null)
                {
                    return NotFound("Reservation detail not found.");
                }


                _context.ReservationDetails.Remove(reservationDetail);


                var reservationHeader = reservationDetail.ReservationHeader;
                if (reservationHeader != null)
                {
                    _context.ReservationHeaders.Remove(reservationHeader);
                }

                _context.SaveChanges();

                return RedirectToAction("Index", "FunctionRoom");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatusAsync(int ReservationId, string action, int _facilityId, int _roomId, string Remarks)
        {
            Console.WriteLine("ReservationId: " + ReservationId);

            if (ReservationId <= 0 || string.IsNullOrEmpty(action))
            {
                return BadRequest("Invalid parameters.");
            }

            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            var reservationDetails = _context.ReservationDetails.FirstOrDefault(r => r.ReservationId == reservation.ReservationId);
            var roomName = _context.Rooms.Where(r => r.RoomId == _roomId).Select(r => r.RoomName).FirstOrDefault();
            var facilityName = _context.Facilities.Where(f => f.FacilityId == _facilityId).Select(f => f.FacilityName).FirstOrDefault();
            var empName = _context.Employees.FirstOrDefault(e => e.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (reservation == null)
            {
                return NotFound();
            }

            if (action == "Approve" && reservation.Status == "FOR DEPARTMENT HEAD'S APPROVAL")
            {
                reservation.Status = "FOR ADMIN APPROVER'S APPROVAL";
                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN APPROVER'S APPROVAL")
            {
                reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";
                reservationDetails.FacilityId = _facilityId;
                reservationDetails.RoomId = _roomId;
                reservation.ApprovedBy = empName.Fullname;

                TempData["AlertMessage"] = "Room details assigned successfully!";
                TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                TempData["AlertType"] = "success";

                /*await _emailService.RoomAssignmentApprovalEmail(
                       reservation,
                       reservation.RequestorEmail,
                       reservationDetails.StartDate.ToString(),
                       reservationDetails.EndDate.ToString(),
                       reservationDetails.NumberOfGuest,
                       facilityName,
                       roomName,
                       reservationDetails.Purpose
                   );

                await _emailService.SendReservationApprovalEmail(
                      reservation,
                      reservation.RequestorEmail,
                      reservationDetails.StartDate.ToString(),
                      reservationDetails.EndDate.ToString(),
                      reservationDetails.NumberOfGuest,
                      facilityName,
                      roomName,
                      reservationDetails.Purpose
                  );*/
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
            {
                reservation.Status = "APPROVED";
                reservationDetails.FacilityId = _facilityId;
                reservationDetails.RoomId = _roomId;
                reservation.ApprovedBy = empName.Fullname;


                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                /*await _emailService.SendApprovalNotification(
                     reservation,
                     reservation.RequestorEmail
                 );*/
            }
            else if (action == "Reject")
            {
                reservation.Status = "DISAPPROVED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName.Fullname;
                await _emailService.SendDisapprovalNotification(
                      reservation,
                      reservation.RequestorEmail
                  );
            }
            else
            {
                return BadRequest("Invalid action.");
            }
            _context.SaveChanges();

            return RedirectToAction("Index", "FunctionRoom");
        }
    }
}
