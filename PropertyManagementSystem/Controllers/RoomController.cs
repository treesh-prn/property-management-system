using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Cryptography.X509Certificates;

namespace PropertyManagementSystem.Controllers
{
    public class RoomController : Controller
    {
        private readonly DAL _context;
        public RoomController(DAL context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GuestRoom(string roomName)
        {

            return View();
            

        }

        public IActionResult ViewGuestRoom() 
        { 
            return View();
        }
        public IActionResult FunctionRoom()
        {

            return View();

        }
        public IActionResult DormRoom()
        {

            return View();

        }
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
                //RoomType = _Room.RoomType
            });
            return Json(result);

        }
        public ActionResult getDMRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms.AsQueryable().ToList();
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

            return RedirectToAction("FunctionRoom", "Room");
        }
    }
}
