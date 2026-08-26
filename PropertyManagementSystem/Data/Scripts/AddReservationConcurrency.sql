SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.GFMS_ReservationHeader', 'RowVersion') IS NULL
    ALTER TABLE dbo.GFMS_ReservationHeader ADD RowVersion rowversion NOT NULL;

IF COL_LENGTH('dbo.GFMS_ReservationDetails', 'RowVersion') IS NULL
    ALTER TABLE dbo.GFMS_ReservationDetails ADD RowVersion rowversion NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.GFMS_ReservationHeader')
      AND name = 'UX_GFMS_ReservationHeader_ReservationCode')
    CREATE UNIQUE INDEX UX_GFMS_ReservationHeader_ReservationCode
        ON dbo.GFMS_ReservationHeader(ReservationCode)
        WHERE ReservationCode IS NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.GFMS_ReservationDetails')
      AND name = 'IX_GFMS_ReservationDetails_Room_Slot')
    CREATE INDEX IX_GFMS_ReservationDetails_Room_Slot
        ON dbo.GFMS_ReservationDetails(RoomId, StartDate, EndDate)
        INCLUDE (ReservationId, ReservationDetailId, NumberOfGuest, FacilityId);

IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL
    CREATE TABLE dbo.__EFMigrationsHistory
    (
        MigrationId nvarchar(150) NOT NULL,
        ProductVersion nvarchar(32) NOT NULL,
        CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId)
    );

IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = '20260801070000_AddReservationConcurrency')
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES ('20260801070000_AddReservationConcurrency', '6.0.29');

COMMIT TRANSACTION;
