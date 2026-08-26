-- Some legacy databases use identity columns without declaring them as keys.
-- Add candidate keys only when no equivalent unique index already exists.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.GFMS_Equipments')
      AND i.is_unique = 1
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND ic.column_id = COLUMNPROPERTY(i.object_id, N'Id', 'ColumnId'))
)
    CREATE UNIQUE INDEX UX_GFMS_Equipments_Id ON dbo.GFMS_Equipments(Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.GFMS_ReservationDetails')
      AND i.is_unique = 1
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND ic.column_id = COLUMNPROPERTY(i.object_id, N'ReservationDetailId', 'ColumnId'))
)
    CREATE UNIQUE INDEX UX_GFMS_ReservationDetails_Id
        ON dbo.GFMS_ReservationDetails(ReservationDetailId);

IF OBJECT_ID('dbo.GFMS_FacilityEquipmentLinks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GFMS_FacilityEquipmentLinks
    (
        FacilityEquipmentLinkId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FacilityId int NOT NULL,
        EquipmentId int NOT NULL,
        RoomId int NULL,
        DefaultQuantity int NOT NULL CONSTRAINT DF_GFMS_FacilityEquipmentLinks_Quantity DEFAULT (1),
        LinkType nvarchar(20) NOT NULL CONSTRAINT DF_GFMS_FacilityEquipmentLinks_LinkType DEFAULT ('INCLUDED'),
        IsFixed bit NOT NULL CONSTRAINT DF_GFMS_FacilityEquipmentLinks_IsFixed DEFAULT (0),
        IsActive bit NOT NULL CONSTRAINT DF_GFMS_FacilityEquipmentLinks_IsActive DEFAULT (1),
        CONSTRAINT UX_GFMS_FacilityEquipmentLinks_LocationEquipment UNIQUE (FacilityId, EquipmentId, RoomId),
        CONSTRAINT CK_GFMS_FacilityEquipmentLinks_Quantity CHECK (DefaultQuantity > 0),
        CONSTRAINT CK_GFMS_FacilityEquipmentLinks_LinkType CHECK (LinkType IN ('INCLUDED', 'ADD_ON')),
        CONSTRAINT FK_GFMS_FacilityEquipmentLinks_Facility FOREIGN KEY (FacilityId)
            REFERENCES dbo.GFMS_Facility(FacilityId),
        CONSTRAINT FK_GFMS_FacilityEquipmentLinks_Equipment FOREIGN KEY (EquipmentId)
            REFERENCES dbo.GFMS_Equipments(Id),
        CONSTRAINT FK_GFMS_FacilityEquipmentLinks_Room FOREIGN KEY (RoomId)
            REFERENCES dbo.GFMS_Rooms(RoomId)
    );

    INSERT INTO dbo.GFMS_FacilityEquipmentLinks
        (FacilityId, EquipmentId, RoomId, DefaultQuantity, LinkType, IsFixed, IsActive)
    SELECT e.FacilityId, e.Id, e.RoomId,
           CASE WHEN ISNULL(e.EquipmentQty, 0) < 1 THEN 1 ELSE e.EquipmentQty END,
           'INCLUDED', CASE WHEN e.RoomId IS NULL THEN 0 ELSE 1 END, 1
    FROM dbo.GFMS_Equipments e
    WHERE e.FacilityId IS NOT NULL
      AND ISNULL(e.EquipmentQty, 0) > 0
      AND EXISTS (SELECT 1 FROM dbo.GFMS_Facility f WHERE f.FacilityId = e.FacilityId)
      AND (e.RoomId IS NULL OR EXISTS
           (SELECT 1 FROM dbo.GFMS_Rooms r
            WHERE r.RoomId = e.RoomId AND r.FacilityId = e.FacilityId));
END;

IF OBJECT_ID('dbo.GFMS_ReservationEquipments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GFMS_ReservationEquipments
    (
        ReservationEquipmentId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReservationDetailId int NOT NULL,
        EquipmentId int NOT NULL,
        Quantity int NOT NULL CONSTRAINT DF_GFMS_ReservationEquipments_Quantity DEFAULT (1),
        SourceType nvarchar(20) NOT NULL,
        SourceFacilityId int NULL,
        SourceRoomId int NULL,
        IsAutomatic bit NOT NULL,
        AllocationStatus nvarchar(20) NOT NULL CONSTRAINT DF_GFMS_ReservationEquipments_Status DEFAULT ('RESERVED'),
        AssignedCost decimal(18,2) NOT NULL CONSTRAINT DF_GFMS_ReservationEquipments_Cost DEFAULT (0),
        EquipmentNameSnapshot nvarchar(200) NOT NULL,
        DateAssigned datetime2 NOT NULL CONSTRAINT DF_GFMS_ReservationEquipments_Date DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UX_GFMS_ReservationEquipments_DetailEquipment UNIQUE (ReservationDetailId, EquipmentId),
        CONSTRAINT CK_GFMS_ReservationEquipments_Quantity CHECK (Quantity > 0),
        CONSTRAINT FK_GFMS_ReservationEquipments_Detail FOREIGN KEY (ReservationDetailId)
            REFERENCES dbo.GFMS_ReservationDetails(ReservationDetailId) ON DELETE CASCADE,
        CONSTRAINT FK_GFMS_ReservationEquipments_Equipment FOREIGN KEY (EquipmentId)
            REFERENCES dbo.GFMS_Equipments(Id)
    );
END;

-- Keep EF Core's migration history consistent when this fallback script is
-- used on an existing database that cannot run the dotnet-ef CLI.
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM dbo.__EFMigrationsHistory
       WHERE MigrationId = N'20260820090000_AddEquipmentAllocations'
   )
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260820090000_AddEquipmentAllocations', N'6.0.24');
END;
