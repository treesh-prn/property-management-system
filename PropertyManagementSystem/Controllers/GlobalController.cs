using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class GlobalController : Controller
    {
        private readonly DAL _context;
        private readonly IReservationAuthorizationService _authorization;
        private readonly IReservationAuditService _audit;
        private readonly IReservationEquipmentAllocationService _equipmentAllocations;

        public GlobalController(
            DAL context,
            IReservationAuthorizationService authorization,
            IReservationAuditService audit,
            IReservationEquipmentAllocationService equipmentAllocations)
        {

            _context = context;
            _authorization = authorization;
            _audit = audit;
            _equipmentAllocations = equipmentAllocations;

        }
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult getCompanies()
        {
            var _companyList = _context.Companies.Select(c => new { c.Id, Company = c.Company.ToUpper(), CompanyCode = c.CompanyCode }).AsQueryable().ToList();

            return Json(_companyList, new JsonSerializerSettings());
        }
        public string GenerateId()
        {
            //{00000000-0000-0000-0000-000000000000}
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            return unique;
        }

        public JsonResult getDepartments(int companyId)
        {
            var _deptList = _context.Departments.Where(d => d.CompanyId == companyId);

            return Json(_deptList, new JsonSerializerSettings());
        }

        public IActionResult GetAllFacility()
        {
            var facilities = _context.Facilities
                .Where(f => f.isActive == true)
                .Select(f => new
                {
                    FacilityId = f.FacilityId,
                    FacilityName = f.FacilityName,

                }).ToList();

            return Json(facilities);
        }

        public IActionResult GetAllRooms(int facilityId)
        {
            var rooms = _context.Rooms
               .Where(room => room.FacilityId == facilityId)
               .Select(r => new
               {
                   RoomId = r.RoomId,
                   RoomName = r.RoomName
               }).ToList();

            return Json(rooms);
        }

        public IActionResult GetFunctionRoomFacilities()
        {
            var facilities = _context.Facilities
                .AsNoTracking()
                .Where(facility => facility.isActive == true &&
                    facility.FacilityClassification == "FUNCTION ROOM")
                .OrderBy(facility => facility.FacilityName)
                .Select(facility => new
                {
                    facility.FacilityId,
                    facility.FacilityName
                })
                .ToList();

            return Json(facilities);
        }

        public IActionResult GetFunctionRooms(int facilityId)
        {
            var rooms = _context.Rooms
                .AsNoTracking()
                .Where(room => room.FacilityId == facilityId &&
                    room.RoomType == ReservationRoomTypes.FunctionRoom &&
                    room.isActive &&
                    _context.Facilities.Any(facility =>
                        facility.FacilityId == room.FacilityId &&
                        facility.isActive == true &&
                        facility.FacilityClassification == "FUNCTION ROOM"))
                .OrderBy(room => room.RoomName)
                .Select(room => new
                {
                    room.RoomId,
                    room.RoomName
                })
                .ToList();

            return Json(rooms);
        }

        public async Task<IActionResult> GetFacility(int ReservationDetailId)
        {
            Console.WriteLine("ReservationDetailId:" + ReservationDetailId);
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == ReservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "FN") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "FUNCTION ROOM")
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        !_context.ReservationDetails.Any(rd =>
                            rd.RoomId == room.RoomId &&
                            rd.ReservationDetailId != ReservationDetailId &&
                            ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                             (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                             (rd.StartDate >= startDate && rd.EndDate <= endDate))))
                })
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            var selectedFacility = _context.Facilities
                .Where(f => f.FacilityId == reservation.FacilityId)
                .Select(f => new { FacilityId = f.FacilityId, FacilityName = f.FacilityName })
                .FirstOrDefault();

            if (selectedFacility != null && !facilities.Any(f => f.FacilityId == selectedFacility.FacilityId))
            {
                facilities.Insert(0, selectedFacility);
            }


            return Json(facilities);
        }
        public async Task<IActionResult> GetRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine("FacilityId: " + facilityId);
            Console.WriteLine("Reservation Id: " + reservationDetailId);
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "FN") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var rooms = _context.Rooms
         .Where(room => room.FacilityId == facilityId &&
                   !_context.ReservationDetails.Any(rd =>
                       rd.RoomId == room.RoomId &&
                       rd.ReservationDetailId != reservationDetailId && 
                       ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                        (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                        (rd.StartDate >= startDate && rd.EndDate <= endDate))))
         .Select(room => new { RoomId = room.RoomId, RoomName = room.RoomName })
         .ToList();

            var selectedRoom = _context.Rooms
                .Where(room => room.RoomId == reservation.RoomId)
                .Select(room => new { RoomId = room.RoomId, RoomName = room.RoomName })
                .FirstOrDefault();

            if (selectedRoom != null && !rooms.Any(r => r.RoomId == selectedRoom.RoomId))
            {
                rooms.Insert(0, selectedRoom);
            }


            return Json(rooms);
        }

        public async Task<IActionResult> GetGH(int ReservationDetailId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == ReservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "GH") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "GUESTHOUSE" || f.FacilityId == reservation.FacilityId)
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        room.isActive == false ||

                        !_context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Any(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate))) &&
                        _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Where(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                            .Select(joined => joined.rd.GuestName)
                            .Count() < room.Capacity)
                })
                .Where(f => f.AvailableRooms.Any())
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName,
                    FacilityClass = f.Facility.FacilityClassification
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            // Select the current facility based on the reservation's FacilityId
            var selectedFacility = _context.Facilities
        .Where(f => f.FacilityId == reservation.FacilityId)
        .Select(f => new
        {
            FacilityId = f.FacilityId,
            FacilityName = f.FacilityName,
            FacilityClass = f.FacilityClassification // Include FacilityClass
        })
        .FirstOrDefault();

            // Insert the selected facility at the top if it's not already in the list
            if (selectedFacility != null && !facilities.Any(f => f.FacilityId == selectedFacility.FacilityId))
            {
                facilities.Insert(0, selectedFacility);
            }

            return Json(facilities);
        }
        public async Task<IActionResult> GetGHRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine(facilityId);
            Console.WriteLine(reservationDetailId);

            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "GH") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    OccupiedGuestCount = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(joined =>
                            room.isActive == false ||
                            joined.rd.RoomId == room.RoomId &&
                            joined.rh.Status != "CANCELLED" &&
                            joined.rh.Status != "DISAPPROVED" &&
                            ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                             (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                             (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                        .Select(joined => joined.rd.GuestName)
                        .Count(),
                    RoomCapacity = room.Capacity
                })
                .Where(room => room.OccupiedGuestCount < room.RoomCapacity)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .ToList();

            // Add the initially selected room if it's not already in the list
            var selectedRoom = _context.Rooms
                .Where(room => room.RoomId == reservation.RoomId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .FirstOrDefault();

            if (selectedRoom != null && !rooms.Any(r => r.RoomId == selectedRoom.RoomId))
            {
                rooms.Insert(0, selectedRoom);
            }

            return Json(rooms);
        }

        public async Task<IActionResult> GetDRM(int reservationDetailId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "DRM") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "DORM")
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        room.isActive == false ||

                        !_context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Any(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate))) &&
                        _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Where(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                            .Select(joined => joined.rd.GuestName)
                            .Count() < room.Capacity)
                })
                .Where(f => f.AvailableRooms.Any())
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName,
                    FacilityClass = f.Facility.FacilityClassification
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            return Json(facilities);
        }

        public async Task<IActionResult> GetDRMRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine(facilityId);
            Console.WriteLine(reservationDetailId);

            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }
            var header = GetHeaderForDetail(reservation);
            if (header == null || header.ReservationType != "DRM") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    OccupiedGuestCount = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(joined =>
                            room.isActive == false ||
                            joined.rd.RoomId == room.RoomId &&
                            joined.rh.Status != "CANCELLED" &&
                            joined.rh.Status != "DISAPPROVED" &&
                            ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                             (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                             (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                        .Select(joined => joined.rd.GuestName)
                        .Count(),
                    RoomCapacity = room.Capacity
                })
                .Where(room => room.OccupiedGuestCount < room.RoomCapacity)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .ToList();

            // Add the initially selected room if it's not already in the list
            var selectedRoom = _context.Rooms
                .Where(room => room.RoomId == reservation.RoomId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .FirstOrDefault();

            if (selectedRoom != null && !rooms.Any(r => r.RoomId == selectedRoom.RoomId))
            {
                rooms.Insert(0, selectedRoom);
            }

            return Json(rooms);
        }
        [HttpGet]
        public async Task<IActionResult> GetEquipmentFNAssign(int facilityId, int roomId)
        {
            var actor = await _authorization.GetActorAsync(User);
            if (actor.ActiveRole is not ReservationRoles.AdminApprover and
                not ReservationRoles.AdminManager)
            {
                return Forbid();
            }
            var equipment = _context.FacilityEquipmentLinks
                .AsNoTracking()
                .Where(link => link.IsActive &&
                    link.LinkType == EquipmentLinkTypes.Included &&
                    link.FacilityId == facilityId &&
                    (!link.RoomId.HasValue || link.RoomId == roomId))
                .Join(_context.Equipments.AsNoTracking().Where(item => item.isActive != false),
                    link => link.EquipmentId,
                    item => item.Id,
                    (link, item) => new
                    {
                        item.Id,
                        item.EquipmentName,
                        link.DefaultQuantity,
                        Source = link.RoomId.HasValue ? "Room" : "Facility"
                    })
                .Distinct()
                .OrderBy(item => item.EquipmentName)
                .ToList();

            return Json(equipment);
        }
        public ActionResult GetEquipments(
            DateTime startDate,
            DateTime endDate,
            int? facilityId = null,
            int? roomId = null)
        {
            var addOnIds = _context.FacilityEquipmentLinks.AsNoTracking()
                .Where(link => facilityId.HasValue && link.IsActive &&
                    link.LinkType == EquipmentLinkTypes.AddOn &&
                    link.FacilityId == facilityId.Value &&
                    (!roomId.HasValue || !link.RoomId.HasValue || link.RoomId == roomId.Value))
                .Select(link => link.EquipmentId);
            var includedIds = _context.FacilityEquipmentLinks.AsNoTracking()
                .Where(link => link.IsActive && link.LinkType == EquipmentLinkTypes.Included)
                .Select(link => link.EquipmentId);
            var equipments = _context.Equipments
                .AsNoTracking()
                .Where(eq => eq.isActive == true)
                .Where(eq => !includedIds.Contains(eq.Id) || addOnIds.Contains(eq.Id))
                .OrderBy(eq => eq.EquipmentName)
                .Select(eq => new equipmentList
                {
                    Id = eq.Id,
                    EquipmentName = eq.EquipmentName ?? string.Empty
                })
                .ToList();

            return Json(equipments);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReservation(int ReservationId, string Remarks, string reservationType)
        {
            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            if (reservation == null)
            {
                return NotFound();
            }

            var access = await _authorization.AuthorizeAsync(
                User, reservation, ReservationOperation.Cancel);
            var denied = AuthorizationFailure(access);
            if (denied != null) return denied;

            var previousStatus = reservation.Status;
            reservation.Status = ReservationStatuses.Cancelled;
            reservation.Remarks = Remarks;
            try
            {
                await _equipmentAllocations.ReleaseReservationAsync(reservation.ReservationId);
                _context.SaveChanges();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("The reservation state changed. Refresh and try again.");
            }
            await _audit.AddAsync(
                reservation, User, "CANCEL", previousStatus, reservation.Status);

            return reservation.ReservationType switch
            {
                "FN" => RedirectToAction("ReservationList", "FunctionRoom"),
                "GH" => RedirectToAction("ReservationList", "Accommodation"),
                "DRM" => RedirectToAction("ReservationList", "Dorm"),
                _ => RedirectToAction("Index", "Home")
            };
        }
        public ActionResult getDormList([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.Where(f => f.FacilityClassification == "DORM").ToList();
            DataSourceResult result = _Facilities.ToDataSourceResult(request, _Facility => new
            {
                FacilityId = _Facility.FacilityId,
                FacilityName = _Facility.FacilityName,
                FacilityClassification = _Facility.FacilityClassification,
                FacilityLocation = _Facility.FacilityLocation,
                isActive = _Facility.isActive,
                RoomCount = _context.Rooms.Count(room => room.FacilityId == _Facility.FacilityId && room.RoomType == 2),
                TotalCapacity = _context.Rooms
                    .Where(room => room.FacilityId == _Facility.FacilityId && room.RoomType == 2)
                    .Sum(room => (int?)room.Capacity) ?? 0,
                OccupiedCount = _context.ReservationDetails
                    .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                    .Count(x => x.rd.FacilityId == _Facility.FacilityId && x.rh.ReservationType == "DRM" && x.rh.Status == "APPROVED")
            });
            return Json(result);

        }

        private GFMS_ReservationHeader? GetHeaderForDetail(GFMS_ReservationDetails detail) =>
            _context.ReservationHeaders.FirstOrDefault(h => h.ReservationId == detail.ReservationId);

        private IActionResult? AuthorizationFailure(ReservationAuthorizationResult authorization)
        {
            if (authorization.Succeeded) return null;
            return authorization.Failure == ReservationAuthorizationFailure.Forbidden
                ? Forbid()
                : Conflict("The reservation state changed. Refresh and try again.");
        }
    }
}
