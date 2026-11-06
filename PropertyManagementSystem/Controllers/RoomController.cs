using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize("GFMS.System Admin", "GFMS.Admin Approver", "GFMS.Admin Manager")]
    public class RoomController : Controller
    {
        private readonly DAL _context;
        private readonly ILogger<RoomController> _logger;
        private readonly ISetupReservationGuard _reservationGuard;

        public RoomController(
            DAL context,
            ILogger<RoomController> logger,
            ISetupReservationGuard reservationGuard)
        {
            _context = context;
            _logger = logger;
            _reservationGuard = reservationGuard;
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GuestRoom(string roomName)
        {

            return View();
            

        }

        public IActionResult ViewGuestRoom(int RoomId, [DataSourceRequest] DataSourceRequest request) 
        {
            var roomDetails = (from room in _context.Rooms
                               join facilityList in _context.Facilities on room.FacilityId equals facilityList.FacilityId
                               where room.RoomId == RoomId
                               select new
                               {
                                   RoomId = room.RoomId,
                                   RoomName = room.RoomName,
                                   RoomStatus = room.RoomStatus,
                                   FacilityId = room.FacilityId,
                                   FacilityName = facilityList.FacilityName,
                                   FacilityLocation = facilityList.FacilityLocation,
                                   Capacity = room.Capacity,
                                   isActive = room.isActive,
                                   RoomType = room.RoomType,
                                   CostPerDay = room.CostPerDay,
                                   CostPerWeek = room.CostPerWeek,
                                   CostPerMonth = room.CostPerMonth,
                                   isPerHead = room.isPerHead

                               }).FirstOrDefault();
            if (roomDetails == null)
            {
                return NotFound();
            }
            var occupants = (from detail in _context.ReservationDetails.AsNoTracking()
                             join header in _context.ReservationHeaders.AsNoTracking()
                                 on detail.ReservationId equals header.ReservationId
                             where detail.RoomId == RoomId
                                   && header.Status == ReservationStatuses.Approved
                                   && detail.StartDate.HasValue
                                   && detail.EndDate.HasValue
                             select new RoomOccupantViewModel
                             {
                                 OccupantName = detail.GuestName ?? header.Requestor ?? "Unnamed occupant",
                                 Company = detail.Company,
                                 ReservationCode = header.ReservationCode,
                                 ArrivalDate = detail.StartDate.GetValueOrDefault(),
                                 DepartureDate = detail.EndDate.GetValueOrDefault()
                             })
                .ToList();

            var now = DateTime.Now;
            occupants = occupants
                .OrderBy(occupant =>
                    occupant.ArrivalDate <= now && occupant.DepartureDate >= now ? 0 :
                    occupant.ArrivalDate > now ? 1 : 2)
                .ThenBy(occupant => occupant.ArrivalDate)
                .ThenBy(occupant => occupant.OccupantName)
                .ToList();

            var roomViewModel = new RoomViewModel
            {
                RoomId = roomDetails.RoomId,
                RoomName = roomDetails.RoomName,
                RoomStatus = roomDetails.RoomStatus,
                FacilityId = roomDetails.FacilityId,
                FacilityName = roomDetails.FacilityName,
                FacilityLocation = roomDetails.FacilityLocation,
                Capacity = roomDetails.Capacity,
                isActive = roomDetails.isActive,
                RoomType = roomDetails.RoomType,
                CostPerDay = roomDetails.CostPerDay,
                CostPerWeek = roomDetails.CostPerWeek,
                CostPerMonth = roomDetails.CostPerMonth,
                isPerHead = roomDetails.isPerHead,
                Occupants = occupants

            };

            return View(roomViewModel);
        }
        public IActionResult FunctionRoom()
        {

            return View();

        }
        public IActionResult DormRoom()
        {

            return View();

        }
        /*Display*/
        [HttpGet]
        public ActionResult getGHRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms
                .AsQueryable()
                .Where(x => x.RoomType == 0)
                .ToList();
            DataSourceResult result = _Rooms.ToDataSourceResult(request, _Room => new
            {
                RoomId = _Room.RoomId,
                RoomStatus = _Room.RoomStatus,
                RoomName = _Room.RoomName,
                FacilityId = _Room.FacilityId,
                Capacity = _Room.Capacity,
                isActive = _Room.isActive
            });
            return Json(result);

        }

        public ActionResult getGHRoomDetail( int roomId, [DataSourceRequest] DataSourceRequest dataSourceRequest) 
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms
                .Where(x => x.RoomId == roomId)
                .ToList();

            DataSourceResult result = _Rooms.ToDataSourceResult(dataSourceRequest, _Room => new
            {
                RoomId = roomId,
                RoomStatus = _Room.RoomStatus,
                RoomName = _Room.RoomName,
                FacilityId = _Room.FacilityId,
                Capacity = _Room.Capacity,
                isActive = _Room.isActive
            });
            return Json(result);
        }
        [HttpGet]
        public ActionResult getFNRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms
                .AsQueryable()
                .Where(x => x.RoomType == 1)
                .ToList();
            DataSourceResult result = _Rooms.ToDataSourceResult(request, _Room => new
            {
                RoomId = _Room.RoomId,
                RoomName = _Room.RoomName,
                FacilityId = _Room.FacilityId,
                Capacity = _Room.Capacity,
                isActive = _Room.isActive,
                RoomType = _Room.RoomType,
                CostPerDay = _Room.CostPerDay,
                CostPerWeek = _Room.CostPerWeek,
                CostPerMonth = _Room.CostPerMonth,
                isPerHead = _Room.isPerHead,
            });
            return Json(result);

        }
        public IActionResult GetFNRoomList(int facilityId)
        {
            var rooms = _context.Rooms
                .Where(r => r.FacilityId == facilityId)
                .Select(r => new
                {
                    RoomId = r.RoomId,
                    RoomName = r.RoomName
                })
                .ToList();

            return Json(rooms);
        }
        [HttpGet]
        public ActionResult getDMRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms.AsQueryable()
                .Where(X => X.RoomType == 2)
                .ToList();
            DataSourceResult result = _Rooms.ToDataSourceResult(request, _Room => new
            {
                RoomId = _Room.RoomId,
                RoomName = _Room.RoomName,
                FacilityId = _Room.FacilityId,
                Capacity = _Room.Capacity,
                isActive = _Room.isActive
            });
            return Json(result);

        }
        /*Create*/
        [HttpPost]
        public IActionResult newGHRoom(
            string _name,
            string _status,
            int _facility,
            int _capacity,
            bool _isActive,
            double? _costPerDay,
            double? _costPerWeek,
            double? _costPerMonth)
        {
            if (!ValidateCapacity(_capacity))
            {
                return RedirectToAction("GuestRoom", "Room");
            }
            if (!ValidateOptionalRoomCosts(_costPerDay, _costPerWeek, _costPerMonth))
            {
                return RedirectToAction("GuestRoom", "Room");
            }

            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 0,
                CostPerDay = _costPerDay,
                CostPerWeek = _costPerWeek,
                CostPerMonth = _costPerMonth
            };


            if (!TryCreateRoom(_room, "Guest room"))
            {
                return RedirectToAction("GuestRoom", "Room");
            }

            return RedirectToAction("GuestRoom", "Room");
        }
        [HttpPost]
        public IActionResult newFNRoom(
            string _name,
            string _status,
            int _facility,
            int _capacity,
            bool _isActive,
            double? _costPerDay,
            double? _costPerWeek,
            double? _costPerMonth)
        {
            if (!ValidateCapacity(_capacity))
            {
                return RedirectToAction("FunctionRoom", "Room");
            }
            if (!ValidateOptionalRoomCosts(_costPerDay, _costPerWeek, _costPerMonth))
            {
                return RedirectToAction("FunctionRoom", "Room");
            }

            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 1,
                CostPerDay = _costPerDay,
                CostPerWeek = _costPerWeek,
                CostPerMonth = _costPerMonth
            };


            if (!TryCreateRoom(_room, "Function room"))
            {
                return RedirectToAction("FunctionRoom", "Room");
            }

            return RedirectToAction("FunctionRoom", "Room");
        }
        [HttpPost]
        public IActionResult newDMRoom(string _name, string _status, int _facility, int _capacity, bool _isActive)
        {
            if (!ValidateCapacity(_capacity))
            {
                return RedirectToAction("DormRoom", "Room");
            }

            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus= _status,    
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 2
            };


            if (!TryCreateRoom(_room, "Dorm room"))
            {
                return RedirectToAction("DormRoom", "Room");
            }

            return RedirectToAction("DormRoom", "Room");
        }

        /*Update*/

        [HttpPost]
        public IActionResult EditGHRoom(
            int _roomId,
            string _roomName,
            int _facility,
            string _roomStatus,
            int _capacity,
            bool _isActive,
            double? _costPerDay,
            double? _costPerWeek,
            double? _costPerMonth)
        {
            var ghRoom = _context.Rooms.FirstOrDefault(gh => gh.RoomId == _roomId);
            if (ghRoom == null)
            {
                SetToast("Room not found", "The selected room may have already been removed.", "error");
                return RedirectToAction("GuestRoom", "Room");
            }

            if (!ValidateCapacity(_capacity))
            {
                return RedirectToAction("ViewGuestRoom", "Room", new { RoomId = _roomId });
            }
            if (!ValidateOptionalRoomCosts(_costPerDay, _costPerWeek, _costPerMonth))
            {
                return RedirectToAction("ViewGuestRoom", "Room", new { RoomId = _roomId });
            }

            if (ghRoom.isActive && !_isActive)
            {
                var blockingReservationCount = _reservationGuard
                    .CountBlockingReservationsForRoom(_roomId, DateTime.Now);

                if (blockingReservationCount > 0)
                {
                    var reservationLabel = blockingReservationCount == 1
                        ? "reservation"
                        : "reservations";
                    SetToast(
                        $"{GetRoomTypeLabel(ghRoom.RoomType)} cannot be made inactive",
                        $"{ghRoom.RoomName} has {blockingReservationCount} pending, ongoing, or upcoming {reservationLabel}. Reassign, cancel, or complete them before changing its status.",
                        "warning");
                    return RedirectToAction("ViewGuestRoom", "Room", new { RoomId = _roomId });
                }
            }

            ghRoom.RoomName = _roomName;
            ghRoom.FacilityId = _facility;
            ghRoom.RoomStatus = _roomStatus;
            ghRoom.Capacity = _capacity;
            ghRoom.isActive = _isActive;
            ghRoom.CostPerDay = _costPerDay;
            ghRoom.CostPerWeek = _costPerWeek;
            ghRoom.CostPerMonth = _costPerMonth;

            try
            {
                _context.Rooms.Update(ghRoom);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to update room {RoomId}.", _roomId);
                SetToast(
                    $"{GetRoomTypeLabel(ghRoom.RoomType)} could not be updated",
                    "The changes were not saved. Review the details and try again.",
                    "error");
                return RedirectToAction("ViewGuestRoom", "Room", new { RoomId = _roomId });
            }
            SetToast(
                $"{GetRoomTypeLabel(ghRoom.RoomType)} updated",
                $"Changes to {ghRoom.RoomName} were saved successfully.",
                "success");
            string redirectUrl = Url.Action("ViewGuestRoom", "Room", new { RoomId = _roomId });
            return Redirect(redirectUrl);
        }

        /*Delete*/
        [HttpPost]
        public IActionResult DeleteGHRoom(int RoomId) 
        {
            GFMS_Rooms room = _context.Rooms.Find(RoomId);
            if(room == null)
            {
                SetToast("Guest room not found", "The selected room may have already been removed.", "error");
                return RedirectToAction("GuestRoom", "Room");
            }
            DeleteRoom(room);
            return RedirectToAction("GuestRoom", "Room");

        }

        [HttpPost]
        public IActionResult DeleteDMRoom(int RoomId)
        {
            GFMS_Rooms room = _context.Rooms.Find(RoomId);
            if (room == null)
            {
                SetToast("Dorm room not found", "The selected room may have already been removed.", "error");
                return RedirectToAction("DormRoom", "Room");
            }
            DeleteRoom(room);

            return RedirectToAction("DormRoom", "Room");
        }

        public IActionResult DeleteFNRoom(int RoomId)
        {
            GFMS_Rooms room = _context.Rooms.Find(RoomId);
            if(room == null)
            {
                SetToast("Function room not found", "The selected room may have already been removed.", "error");
                return RedirectToAction("FunctionRoom", "Room");
            }

            DeleteRoom(room);

            return RedirectToAction("FunctionRoom", "Room");
        }

        private bool TryCreateRoom(GFMS_Rooms room, string roomTypeLabel)
        {
            try
            {
                _context.Rooms.Add(room);
                _context.SaveChanges();
                SetToast(
                    $"{roomTypeLabel} created",
                    $"{room.RoomName} is now available in the room directory.",
                    "success");
                return true;
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to create {RoomType} {RoomName}.", roomTypeLabel, room.RoomName);
                SetToast(
                    $"{roomTypeLabel} could not be created",
                    "Review the room details and try again.",
                    "error");
                return false;
            }
        }

        private bool ValidateOptionalRoomCosts(params double?[] costs)
        {
            if (!ModelState.IsValid)
            {
                SetToast("Room rates are invalid", "Enter valid numeric room rates or leave the optional fields blank.", "warning");
                return false;
            }
            if (costs.Any(cost => cost.HasValue && (cost.Value < 0 || double.IsNaN(cost.Value) || double.IsInfinity(cost.Value))))
            {
                SetToast("Room rates are invalid", "Room rates cannot be negative or outside the supported numeric range.", "warning");
                return false;
            }
            return true;
        }

        private void DeleteRoom(GFMS_Rooms room)
        {
            var roomTypeLabel = GetRoomTypeLabel(room.RoomType);
            try
            {
                _context.Rooms.Remove(room);
                _context.SaveChanges();
                SetToast(
                    $"{roomTypeLabel} deleted",
                    $"{room.RoomName} was removed from the room directory.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to delete room {RoomId}.", room.RoomId);
                SetToast(
                    $"{roomTypeLabel} could not be deleted",
                    "This room may still be linked to equipment, occupants, or reservations.",
                    "error");
            }
        }

        private static string GetRoomTypeLabel(int roomType) => roomType switch
        {
            0 => "Guest room",
            1 => "Function room",
            2 => "Dorm room",
            _ => "Room"
        };

        private bool ValidateCapacity(int capacity)
        {
            if (RoomCapacityPolicy.IsValid(capacity))
            {
                return true;
            }

            SetToast(
                "Invalid room capacity",
                RoomCapacityPolicy.ValidationMessage,
                "warning");
            return false;
        }

        private void SetToast(string title, string message, string type)
        {
            TempData["ToastTitle"] = title;
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = type;
        }
    }
}
