using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using PropertyManagementSystem.Services;
using System.Security.Claims;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class DormController : Controller
    {
        private readonly DAL _context;
        private const string UploadPath = "wwwroot/Uploads";
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;

        public DormController(DAL context, IRoleService roleService, EmailService emailService, EmployeeService employeeService)
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
           .Where(x => x.RoomType == 2)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 2))
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
                Requestor = _context.ReservationHeaders.FirstOrDefault()?.Requestor,
                //NewReservationsCount = newReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }

        public IActionResult ReservationList()
        {

            return View();

        }
        public IActionResult GetDorm(int ReservationId)
        {
            var resDetails = _context.ReservationDetails.Where(r => r.ReservationId == ReservationId);
            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClass = c.FacilityClassification })
                .Where(p => p.FacilityClass == "DORM")
                .OrderBy(o => o.FacilityName).ToList());
        }
        [HttpPost]
        public IActionResult Upload(List<IFormFile> files, string fileUid, int chunkIndex)
        {
            try
            {
                List<string> uploadedFilePaths = new List<string>();

                foreach (var file in files)
                {
                    if (file != null && file.Length > 0)
                    {
                        string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

                        var filepath = Path.Combine(directoryPath, file.FileName);
                        using (var fs = new FileStream(filepath, FileMode.Append))
                        {
                            file.CopyTo(fs);
                        }
                        string relativePath = Path.Combine("/Uploads", file.FileName);
                        uploadedFilePaths.Add(relativePath);

                        Console.WriteLine($"Uploaded file: {relativePath}");
                    }
                }

                return Json(new { success = true, fileUid = fileUid, chunkIndex = chunkIndex, filepaths = uploadedFilePaths });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading chunk: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost]
        public IActionResult Remove([FromBody] string[] files)
        {
            foreach (var fullName in files)
            {
                var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fullName);

                if (System.IO.File.Exists(physicalPath))
                {
                    System.IO.File.Delete(physicalPath);
                }
            }

            return Content("");
        }

        public ActionResult getDormReservation([DataSourceRequest] DataSourceRequest request, string gridType, int FacilityId, int RoomId)
        {
            Console.WriteLine("FacilityId: " + FacilityId);
            Console.WriteLine("RoomId: " + RoomId);
            var employee = _roleService.GetUserName(User).Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "DRM");

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
            else
            {
                query = query.Where(x => x.Status == "APPROVED");
            }

            var reservationHeaders = query
                .Select(header => new
                {
                    header,
                    Department = _context.Departments.FirstOrDefault(d => d.Id == header.Department),
                    Details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList(),
                    Facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).FirstOrDefault().FacilityId),
                    Room = _context.Rooms.FirstOrDefault(r => r.RoomId == _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).FirstOrDefault().RoomId)
                })
                .ToList();

            var reservationViewModels = reservationHeaders.Select(reservation => new ReservationViewModel
            {
                ReservationId = reservation.header.ReservationId,
                ReservationCode = reservation.header.ReservationCode,
                Requestor = reservation.header.Requestor,
                RequestorEmail = reservation.header.RequestorEmail,
                Status = reservation.header.Status,
                GUID = reservation.header.GUID,
                FacilityName = reservation.Facility?.FacilityName,
                RoomName = reservation.Room?.RoomName,
                FacilityLocation = reservation.Facility?.FacilityLocation,
                FacilityId = reservation.Facility?.FacilityId ?? 0,
                ReservationType = reservation.header.ReservationType,
                DepartmentName = reservation.Department?.DepartmentCode,
                ReservationDetailId = reservation.Details.FirstOrDefault()?.ReservationDetailId ?? 0
            }).ToList();

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.FacilityName,
                model.RoomName,
                model.FacilityLocation,
                model.Status,
                model.GUID,
                model.ReservationType,
                model.ReservationDetailId
            });

            return Json(result);
        }


        public IActionResult getRoomDetails([DataSourceRequest] DataSourceRequest request, int facilityId, int RoomId)
        {
            var reservationDetails = _context.ReservationDetails
                .Where(rd => rd.FacilityId == facilityId && rd.RoomId == RoomId)
                .ToList();

            var room = _context.Rooms.FirstOrDefault(r => r.RoomId == RoomId);

            var viewModels = reservationDetails.Select(rd =>
            {
                var header = _context.ReservationHeaders
                    .FirstOrDefault(rh => rh.ReservationId == rd.ReservationId);

                return new ReservationViewModel
                {
                    ReservationDetailId = rd?.ReservationDetailId,
                    RoomName = room?.RoomName,
                    Requestor = header?.Requestor,
                };
            }).ToList();

            return Json(viewModels.ToDataSourceResult(request));
        }


        private string GenerateReservationCode()
        {
            int currentYear = DateTime.Now.Year;
            int sequenceNumber = 1;

            var lastReservation = _context.ReservationHeaders
                .Where(r => r.ReservationType == "DRM")
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

            return $"RSQ-DRM-{currentYear}-{sequenceNumber:D4}";
        }

        /*Create Reservation*/
        [HttpPost]
        public async Task<IActionResult> CreateReservationAsync(int _department, string _requestor, string _requestorEmail)
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
                Requestor = _requestor,
                RequestorEmail = _requestorEmail,
                Approver = managerFullName,
                ApproverEmail = managerEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = "DRAFT",
                Department = _department,
                ReservationCode = reservationCode,
                ReservationType = "DRM"

            };

            _context.ReservationHeaders.Add(_reservation);
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewReservationDetails", new { Id = unique});

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
                var requestorEmail = reservationHeader.RequestorEmail;

                Console.WriteLine("Requestor Email: " + requestorEmail);

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();


                Gen_Department department = null;
                GFMS_Facility facility = null;
                GFMS_Rooms rooms = null;

                var employee = _context.Employees.FirstOrDefault(e => e.Email == requestorEmail);
                Console.WriteLine("Employee:" + employee.Email);

                if (reservationDetails.Any())
                {
                    var firstDetail = reservationDetails.FirstOrDefault();
                    facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == firstDetail.FacilityId);
                    rooms = _context.Rooms.FirstOrDefault(r => r.RoomId == firstDetail.RoomId);
                }

                department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);
                var position = _context.Positions.FirstOrDefault(p => p.Id == employee.PositionId);
                var level = _context.EmploymentLevels.FirstOrDefault(l => l.Id == employee.EmploymentLevelId);


                var reservation = new ReservationViewModel
                {
                    Department = department,
                    Facility = facility,
                    Room = rooms,
                    EmploymentLevel = level,
                    Position = position,
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

        public IActionResult ReservationDetails(string Id)
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

                var department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);
                if (department == null)
                {
                    return NotFound("Department not found");
                }

                var reservation = new ReservationDetailViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Gen_Department = department
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
                return StatusCode(500, "Internal server error");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
        /*Create*/
        public IActionResult CreateReservationDetails(
            int _reservationId, int _unitApplied, int _schedType, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation,int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, string _civilStat,string[] _dependents, string[] _age, string[] _location, string[] _position, string filepath, string[] _dependent_status)
        {
            try
            {
                if (_dependents != null && _age != null && _position != null && _location != null && _dependent_status != null)
                {
                    int length = Math.Min(
                Math.Min(_dependents.Length, _age.Length),
                Math.Min(_location.Length, Math.Min(_position.Length, _dependent_status.Length)));
                    for (int i = 0; i < length; i++)
                    {
                        Console.WriteLine($"Dependent Name: {_dependents[i]}, Status: {_dependent_status[i]}");
                        // Create a new reservation detail object and populate it with form values
                        GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                        {
                            ReservationId = _reservationId,
                            AppliedUnit = _unitApplied,
                            ScheduleType = _schedType,
                            ContactNumber = _contactNumber ?? string.Empty,
                            PermanentAddress = _permanentAdd ?? string.Empty,
                            CurrentAddress = _currentAdd ?? string.Empty,
                            HomeOwnership = _homeOwnership,
                            OwnedHouseLocation = _ownedLocation,
                            SpecificLocation = _specificLocation ?? string.Empty,
                            ModeOfTransportation = _modeOfTransport,
                            VehicleType = _vehicleType ?? string.Empty,
                            TravelTime = _travelTime ?? string.Empty,
                            Fare = _fare ?? string.Empty,
                            CivilStatus = _civilStat ?? string.Empty,
                            DependentName = _dependents[i] ?? string.Empty,
                            DependentAge = _age[i] ?? string.Empty,
                            WorkPosition = _position[i] ?? string.Empty,
                            StudyLocation = _location[i] ?? string.Empty,
                            OtherInfo = _dependent_status[i] ?? string.Empty,
                            DateAdded = DateTime.UtcNow,
                            FileAttachment = filepath ?? string.Empty
                        };
                        Console.WriteLine($"Dependent Status for row {i}: {_dependent_status[i]}");
                        var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == _reservationId);
                        if (reservationHeader != null)
                        {
                            reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";
                        }

                        _context.ReservationDetails.Add(reservationDetails);
                    }

                    _context.SaveChanges();
                }
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while saving changes: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }

            return RedirectToAction("Index", "Dorm");
        }



        [HttpPost]
        public IActionResult UpdateReservationDetails(int reservationId, int _unitApplied, int _schedType, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation, int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, string _civilStat, List<string> _dependents, List<string> _age, List<string> _location, List<string> _position, List<string> _dependent_status, string filepath)
        {
            // Retrieve the existing reservation details
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == reservationId)
                .ToList();

            // Loop through the existing reservation details
            for (int i = 0; i < existingReservationDetails.Count; i++)
            {
                var existingDetail = existingReservationDetails[i];
                var index = _dependents.FindIndex(g => string.Equals(g, existingDetail.DependentName, StringComparison.OrdinalIgnoreCase));

                if (index != -1)
                {
                    // Update details
                    existingDetail.AppliedUnit = _unitApplied;
                    existingDetail.ScheduleType = _schedType;
                    existingDetail.ContactNumber = _contactNumber;
                    existingDetail.PermanentAddress = _permanentAdd;
                    existingDetail.CurrentAddress = _currentAdd;
                    existingDetail.HomeOwnership = _homeOwnership;
                    existingDetail.OwnedHouseLocation = _ownedLocation;
                    existingDetail.SpecificLocation = _specificLocation;
                    existingDetail.ModeOfTransportation = _modeOfTransport;
                    existingDetail.VehicleType = _vehicleType;
                    existingDetail.TravelTime = _travelTime;
                    existingDetail.Fare = _fare;
                    existingDetail.CivilStatus = _civilStat;
                    existingDetail.DependentName = _dependents[index];
                    existingDetail.DependentAge = _age[index];
                    existingDetail.StudyLocation = _location[index];
                    existingDetail.WorkPosition = _position[index];

                    // Update the dependent status (studying/working)
                    if (_dependent_status != null && _dependent_status.Count > index)
                    {
                        existingDetail.OtherInfo = _dependent_status[index];  // Assuming OtherInfo is where you store the dependent status
                    }

                    // Handle the file update - if a new file is uploaded, replace the old one
                    if (!string.IsNullOrEmpty(filepath))
                    {
                        existingDetail.FileAttachment = filepath;
                    }

                    _context.ReservationDetails.Update(existingDetail);
                }
                else
                {
                    // If the dependent is not in the new list, remove it
                    _context.ReservationDetails.Remove(existingDetail);
                }
            }

            // Save the changes to the database
            _context.SaveChanges();

            return RedirectToAction("Index", "Dorm");
        }


        /*Delete*/
        public IActionResult DeleteReservationDetail(int ReservationId)
        {
            try
            {
                Console.WriteLine(ReservationId);
                var reservationDetail = _context.ReservationDetails
                    .Include(rd => rd.ReservationHeader)
                    .Where(rd => rd.ReservationId == ReservationId).ToList();

                if (reservationDetail == null)
                {
                    return NotFound("Reservation detail not found.");
                }


                _context.ReservationDetails.RemoveRange(reservationDetail);


                var reservationHeader = reservationDetail.First().ReservationHeader;
                if (reservationHeader != null)
                {
                    _context.ReservationHeaders.Remove(reservationHeader);
                }

                _context.SaveChanges();

                return RedirectToAction("Index", "Dorm");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatusAsync(int ReservationId, string action, int _facilityId, int _roomId, string Remarks, bool _isCritical, bool _isOnCall, string _deptRemarks)
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
                reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";
                reservationDetails.DeptHeadRemarks = _deptRemarks;
                reservationDetails.isCritical = _isCritical;
                reservationDetails.isOnCall = _isOnCall;
                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
            {
                reservation.Status = "FOR ADMIN APPROVER'S APPROVAL";
                reservation.ApprovedBy = empName.Fullname;

                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                /*await _emailService.SendApprovalNotification(
                     reservation,
                     reservation.RequestorEmail
                 );*/
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN APPROVER'S APPROVAL")
            {
                reservation.Status = "APPROVED";
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

            return RedirectToAction("ReservationList", "Dorm");
        }

    }
}
