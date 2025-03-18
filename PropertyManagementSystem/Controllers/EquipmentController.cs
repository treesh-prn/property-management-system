using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using PropertyManagementSystem.Attributes;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class EquipmentController : Controller
    {
        private readonly DAL _context;

        public EquipmentController(DAL context)
        {
            _context = context;
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetEquipmentList()
        {
            var linkedEquipmentIds = _context.RoomEquipments // Assuming this is the linking table
                .Select(re => re.EquipmentId)
                .Distinct()
                .ToList();

            var equipments = _context.Equipments
                .Where(e => e.isActive == true && e.isFacilityEquipment == true && !linkedEquipmentIds.Contains(e.Id))
                .Select(e => new { e.EquipmentName, e.Id })
                .ToList();

            return Json(equipments);
        }

        public ActionResult getEquipments([DataSourceRequest] DataSourceRequest request)
        {
            List <GFMS_Equipments> _Equipments = _context.Equipments.AsQueryable().ToList();
            DataSourceResult result = _Equipments.ToDataSourceResult(request, _Equipments => new
            {
                Id = _Equipments.Id,
                EquipmentName = _Equipments.EquipmentName,
                EquipmentQty = _Equipments.EquipmentQty,
                PurchaseCost = _Equipments.PurchaseCost,
                WholeDayCost = _Equipments.WholeDayCost,
                HalfDayCost = _Equipments.HalfDayCost,
                Remarks = _Equipments.Remarks,
                isActive = _Equipments.isActive,
            });
            return Json(result);
        }
        public ActionResult newEquipments(string _name, int _quantity, double _purCost, double _halfCost, double _wholeCost, string _remarks, bool _isActive, int _facilityId, int _roomId, bool _isFacilityEquipment)
        {
            GFMS_Equipments equipments = new GFMS_Equipments()
            {
                EquipmentName = _name.ToUpper(),
                EquipmentQty = _quantity,
                PurchaseCost = _purCost,
                HalfDayCost = _halfCost,
                WholeDayCost = _wholeCost,
                Remarks = _remarks?.ToUpper(),
                isActive = _isActive,
                FacilityId = _facilityId,
                RoomId = _roomId,
                isFacilityEquipment = _isFacilityEquipment


            };
            _context.Equipments.Add(equipments);
            _context.SaveChanges();
            return RedirectToAction("Index", "Equipment");
        }

        public IActionResult ViewEquipment(int Id)
        {
            var equipmentDetail = _context.Equipments.FirstOrDefault(e => e.Id == Id);
            var facilityDetail = _context.Facilities.FirstOrDefault(f => f.FacilityId == equipmentDetail.FacilityId);
            var roomDetail = _context.Rooms.FirstOrDefault(r => r.RoomId == equipmentDetail.RoomId);

            if (equipmentDetail == null)
            {
                return NotFound();
            }

            var equipmentViewModel = new EquipmentViewModel
            {
                Id = equipmentDetail.Id,
                EquipmentName = equipmentDetail.EquipmentName,
                EquipmentQty = equipmentDetail.EquipmentQty,
                PurchaseCost = equipmentDetail.PurchaseCost,
                WholeDayCost = equipmentDetail.WholeDayCost,
                HalfDayCost = equipmentDetail.HalfDayCost,
                Remarks = equipmentDetail.Remarks,
                isActive = equipmentDetail.isActive,
                FacilityId = equipmentDetail.FacilityId,
                RoomId = equipmentDetail.RoomId,
                isFacilityEquipment = equipmentDetail.isFacilityEquipment,
                FacilityName = facilityDetail?.FacilityName,
                RoomName = roomDetail?.RoomName
            };

            return View(equipmentViewModel);
        }

        /*Update*/
        public IActionResult EditEquipment(int Id, string _name, int _quantity, double _purCost, double _halfCost, double _wholeCost, string _remarks, bool _isActive, bool _isFacilityEquipment)
        {
            var equipment = _context.Equipments.FirstOrDefault(e => e.Id == Id);
            if (equipment == null)
            {
                return NotFound("Equipment not found");
            }

            equipment.EquipmentName = _name.ToUpper();
            equipment.EquipmentQty = _quantity;
            equipment.PurchaseCost = _purCost;
            equipment.HalfDayCost = _halfCost;
            equipment.WholeDayCost = _wholeCost;
            equipment.Remarks = _remarks?.ToUpper();
            equipment.isActive = _isActive;
            equipment.isFacilityEquipment = _isFacilityEquipment;
            try
            {
                _context.Equipments.Update(equipment);
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
            string redirectUrl = Url.Action("ViewEquipment", "Equipment", new { Id = Id });
            return Redirect(redirectUrl);
        }
        /*Delete*/

        [HttpPost]
        public IActionResult DeleteEquipment(int Id)
        {
            GFMS_Equipments equipments= _context.Equipments.Find(Id);
            if (equipments == null)
            {
                return NotFound();
            }
            _context.Equipments.Remove(equipments);
            _context.SaveChanges();
            return RedirectToAction("Index", "Equipment");
        }
    }
}