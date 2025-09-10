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
using MimeKit;

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

        public IActionResult Housing()
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
        public IActionResult Remove()
        {
            var fileNames = Request.Form["fileNames[0]"];
            foreach (var fileName in fileNames)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }



            return Json(new { status = "success" });
        }



        public async Task<ActionResult> getDormReservationAsync([DataSourceRequest] DataSourceRequest request, string gridType, int FacilityId, int RoomId)
        {
            Console.WriteLine("FacilityId: " + FacilityId);
            Console.WriteLine("RoomId: " + RoomId);
            var employee = _roleService.GetUserName(User).Result;
            string currentRole = await _roleService.GetActiveRoleAsync(User);

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "DRM")
                .Where(x => x.Status != "DELETED");



            if (gridType == "myReservation")
            {
                query = query.Where(x => x.Requestor == employee.Fullname);
            }
            else if (gridType == "approve")
            {
                query = query.Where(x => x.Status == "APPROVED" || x.Status == "FOR RENEWAL");
            }
            else if (gridType == "reservationApproval")
            {
                if (currentRole == ("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname && x.Status == "FOR DEPARTMENT HEAD'S APPROVAL");
                }
                else if (currentRole == ("GFMS.Admin Approver"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN APPROVER'S APPROVAL");
                }
                else if (currentRole == ("GFMS.Admin Manager"))
                {
                    query = query.Where(x => x.Status == "FOR ADMIN MANAGER'S APPROVAL");
                }
            }

            var reservationHeaders = query
                .OrderByDescending(x => x.ApprovedDateTime)
                .Select(header => new
                {
                    header,
                    Department = _context.Departments.FirstOrDefault(d => d.Id == header.Department),
                    Company = _context.Companies.FirstOrDefault(c => c.Id == header.ChargeToCompany),
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
                ApprovedDateTime = reservation.header.ApprovedDateTime,
                CanDelete = reservation.header.Status == "DRAFT",
                FacilityName = reservation.Facility?.FacilityName,
                RoomName = reservation.Room?.RoomName,
                FacilityLocation = reservation.Facility?.FacilityLocation,
                FacilityId = reservation.Facility?.FacilityId ?? 0,
                ReservationType = reservation.header.ReservationType ?? "N/A",
                AppliedUnit = reservation.Details.FirstOrDefault()?.AppliedUnit == 0 ? "DORM" :
                reservation.Details.FirstOrDefault()?.AppliedUnit == 1 ? "HOUSING" : "N/A",
                Company = reservation.Company?.CompanyCode ?? "N/A",
                DepartmentName = reservation.Department?.DepartmentCode ?? "N/A",
                ReservationDetailId = reservation.Details.FirstOrDefault()?.ReservationDetailId ?? 0
            }).ToList();

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.Company,
                model.DepartmentName,
                model.RequestorEmail,
                model.FacilityName,
                model.RoomName,
                model.FacilityLocation,
                model.Status,
                model.GUID,
                model.ApprovedDateTime,
                model.CanDelete,
                model.ReservationType,
                model.AppliedUnit,
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

            var maxSequence = _context.ReservationHeaders
                .Where(r => r.ReservationType == "DRM"
                            && r.ReservationCode.StartsWith($"RSQ-DRM-{currentYear}"))
                .AsEnumerable()
                .Select(r =>
                {
                    var parts = r.ReservationCode.Split('-');
                    return parts.Length == 4 && int.TryParse(parts[3], out int seq) ? seq : 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            sequenceNumber = maxSequence + 1;

            return $"RSQ-DRM-{currentYear}-{sequenceNumber:D4}";
        }


        /*Create Reservation*/
        [HttpPost]
        public async Task<IActionResult> CreateReservationAsync(int _company, int _department, string _requestor, string _requestorEmail, string _userId, bool _forRenewal, int _renewalReferenceId)
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
                RequestorID = _userId,
                Approver = managerFullName,
                ApproverEmail = managerEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = "DRAFT",
                ChargeToCompany = _company,
                Department = _department,
                ReservationCode = reservationCode,
                ReservationType = "DRM",
                isForRenewal = _forRenewal,
                PreviousReservationId = _renewalReferenceId


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
                var reservationHeader = _context.ReservationHeaders
                    .FirstOrDefault(x => x.GUID == Id);

                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                int reservationId = reservationHeader.ReservationId;

                // Get requestor (DormApplicant) for schedtype
                var dormApplicant = _context.DormApplicants
                    .FirstOrDefault(a => a.UserId == reservationHeader.RequestorID);

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();

                Gen_Department department = _context.Departments
                    .FirstOrDefault(d => d.Id == reservationHeader.Department);

                GFMS_Facility facility = null;
                GFMS_Rooms rooms = null;

                if (reservationDetails.Any())
                {
                    var firstDetail = reservationDetails.First();
                    facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == firstDetail.FacilityId);
                    rooms = _context.Rooms.FirstOrDefault(r => r.RoomId == firstDetail.RoomId);
                }

                // Employee by UserId instead of email (safer with your schema)
                var employee = _context.Employees
                    .FirstOrDefault(e => e.UserId == reservationHeader.RequestorID);

                var position = employee != null ? _context.Positions.FirstOrDefault(p => p.Id == employee.PositionId) : null;
                var level = employee != null ? _context.EmploymentLevels.FirstOrDefault(l => l.Id == employee.EmploymentLevelId) : null;


                // Room history
                string dormerId = reservationHeader.RequestorID;
                var roomHistory = _context.DormerRoomHistories
                    .Where(h => h.DormerId == dormerId)
                    .OrderByDescending(h => h.TransferDate)
                    .Select(h => new DormerHistoryViewModel
                    {
                        DormerId = h.DormerId,
                        PreviousRoom = _context.Rooms.Where(r => r.RoomId == h.PreviousRoomId).Select(r => r.RoomName).FirstOrDefault(),
                        NewRoom = _context.Rooms.Where(r => r.RoomId == h.NewRoomId).Select(r => r.RoomName).FirstOrDefault(),
                        PreviousFacility = _context.Facilities
                            .Where(f => f.FacilityId == _context.Rooms
                                .Where(r => r.RoomId == h.PreviousRoomId)
                                .Select(r => r.FacilityId)
                                .FirstOrDefault())
                            .Select(f => f.FacilityName)
                            .FirstOrDefault(),
                        NewFacility = _context.Facilities
                            .Where(f => f.FacilityId == _context.Rooms
                                .Where(r => r.RoomId == h.NewRoomId)
                                .Select(r => r.FacilityId)
                                .FirstOrDefault())
                            .Select(f => f.FacilityName)
                            .FirstOrDefault(),
                        TransferDate = h.TransferDate,
                        TransferredBy = h.TransferredBy,
                        Status = h.Status,
                    }).ToList();

                var fileAttachments = _context.FileAttachments
                    .Where(f => f.ReservationId == reservationId)
                    .ToList();

                var reservation = new ReservationViewModel
                {
                    Department = department,
                    Facility = facility,
                    Room = rooms,
                    Employees = employee,
                    EmploymentLevel = level,
                    Position = position,
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    NumberOfGuest = reservationDetails.Sum(rd => rd.NumberOfGuest),
                    DormerHistoryDetails = roomHistory,
                    FileAttachments = fileAttachments,
                    ScheduleType = dormApplicant?.SchedType // new
                };

                return View(reservation);
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
        public async Task<IActionResult> CreateReservationDetailsAsync(
        int _reservationId,
        int _unitApplied,
        int _schedType,
        string _contactNumber,
        string _permanentAdd,
        string _currentAdd,
        int _homeOwnership,
        int _ownedLocation,
        string _specificLocation,
        int _modeOfTransport,
        string _plateNumber,
        string _travelTime,
        string _fare,
        string _civilStat,
        string[] _dependents,
        string[] _age,
        string[] _location,
        string[] _name,
        string filepath,
        string[] _dependent_status,
        List<string> filesToRemove,
        bool _isForRenewal,
        int? _previousReservationId)
        {
            string currentRole = await _roleService.GetActiveRoleAsync(User);
            var approverEmails = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Approver");
            var controllerName = "Dorm";

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var applicant = await _context.DormApplicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    applicant = new GFMS_DormApplicants
                    {
                        UserId = userId,
                        PermanentAddress = _permanentAdd ?? string.Empty,
                        CurrentAddress = _currentAdd ?? string.Empty,
                        ContactNumber = _contactNumber ?? string.Empty,
                        SchedType = _schedType,
                        HomeOwnership = _homeOwnership,
                        SpecificLocation = _specificLocation ?? string.Empty,
                        OwnedHouseLocation = _ownedLocation,
                        ModeOfTransportation = _modeOfTransport,
                        PlateNumber = _plateNumber,
                        TravelTime = _travelTime,
                        Fare = _fare,
                        DateAdded = DateTime.UtcNow

                    };
                    _context.DormApplicants.Add(applicant);

                    await _context.SaveChangesAsync();
                }
                else
                {
                    applicant.PermanentAddress = _permanentAdd ?? applicant.PermanentAddress;
                    applicant.CurrentAddress = _currentAdd ?? applicant.CurrentAddress;
                    applicant.ContactNumber = _contactNumber ?? applicant.ContactNumber;
                    applicant.SchedType = _schedType;
                    applicant.HomeOwnership = _homeOwnership;
                    applicant.SpecificLocation = _specificLocation ?? applicant.SpecificLocation;
                    applicant.OwnedHouseLocation = _ownedLocation;
                    applicant.ModeOfTransportation = _modeOfTransport;
                    applicant.PlateNumber = _plateNumber ?? applicant.PlateNumber;
                    applicant.TravelTime = _travelTime ?? applicant.TravelTime;
                    applicant.Fare = _fare ?? applicant.Fare;
                    _context.DormApplicants.Update(applicant);
                }

                await _context.SaveChangesAsync();

                if (_civilStat == "MARRIED" && _dependents != null && _dependents.Length > 0)
                {
                    int length = Math.Min(Math.Min(_dependents.Length, _age.Length),
                        Math.Min(_location.Length, Math.Min(_name.Length, _dependent_status.Length)));

                    for (int i = 0; i < length; i++)
                    {
                        var dep = new GFMS_HousingDependents
                        {
                            ApplicantId = applicant.ApplicantId,
                            DependentName = _dependents[i],
                            DependentAge = _age[i],
                        };
                        if (_dependent_status[i] == "studying")
                        {
                            dep.SchoolName = _name[i];
                            dep.SchoolLocation = _location[i];
                        }
                        else
                        {
                            dep.CompanyName = _name[i];
                            dep.CompanyLocation = _location[i];
                        }

                        _context.HousingDependents.Add(dep);
                    }
                    await _context.SaveChangesAsync();
                }
                var reservationDetails = new GFMS_ReservationDetails
                {
                    ReservationId = _reservationId,
                    AppliedUnit = _unitApplied,
                };
                _context.ReservationDetails.Add(reservationDetails);
                await _context.SaveChangesAsync();

                if (filesToRemove != null && filesToRemove.Any())
                {
                    foreach (var fileName in filesToRemove)
                    {
                        if (string.IsNullOrWhiteSpace(fileName))
                            continue;

                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }

                        var fileRecord = _context.FileAttachments
                            .FirstOrDefault(f => f.FileName == fileName && f.ReservationId == _reservationId);

                        if (fileRecord != null)
                        {
                            _context.FileAttachments.Remove(fileRecord);
                        }
                    }
                }
                var reservationHeader = await _context.ReservationHeaders.FirstOrDefaultAsync(rh => rh.ReservationId == _reservationId);
                if (reservationHeader != null)
                {
                    var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeID == reservationHeader.RequestorID);
                    var department = await _context.Departments.FirstOrDefaultAsync(d => d.Id == employee.DepartmentId);
                    var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == employee.CompanyId);

                    if (User.IsInRole("GFMS.Admin Approver") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                        await _emailService.SendPendingRoomAssignmentEmail(reservationHeader, approverEmails, controllerName, department.DepartmentCode);
                    }
                    else if (currentRole == "GFMS.Requestor" && department.DepartmentCode == "ADMIN" && company.CompanyCode == "FDPPC")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                        await _emailService.SendPendingRoomAssignmentEmail(reservationHeader, approverEmails, controllerName, department.DepartmentCode);
                    }
                    else if (User.IsInRole("GFMS.Admin Manager") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "APPROVED";
                    }
                    else if (User.IsInRole("GFMS.Department Head") && currentRole == "GFMS.Requestor")
                    {
                        reservationHeader.Status = "FOR ADMIN APPROVER'S APPROVAL";
                        await _emailService.SendPendingRoomAssignmentEmail(reservationHeader, approverEmails, controllerName, department.DepartmentCode);
                    }
                    else
                    {
                        reservationHeader.Status = "FOR DEPARTMENT HEAD'S APPROVAL";
                        await _emailService.SendDRMReservationApprovalEmail(
                            reservationHeader,
                            reservationHeader.RequestorEmail,
                            reservationHeader.ApproverEmail,
                            reservationHeader.Approver,
                            reservationHeader.GUID,
                            controllerName
                        );
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["AlertMessage"] = "Application details created successfully!";
                TempData["AlertSubMessage"] = "Your application has been saved";
                TempData["AlertType"] = "success";

            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
            return RedirectToAction("ReservationList", "Dorm");
        }

        [HttpPost]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files, int reservationId)
        {
            Console.WriteLine("ReservationId:" + reservationId);
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    var fileName = Path.GetFileName(file.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var attachment = new GFMS_FileAttachment
                    {
                        ReservationId = reservationId,
                        FileName = fileName,
                        FilePath = "/uploads/" + fileName,
                        DateUploaded = DateTime.UtcNow
                    };

                    _context.FileAttachments.Add(attachment);
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public IActionResult UpdateReservationDetails(
        int reservationId,
        int _unitApplied,
        int _schedType,
        string _contactNumber,
        string _permanentAdd,
        string _currentAdd,
        int _homeOwnership,
        int _ownedLocation,
        int _specificLocation,
        int _modeOfTransport,
        string _plateNumber,
        string _travelTime,
        string _fare,
        string _civilStat,
        List<string> _dependents,
        List<string> _age,
        List<string> _location,
        List<string> _position,
        List<string> _dependent_status,
        string filepath,
        List<string> filesToRemove)
        {
            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == reservationId);
            var guid = reservationHeader.GUID;

            if (reservationHeader == null)
            {
                return NotFound();
            }

            var applicant = _context.DormApplicants.FirstOrDefault(a => a.UserId == reservationHeader.RequestorID);

            if (applicant != null)
            {
                applicant.ContactNumber = _contactNumber;
                applicant.PermanentAddress = _permanentAdd;
                applicant.CurrentAddress = _currentAdd;
                applicant.HomeOwnership = _homeOwnership;
                applicant.OwnedHouseLocation = _specificLocation;
                applicant.ModeOfTransportation = _modeOfTransport;
                applicant.PlateNumber = _plateNumber;
                applicant.TravelTime = _travelTime;
                applicant.Fare = _fare;
                _context.DormApplicants.Update(applicant);
            }

            var existingDependents = _context.HousingDependents
                .Where(d => d.ApplicantId == applicant.ApplicantId).ToList();

            var newDependent = new HashSet<string>(_dependents, StringComparer.OrdinalIgnoreCase);

            var toRemove = existingDependents.Where(d => !newDependent.Contains(d.DependentName)).ToList();
            if (toRemove.Any())
            {
                _context.HousingDependents.RemoveRange(toRemove);
            }

            for (int i = 0; i < _dependents.Count; i++)
            {
                var existing = existingDependents
                    .FirstOrDefault(d => d.DependentName.Equals(_dependents[i], StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.DependentAge = _age[i];
                    existing.Status = _dependent_status[i];

                    if (_dependent_status[i].Equals("studying", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.SchoolName = _position[i];
                        existing.SchoolLocation = _location[i];
                        existing.CompanyName = null;
                        existing.CompanyLocation = null;
                    }
                    else if (_dependent_status[i].Equals("working", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.CompanyName = _position[i];
                        existing.CompanyLocation = _location[i];
                        existing.SchoolName = null;
                        existing.SchoolLocation = null;
                    }

                    _context.HousingDependents.Update(existing);
                }
                else
                {
                    var newDep = new GFMS_HousingDependents
                    {
                        ApplicantId = applicant.ApplicantId,
                        DependentName = _dependents[i],
                        DependentAge = _age[i],
                        Status = _dependent_status[i]
                    };

                    if (_dependent_status[i].Equals("studying", StringComparison.OrdinalIgnoreCase))
                    {
                        newDep.SchoolName = _position[i];
                        newDep.SchoolLocation = _location[i];
                    }
                    else if (_dependent_status[i].Equals("working", StringComparison.OrdinalIgnoreCase))
                    {
                        newDep.CompanyName = _position[i];
                        newDep.CompanyLocation = _location[i];
                    }

                    _context.HousingDependents.Add(newDep);
                }
            }

            if (filesToRemove != null && filesToRemove.Any())
            {
                foreach (var fileName in filesToRemove)
                {
                    if (string.IsNullOrWhiteSpace(fileName)) continue;

                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);

                    var fileRecord = _context.FileAttachments
                        .FirstOrDefault(f => f.FileName == fileName && f.ReservationId == reservationId);

                    if (fileRecord != null)
                        _context.FileAttachments.Remove(fileRecord);
                }
            }

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

            _context.SaveChanges();

            TempData["AlertMessage"] = "Application details updated successfully!";
            TempData["AlertSubMessage"] = "Your application has been updated";
            TempData["AlertType"] = "success";

            return RedirectToAction("ViewReservationDetails", new { id = guid });
        }

        [HttpPost]
        public IActionResult TransferDormerRoom(int ReservationId, int ReservationDetailId, int _facilityId, int _roomId)
        {
            //Fetch existing reservation header
            var existingResHeader = _context.ReservationHeaders.FirstOrDefault(rh => rh.ReservationId == ReservationId);
            // Fetch existing reservation details
            var existingReservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == ReservationDetailId);

            if (existingReservation != null)
            {
                // Store previous room and facility before updating
                int previousRoomId = existingReservation.RoomId;
                int previousFacilityId = existingReservation.FacilityId;

                // Save history of room & facility transfer
                var history = new GFMS_DormerRoomHistory
                {
                    DormerId = existingResHeader.RequestorID,
                    PreviousRoomId = previousRoomId,
                    NewRoomId = _roomId,
                    PreviousFacilityId = previousFacilityId,
                    NewFacilityId = _facilityId,
                    TransferDate = DateTime.Now,
                    TransferredBy = User.Identity.Name,
                    Status = "PENDING TRANSFER"
                };
                _context.DormerRoomHistories.Add(history);

                // Update the dormer's room and facility
                existingReservation.RoomId = _roomId;
                existingReservation.FacilityId = _facilityId;
                existingResHeader.Status = "FOR ADMIN MANAGER'S APPROVAL";
                _context.SaveChanges();

                TempData["SuccessMessage"] = "Dormer successfully transferred!";
                return RedirectToAction("Index");
            }

            TempData["ErrorMessage"] = "Error transferring dormer.";
            return RedirectToAction("Index");
        }

        /*Delete*/
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
        public IActionResult TriggerDormRenewalNow()
        {
            // Get dorm reservation details and their headers
            var dormReservations = _context.ReservationDetails
                .Where(d => d.AppliedUnit == 0)
                .Join(
                    _context.ReservationHeaders.Where(h => h.Status == "APPROVED"),
                    d => d.ReservationId,
                    h => h.ReservationId,
                    (d, h) => new { Detail = d, Header = h }
                )
                .ToList();

            foreach (var item in dormReservations)
            {
                var detail = item.Detail;
                var header = item.Header;

                // Log the current room/facility to DormerRoomHistory
                var history = new GFMS_DormerRoomHistory
                {
                    DormerId = header.RequestorID,
                    PreviousRoomId = detail.RoomId,
                    NewRoomId = null,
                    PreviousFacilityId = detail.FacilityId,
                    NewFacilityId = null,
                    TransferDate = DateTime.Now,
                    TransferredBy = User.Identity?.Name ?? "System",
                    Status = "FOR RENEWAL"
                };
                _context.DormerRoomHistories.Add(history);

                // Reset room/facility to allow new assignment
                detail.RoomId = 0;
                detail.FacilityId = 0;

                // Update header status
                header.Status = "FOR RENEWAL";
                header.isForRenewal = true;
            }

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Dorm renewal status has been updated.";
            return RedirectToAction("ReservationList", "Dorm");
        }

        public JsonResult GetRenewableDormApplications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var employee = _context.Employees.FirstOrDefault(e => e.UserId == userId);
            if (employee == null)
            {
                return Json(new { success = false, message = "Employee not found." });
            }

            var employeeId = employee.EmployeeID.ToString();
            Console.WriteLine("employeeId:" + employeeId);

            // First, get all ReservationIds that are already marked as renewal
            var alreadyRenewedIds = _context.ReservationHeaders
                .Where(h => h.isForRenewal == true && h.RequestorID == employeeId && h.ReservationType == "DRM")
                .Select(h => h.PreviousReservationId)
                .Where(id => id.HasValue)
                .ToList();

            // Now, fetch only FOR RENEWAL that are not yet renewed
            var applications = _context.ReservationDetails
                .Include(r => r.ReservationHeader)
                .Where(r =>
                    r.ReservationHeader.Status == "FOR RENEWAL" &&
                    r.ReservationHeader.RequestorID == employeeId &&
                    r.ReservationHeader.ReservationType == "DRM" &&
                    !alreadyRenewedIds.Contains(r.ReservationHeader.ReservationId))
                .OrderByDescending(r => r.EndDate)
                .Select(r => new
                {
                    Id = r.ReservationHeader.ReservationId,
                    DisplayText = r.ReservationHeader.ReservationCode
                })
                .ToList();

            return Json(applications);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateReservationStatusAsync(
            int ReservationId,
            string action,
            int _facilityId,
            int _roomId,
            string Remarks,
            bool _isCritical,
            bool _isOnCall,
            string _deptRemarks)
        {
            if (ReservationId <= 0 || string.IsNullOrEmpty(action))
            {
                return BadRequest("Invalid parameters");
            }

            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            if (reservation == null)
            {
                return NotFound();
            }

            var reservationDetails = _context.ReservationDetails.FirstOrDefault(r => r.ReservationId == ReservationId);
            var roomName = _context.Rooms.Where(r => r.RoomId == _roomId).Select(r => r.RoomName).FirstOrDefault();
            var facilityName = _context.Facilities.Where(f => f.FacilityId == _facilityId).Select(f => f.FacilityName).FirstOrDefault();
            var empName = _context.Employees.FirstOrDefault(e => e.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));
            var dormApplicant = _context.DormApplicants.FirstOrDefault(d => d.UserId == empName.UserId);

            var department = empName != null
            ? _context.Departments.FirstOrDefault(d => d.Id == empName.DepartmentId)
            : null;

            var company = empName != null
                ? _context.Companies.FirstOrDefault(c => c.Id == empName.CompanyId)
                : null;

            var controllerName = "Dorm";

            var approverEmail = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Approver");
            var adminManagerEmail = await _roleService.GetEmailsByRoleAsync("GFMS.Admin Manager");

            string appliedUnit = reservationDetails?.AppliedUnit == 0 ? "DORM" : "HOUSING";
            string scheduleType = dormApplicant?.SchedType == 0 ? "REGULAR" : "SHIFTING";

            var dormerHistory = _context.DormerRoomHistories
            .Where(d => d.DormerId == reservation.RequestorID)
            .OrderByDescending(d => d.TransferDate)
            .FirstOrDefault();

            if (action == "Approve" && reservation.Status == "FOR DEPARTMENT HEAD'S APPROVAL")
            {
                reservation.Status = "FOR ADMIN APPROVER'S APPROVAL";
                reservation.ApprovedDateTime = DateTime.Now;

                if (reservationDetails != null)
                {
                    dormApplicant.DeptHeadRemarks = _deptRemarks;
                    dormApplicant.isCritical = _isCritical;
                    dormApplicant.isOnCall = _isOnCall;
                }

                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                await _emailService.SendPendingRoomAssignmentEmail(
                    reservation,
                    approverEmail,
                    controllerName,
                    department?.DepartmentCode
                );
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN MANAGER'S APPROVAL")
            {
                reservation.Status = "APPROVED";
                reservation.ApprovedBy = empName?.Fullname;
                reservation.ApprovedDateTime = DateTime.Now;

                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                if (reservation.isForRenewal == true)
                {
                    var history = new GFMS_DormerRoomHistory
                    {
                        DormerId = reservation.RequestorID,
                        NewRoomId = _roomId,
                        NewFacilityId = _facilityId,
                        TransferDate = DateTime.Now,
                        TransferredBy = User.Identity?.Name,
                        Status = "RENEWAL APPROVED"
                    };
                    _context.DormerRoomHistories.Add(history);
                }

                await _emailService.SendApprovalNotification(
                    reservation,
                    reservation.RequestorEmail,
                    reservation.GUID,
                    controllerName
                );
            }
            else if (action == "Approve" && reservation.Status == "FOR ADMIN APPROVER'S APPROVAL")
            {
                reservation.Status = "FOR ADMIN MANAGER'S APPROVAL";

                if (reservationDetails != null)
                {
                    reservationDetails.FacilityId = _facilityId;
                    reservationDetails.RoomId = _roomId;
                }

                reservation.ApprovedBy = empName?.Fullname;
                reservation.ApprovedDateTime = DateTime.Now;

                TempData["AlertMessage"] = "Room details assigned successfully!";
                TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                TempData["AlertType"] = "success";

                await _emailService.DRMRoomAssignmentApprovalEmail(
                    reservation,
                    appliedUnit,
                    scheduleType,
                    facilityName,
                    roomName,
                    reservation.GUID,
                    controllerName,
                    adminManagerEmail
                );
            }
            else if (action == "ApproveTransfer")
            {
                reservation.Status = "APPROVED";
                reservation.ApprovedBy = empName?.Fullname;

                if (dormerHistory != null)
                {
                    dormerHistory.Status = "TRANSFER APPROVED";
                }

                TempData["AlertMessage"] = "Transfer Approved!";
                TempData["AlertSubMessage"] = "Facility and Room transferred successfully.";
                TempData["AlertType"] = "success";
            }
            else if (action == "RejectTransfer")
            {
                if (reservationDetails != null && dormerHistory != null)
                {
                    reservationDetails.FacilityId = dormerHistory.PreviousFacilityId;
                    reservationDetails.RoomId = dormerHistory.PreviousRoomId;
                }

                reservation.Status = "APPROVED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName?.Fullname;

                if (dormerHistory != null)
                {
                    dormerHistory.Status = "TRANSFER DISAPPROVED";
                }

                TempData["AlertMessage"] = "Transfer Rejected!";
                TempData["AlertSubMessage"] = "Facility and Room reverted back to the original values.";
                TempData["AlertType"] = "danger";
            }
            else if (action == "Reject")
            {
                reservation.Status = "DISAPPROVED";
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = empName?.Fullname;

                await _emailService.SendDisapprovalNotification(
                    reservation,
                    reservation.RequestorEmail,
                    reservation.GUID,
                    controllerName
                );

                TempData["AlertMessage"] = "Rejected!";
                TempData["AlertSubMessage"] = "Reservation has been disapproved.";
                TempData["AlertType"] = "danger";
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
