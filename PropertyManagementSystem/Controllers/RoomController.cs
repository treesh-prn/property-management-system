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
                                   CostPerDay = room.CostPerDay,
                                   CostPerWeek = room.CostPerWeek,
                                   CostPerMonth = room.CostPerMonth,
                                   isActive = room.isActive,
                                   RoomType = room.RoomType
                               }).FirstOrDefault();

            if (roomDetails == null)
            {
                return NotFound();
            }

            // Get the list of equipment for this room
            var equipmentList = (from re in _context.RoomEquipments
                                 join eq in _context.Equipments on re.EquipmentId equals eq.Id
                                 where re.RoomId == RoomId
                                 select new { eq.Id, eq.EquipmentName }).ToList();

            Console.WriteLine($"RoomId: {roomDetails.RoomId}, RoomName: {roomDetails.RoomName}");
            Console.WriteLine($"Equipment Count: {equipmentList.Count}");
            foreach (var eq in equipmentList)
            {
                Console.WriteLine($"Equipment: {eq}");
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
                RoomType = roomDetails.RoomType,
                CostPerDay = roomDetails.CostPerDay,
                CostPerMonth = roomDetails.CostPerMonth,
                CostPerWeek = roomDetails.CostPerWeek,
                EquipmentNames = equipmentList.Select(e => e.EquipmentName).ToList(),
                SelectedEquipmentIds = equipmentList.Select(e => e.Id).ToList(),
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
        public IActionResult newGHRoom(string _name, string _status, int _facility,  int _capacity, bool _isActive, int _costPerDay, int _costPerWeek, int _costPerMonth)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name.ToUpper(),
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                CostPerDay = _costPerDay,
                CostPerWeek = _costPerWeek,
                CostPerMonth = _costPerMonth,
                RoomType = 0
            };


            _context.Rooms.Add(_room);
            _context.SaveChanges();

            return RedirectToAction("GuestRoom", "Room");
        }
        [HttpPost]
        public IActionResult newFNRoom(string _name, string _status, int _facility, int _capacity, bool _isActive, int _costPerDay, int _costPerWeek, int _costPerMonth, List<int> SelectedEquipments)
        {
            // Create new room entity
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name.ToUpper(),
                RoomStatus = _status,
                Capacity = _capacity,
                FacilityId = _facility,
                CostPerDay = _costPerDay,
                CostPerWeek = _costPerWeek,
                CostPerMonth = _costPerMonth,
                isActive = _isActive,
                RoomType = 1
            };

            // Add room to the database
            _context.Rooms.Add(_room);
            _context.SaveChanges();

            // Save selected equipment if any
            if (SelectedEquipments != null && SelectedEquipments.Any())
            {
                foreach (var equipmentId in SelectedEquipments)
                {
                    GFMS_RoomEquipment roomEquipment = new GFMS_RoomEquipment()
                    {
                        RoomId = _room.RoomId,
                        EquipmentId = equipmentId
                    };
                    _context.RoomEquipments.Add(roomEquipment);
                }
                _context.SaveChanges();
            }

            return RedirectToAction("FunctionRoom", "Room");
        }
        [HttpPost]
        public IActionResult newDMRoom(string _name, string _status, int _facility, int _capacity, bool _isActive, int _costPerDay, int _costPerWeek, int _costPerMonth)
        {
            GFMS_Rooms _room = new GFMS_Rooms()
            {
                RoomName = _name.ToUpper(),
                RoomStatus= _status,    
                Capacity = _capacity,
                FacilityId = _facility,
                isActive = _isActive,
                CostPerDay = _costPerDay,
                CostPerWeek = _costPerWeek,
                CostPerMonth = _costPerMonth,
                RoomType = 2
            };


            _context.Rooms.Add(_room);
            _context.SaveChanges();

            return RedirectToAction("DormRoom", "Room");
        }

        /*Update*/

        [HttpPost]
        public IActionResult EditGHRoom(GFMS_Rooms room, int _roomId, string _roomName, int _facility, string _roomStatus, int _capacity, bool _isActive, double _costPerDay, double _costPerWeek, double _costPerMonth, List<int> selectedEquipmentIds)
        {
            var ghRoom = _context.Rooms.FirstOrDefault(gh => gh.RoomId == _roomId);
            if (ghRoom == null)
            {
                return NotFound("Room not found.");
            }

            // Update room details
            ghRoom.RoomName = _roomName.ToUpper();
            ghRoom.FacilityId = _facility;
            ghRoom.RoomStatus = _roomStatus;
            ghRoom.Capacity = _capacity;
            ghRoom.isActive = _isActive;
            ghRoom.CostPerDay = _costPerDay;
            ghRoom.CostPerWeek = _costPerWeek;
            ghRoom.CostPerMonth = _costPerMonth;

            try
            {
                // Remove existing room equipment mappings
                var existingEquipment = _context.RoomEquipments.Where(re => re.RoomId == _roomId).ToList();
                _context.RoomEquipments.RemoveRange(existingEquipment);

                // Add new equipment selections
                if (selectedEquipmentIds != null && selectedEquipmentIds.Any())
                {
                    var newEquipmentMappings = selectedEquipmentIds.Select(eqId => new GFMS_RoomEquipment
                    {
                        RoomId = _roomId,
                        EquipmentId = eqId
                    }).ToList();

                    _context.RoomEquipments.AddRange(newEquipmentMappings);
                }

                _context.Rooms.Update(ghRoom);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while updating: {ex.Message}");
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
