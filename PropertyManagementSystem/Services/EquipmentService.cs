using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public class EquipmentService
    {
        private readonly DAL _context;
        private readonly IFacilityEquipmentLinkService _equipmentLinks;

        public EquipmentService(DAL context, IFacilityEquipmentLinkService equipmentLinks)
        {
            _context = context;
            _equipmentLinks = equipmentLinks;
        }

        public DataSourceResult GetEquipments(DataSourceRequest request)
        {
            return _context.Equipments
                .AsNoTracking()
                .ToDataSourceResult(request, equipment => new
                {
                    equipment.Id,
                    equipment.EquipmentName,
                    equipment.EquipmentQty,
                    equipment.PurchaseCost,
                    equipment.WholeDayCost,
                    equipment.HalfDayCost,
                    equipment.Remarks,
                    equipment.isActive,
                });
        }

        public void CreateEquipment(EquipmentRequest request)
        {
            using var transaction = _context.Database.IsRelational()
                ? _context.Database.BeginTransaction()
                : null;
            var equipment = new GFMS_Equipments
            {
                EquipmentName = request._name,
                EquipmentQty = request._quantity,
                PurchaseCost = request._purCost,
                HalfDayCost = request._halfCost,
                WholeDayCost = request._wholeCost,
                Remarks = request._remarks,
                isActive = request._isActive,
                FacilityId = request._isFacilityEquipment ? request._facilityId : null,
                RoomId = request._isFacilityEquipment ? request._roomId : null,
                isFacilityEquipment = request._isFacilityEquipment
            };

            _context.Equipments.Add(equipment);
            _context.SaveChanges();
            _equipmentLinks.SyncLegacyEquipmentLink(
                equipment, request._linkType, request._defaultQuantity, request._isFixed);
            _context.SaveChanges();
            transaction?.Commit();
        }

        public EquipmentViewModel? GetEquipment(int id)
        {
            var equipment = _context.Equipments.AsNoTracking()
                .FirstOrDefault(item => item.Id == id);
            if (equipment == null) return null;

            // Keep the location queries simple. EF Core cannot translate an ordered
            // DefaultIfEmpty() over a grouped join on every supported provider.
            var links = _context.FacilityEquipmentLinks.AsNoTracking()
                .Where(link => link.EquipmentId == id && link.IsActive)
                .OrderByDescending(link => link.RoomId.HasValue)
                .ThenBy(link => link.FacilityEquipmentLinkId)
                .ToList();
            var primaryLink = links.FirstOrDefault();
            var facilityId = equipment.FacilityId ?? primaryLink?.FacilityId;
            var roomId = equipment.RoomId ?? primaryLink?.RoomId;

            var facilityIds = links.Select(link => (int?)link.FacilityId)
                .Append(facilityId)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .ToList();
            var roomIds = links.Select(link => link.RoomId)
                .Append(roomId)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .ToList();
            var facilityNames = _context.Facilities.AsNoTracking()
                .Where(facility => facilityIds.Contains(facility.FacilityId))
                .ToDictionary(facility => facility.FacilityId, facility => facility.FacilityName);
            var roomNames = _context.Rooms.AsNoTracking()
                .Where(room => roomIds.Contains(room.RoomId))
                .ToDictionary(room => room.RoomId, room => room.RoomName);

            var result = new EquipmentViewModel
            {
                Id = equipment.Id,
                EquipmentName = equipment.EquipmentName,
                EquipmentQty = equipment.EquipmentQty,
                PurchaseCost = equipment.PurchaseCost,
                WholeDayCost = equipment.WholeDayCost,
                HalfDayCost = equipment.HalfDayCost,
                Remarks = equipment.Remarks,
                isActive = equipment.isActive,
                FacilityId = facilityId,
                RoomId = roomId,
                isFacilityEquipment = equipment.isFacilityEquipment == true || primaryLink != null,
                FacilityName = facilityId.HasValue && facilityNames.TryGetValue(facilityId.Value, out var facilityName)
                    ? facilityName
                    : null,
                RoomName = roomId.HasValue && roomNames.TryGetValue(roomId.Value, out var roomName)
                    ? roomName
                    : null,
                LinkType = primaryLink?.LinkType ?? EquipmentLinkTypes.Included,
                DefaultQuantity = primaryLink?.DefaultQuantity ?? 1,
                IsFixed = primaryLink?.IsFixed ?? false,
                LocationLinks = links
                    .Select(link => new EquipmentLocationLinkViewModel
                    {
                        FacilityId = link.FacilityId,
                        RoomId = link.RoomId,
                        FacilityName = facilityNames.TryGetValue(link.FacilityId, out var linkedFacilityName)
                            ? linkedFacilityName ?? "Facility"
                            : "Facility",
                        RoomName = link.RoomId.HasValue && roomNames.TryGetValue(link.RoomId.Value, out var linkedRoomName)
                            ? linkedRoomName
                            : null,
                        LinkType = link.LinkType,
                        DefaultQuantity = link.DefaultQuantity,
                        IsFixed = link.IsFixed
                    })
                    .OrderBy(link => link.FacilityName)
                    .ThenBy(link => link.RoomName)
                    .ToList()
            };
            return result;
        }

        public bool UpdateEquipment(EditEquipmentRequest request)
        {
            var equipment = _context.Equipments.FirstOrDefault(e => e.Id == request.Id);
            if (equipment == null)
            {
                return false;
            }

            equipment.EquipmentName = request._name;
            equipment.EquipmentQty = request._quantity;
            equipment.PurchaseCost = request._purCost;
            equipment.HalfDayCost = request._halfCost;
            equipment.WholeDayCost = request._wholeCost;
            equipment.Remarks = request._remarks;
            equipment.isActive = request._isActive;
            if (!request._preserveLocationLinks)
            {
                equipment.FacilityId = request._isFacilityEquipment ? request._facilityId : null;
                equipment.RoomId = request._isFacilityEquipment ? request._roomId : null;
                equipment.isFacilityEquipment = request._isFacilityEquipment;
                _equipmentLinks.SyncLegacyEquipmentLink(
                    equipment, request._linkType, request._defaultQuantity, request._isFixed);
            }
            _context.SaveChanges();
            return true;
        }

        public bool DeleteEquipment(int id)
        {
            var equipment = _context.Equipments.Find(id);
            if (equipment == null)
            {
                return false;
            }

            _context.Equipments.Remove(equipment);
            _context.SaveChanges();
            return true;
        }

        public string? ValidateLocation(EquipmentRequest request)
        {
            if (request._preserveLocationLinks) return null;
            if (!request._isFacilityEquipment) return null;
            if (!request._facilityId.HasValue || !_context.Facilities.AsNoTracking().Any(facility =>
                    facility.FacilityId == request._facilityId.Value &&
                    facility.isActive == true &&
                    facility.FacilityClassification == "FUNCTION ROOM"))
                return "Select an active Function Room facility for this equipment.";
            if (request._roomId.HasValue && !_context.Rooms.AsNoTracking().Any(room =>
                    room.RoomId == request._roomId.Value &&
                    room.FacilityId == request._facilityId.Value &&
                    room.RoomType == ReservationRoomTypes.FunctionRoom &&
                    room.isActive))
                return "The selected Function Room does not belong to the selected facility or is inactive.";
            return null;
        }
    }
}
