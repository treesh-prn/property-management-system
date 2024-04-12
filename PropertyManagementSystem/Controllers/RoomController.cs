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

        public IActionResult GuestRoom()
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
            List<GFMS_Rooms> _Rooms = _context.Rooms.AsQueryable().Where(x => x.RoomType == 0).ToList();
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
        public ActionResult getFNRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms.AsQueryable().Where(x => x.RoomType == 1).ToList();
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
        public ActionResult getDMRooms([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Rooms> _Rooms = _context.Rooms.AsQueryable().Where(x => x.RoomType == 2).ToList();
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
        public IActionResult newGHRoom(string _name,int _facility,  int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
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
        public IActionResult newFNRoom(string _name, int _facility, int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
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
        public IActionResult newDMRoom(string _name, int _facility, int _capacity, bool _isActive)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name,
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
