using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using PropertyManagementSystem.Attributes;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class RoomController : Controller
    {
        private readonly DAL _context;
        public RoomController(DAL context)
        {
            _context = context;
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult Index()
        {
            return View();
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
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
                                   FacilityName = facilityList.FacilityName,
                                   FacilityLocation = facilityList.FacilityLocation,
                                   Capacity = room.Capacity,
                                   isActive = room.isActive,
                                   RoomType = room.RoomType

                               }).FirstOrDefault();
            if (roomDetails == null)
            {
                return NotFound();
            }
            var roomViewModel = new RoomViewModel
            {
                RoomId = roomDetails.RoomId,
                RoomName = roomDetails.RoomName,
                RoomStatus = roomDetails.RoomStatus,
                FacilityName = roomDetails.FacilityName,
                FacilityLocation = roomDetails.FacilityLocation,
                Capacity = roomDetails.Capacity,
                isActive = roomDetails.isActive,
                RoomType = roomDetails.RoomType

            };

            return View(roomViewModel);
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult FunctionRoom()
        {

            return View();

        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult DormRoom()
        {

            return View();

        }
        /*Display*/
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
        public IActionResult newGHRoom(string _name, string _status, int _facility,  int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 0
            };


            _context.Rooms.Add(_room);
            _context.SaveChanges();

            return RedirectToAction("GuestRoom", "Room");
        }
        [HttpPost]
        public IActionResult newFNRoom(string _name, string _status, int _facility, int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 1
            };


            _context.Rooms.Add(_room);
            _context.SaveChanges();

            return RedirectToAction("FunctionRoom", "Room");
        }
        [HttpPost]
        public IActionResult newDMRoom(string _name, string _status, int _facility, int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
                RoomStatus= _status,    
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                RoomType = 2
            };


            _context.Rooms.Add(_room);
            _context.SaveChanges();

            return RedirectToAction("DormRoom", "Room");
        }

        /*Update*/

        [HttpPost]
        public IActionResult EditGHRoom(GFMS_Rooms room, int _roomId, string _roomName, int _facility, string _roomStatus, int _capacity, bool _isActive, double _costPerDay)
        {
            var ghRoom = _context.Rooms.FirstOrDefault(gh => gh.RoomId == _roomId);
            if (ghRoom == null)
            {
                return NotFound("Reservation detail not found.");
            }
            ghRoom.RoomName = _roomName;
            ghRoom.FacilityId = _facility;
            ghRoom.RoomStatus = _roomStatus;
            ghRoom.Capacity = _capacity;
            ghRoom.isActive = _isActive;
            ghRoom.CostPerDay = _costPerDay;

            try
            {
                _context.Rooms.Update(ghRoom);
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
                return NotFound();
            }
            _context.Rooms.Remove(room);
            _context.SaveChanges();

            
            return RedirectToAction("GuestRoom", "Room");

        }

        [HttpPost]
        public IActionResult DeleteDMRoom(int RoomId)
        {
            GFMS_Rooms room = _context.Rooms.Find(RoomId);
            if (room == null)
            {
                return NotFound();
            }
            _context.Rooms.Remove(room);
            _context.SaveChanges();

            return RedirectToAction("DormRoom", "Room");
        }

        public IActionResult DeleteFNRoom(int RoomId)
        {
            GFMS_Rooms room = _context.Rooms.Find(RoomId);
            if(room == null)
            {
                return NotFound();
            }

            _context.Rooms.Remove(room);
            _context.SaveChanges();

            return RedirectToAction("FunctionRoom", "Room");
        }
    }
}
