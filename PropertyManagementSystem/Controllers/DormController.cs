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

namespace PropertyManagementSystem.Controllers
{
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
                NewReservationsCount = newReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }
        public IActionResult ReservationList()
        {

            return View();

        }

        [HttpPost]
        public IActionResult Upload(List<IFormFile> files, string fileUid, int chunkIndex)
        {
            try
            {
                var file = files.FirstOrDefault();
                if (file != null)
                {
                    string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    var filepath = Path.Combine(directoryPath, file.FileName);
                    using (var fs = new FileStream(filepath, FileMode.Append))
                    {
                        file.CopyTo(fs);
                    }
                    string relativePath = filepath.Replace(Directory.GetCurrentDirectory(), string.Empty).TrimStart(Path.DirectorySeparatorChar);

                    return Json(new { success = true, fileUid = fileUid, chunkIndex = chunkIndex, filepath = relativePath});
                }

                return Json(new { success = false });
            }
            catch (Exception ex)
            {
                // Handle exceptions appropriately
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

        public IActionResult ViewReservationDetails(string id) 
        {

            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    return BadRequest("GUID cannot be null or empty");
                }

                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationHeader.ReservationId)
                    .ToList();

                var department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);

                var detailViewModel = new ReservationViewModel
                {
                    Department = department,
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails
                };

                return View(detailViewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        public ActionResult getDormReservation([DataSourceRequest] DataSourceRequest request)
        {
            var reservationHeaders = _context.ReservationHeaders
                .Where(x => x.ReservationType == "DRM")
                .ToList();

            List<ReservationViewModel> reservationViewModels = new List<ReservationViewModel>();

            foreach (var header in reservationHeaders)
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList();

                var viewModel = new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    ReservationCode = header.ReservationCode,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    Status = header.Status,
                    GUID = header.GUID,
                    ReservationType = header.ReservationType,
                    DepartmentName = department?.DepartmentName,
                    ReservationDetailId = details.FirstOrDefault()?.ReservationDetailId ?? 0
                };

                reservationViewModels.Add(viewModel);
            }

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.Status,
                model.GUID,
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

            return $"RSQ-DRM-{year}-{sequenceNumber:D4}";
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

            return RedirectToAction("ReservationDetails", new { Id = unique});

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
        public IActionResult CreateReservationDetails(int _reservationId, int _unitApplied, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation, int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, string _civilStat, string[] _dependents, string[] _age, string[] _location, string[] _position, string filepath)
        {
            try
            {
                if (_dependents != null && _age != null && (_position != null || _location != null))
                {
                    int length = Math.Min(Math.Min(_dependents.Length, _age.Length), _position.Length);

                    for (int i = 0; i < length; i++)
                    {
                        GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                        {
                            ReservationId = _reservationId,
                            AppliedUnit = _unitApplied,
                            ContactNumber = _contactNumber,
                            PermanentAddress = _permanentAdd,
                            CurrentAddress = _currentAdd,
                            HomeOwnership = _homeOwnership,
                            OwnedHouseLocation = _ownedLocation,
                            SpecificLocation = _specificLocation,
                            ModeOfTransportation = _modeOfTransport,
                            VehicleType = _vehicleType,
                            TravelTime = _travelTime,
                            Fare = _fare,
                            CivilStatus = _civilStat,
                            DependentName = _dependents[i],
                            DependentAge = _age[i],
                            WorkPosition = _position[i],
                            StudyLocation = _location[i],
                            DateAdded = DateTime.UtcNow,
                            FileAttachment = filepath
                        };

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
        /*Update*/
        [HttpPost]
        public IActionResult UpdateReservationDetails(int reservationId, int _unitApplied, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation, int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, string _civilStat, List<string> _dependents, List<string> _age, List<string> _location, List<string> _position, string filepath)
        {
            List<string> filePaths = new List<string>();
            Console.WriteLine("Reservation Id: " + reservationId);
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == reservationId)
                .ToList();

            

            for (int i = 0; i < existingReservationDetails.Count; i++)
            {
                var existingDetail = existingReservationDetails[i];


                var index = _dependents.FindIndex(g => string.Equals(g, existingDetail.DependentName, StringComparison.OrdinalIgnoreCase));


                if (index != -1)
                {
                    existingDetail.AppliedUnit = _unitApplied;
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
                    existingDetail.FileAttachment = filepath;
                    _context.ReservationDetails.Update(existingDetail);
                }
                else
                {
                    _context.ReservationDetails.Remove(existingDetail);
                }
            }


            for (int i = 0; i < _dependents.Count; i++)
            {
                var existingDetail = existingReservationDetails.FirstOrDefault(rd => string.Equals(rd.DependentName, _dependents[i], StringComparison.OrdinalIgnoreCase));
                if (existingDetail == null)
                {
                    GFMS_ReservationDetails newDetail = new GFMS_ReservationDetails
                    {
                        ReservationId = reservationId,
                        AppliedUnit = _unitApplied,
                        ContactNumber = _contactNumber,
                        PermanentAddress = _permanentAdd,
                        CurrentAddress = _currentAdd,
                        HomeOwnership = _homeOwnership,
                        OwnedHouseLocation = _ownedLocation,
                        SpecificLocation = _specificLocation,
                        ModeOfTransportation = _modeOfTransport,
                        VehicleType = _vehicleType,
                        TravelTime = _travelTime,
                        Fare = _fare,
                        CivilStatus = _civilStat,
                        DependentName = _dependents[i],
                        DependentAge = _age[i],
                        StudyLocation = _location[i],
                        WorkPosition = _position[i],
                        FileAttachment = filepath
                    };

                    _context.ReservationDetails.Add(newDetail);
                }
            }
            _context.SaveChanges();
            return RedirectToAction("ReservationList", "Dorm");
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

    }
}
