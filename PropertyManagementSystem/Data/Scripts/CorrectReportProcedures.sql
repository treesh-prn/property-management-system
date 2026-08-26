SET XACT_ABORT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[GFMS_GenerateCompanyChargesReport]
    @StartDate DATETIME = NULL,
    @EndDate DATETIME = NULL,
    @PreparedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH BookingLines AS
    (
        SELECT
            H.ReservationId,
            H.ChargeToCompany,
            H.Department,
            D.FacilityId,
            D.RoomId,
            D.StartDate,
            D.EndDate,
            MAX(R.RoomName) AS RoomName,
            CAST(MAX(COALESCE(R.CostPerDay, 0)) AS DECIMAL(18, 2)) AS CostPerDay,
            CASE
                WHEN DATEDIFF(DAY, D.StartDate, D.EndDate) < 0 THEN 0
                ELSE DATEDIFF(DAY, D.StartDate, D.EndDate) + 1
            END AS NoOfDays
        FROM dbo.GFMS_ReservationHeader H
        INNER JOIN dbo.GFMS_ReservationDetails D ON D.ReservationId = H.ReservationId
        INNER JOIN dbo.GFMS_Rooms R ON R.RoomId = D.RoomId
        WHERE UPPER(LTRIM(RTRIM(H.Status))) = 'APPROVED'
          AND D.StartDate IS NOT NULL
          AND D.EndDate IS NOT NULL
          AND (@StartDate IS NULL OR D.StartDate >= CONVERT(DATE, @StartDate))
          AND (@EndDate IS NULL OR D.EndDate < DATEADD(DAY, 1, CONVERT(DATE, @EndDate)))
        GROUP BY
            H.ReservationId, H.ChargeToCompany, H.Department,
            D.FacilityId, D.RoomId, D.StartDate, D.EndDate
    ),
    EquipmentItems AS
    (
        SELECT
            H.ReservationId,
            D.FacilityId,
            D.RoomId,
            D.StartDate,
            D.EndDate,
            E.EquipmentId,
            MAX(COALESCE(NULLIF(E.EquipmentNameSnapshot, ''), EQ.EquipmentName, 'Equipment')) AS EquipmentName,
            CASE
                WHEN MAX(CASE WHEN E.IsAutomatic = 1 THEN 1 ELSE 0 END) = 1
                    THEN MAX(COALESCE(E.Quantity, 1))
                ELSE SUM(COALESCE(E.Quantity, 1))
            END AS Quantity,
            CASE
                WHEN MAX(CASE WHEN E.IsAutomatic = 1 THEN 1 ELSE 0 END) = 1
                    THEN MAX(COALESCE(E.AssignedCost, 0) * COALESCE(E.Quantity, 1))
                ELSE SUM(COALESCE(E.AssignedCost, 0) * COALESCE(E.Quantity, 1))
            END AS EquipmentCharge
        FROM dbo.GFMS_ReservationHeader H
        INNER JOIN dbo.GFMS_ReservationDetails D ON D.ReservationId = H.ReservationId
        INNER JOIN dbo.GFMS_ReservationEquipments E ON E.ReservationDetailId = D.ReservationDetailId
        LEFT JOIN dbo.GFMS_Equipments EQ ON EQ.Id = E.EquipmentId
        WHERE UPPER(LTRIM(RTRIM(H.Status))) = 'APPROVED'
          AND UPPER(LTRIM(RTRIM(COALESCE(E.AllocationStatus, 'RESERVED')))) <> 'RELEASED'
          AND D.StartDate IS NOT NULL
          AND D.EndDate IS NOT NULL
          AND (@StartDate IS NULL OR D.StartDate >= CONVERT(DATE, @StartDate))
          AND (@EndDate IS NULL OR D.EndDate < DATEADD(DAY, 1, CONVERT(DATE, @EndDate)))
        GROUP BY
            H.ReservationId, D.FacilityId, D.RoomId, D.StartDate, D.EndDate, E.EquipmentId
    ),
    EquipmentByLine AS
    (
        SELECT
            ReservationId, FacilityId, RoomId, StartDate, EndDate,
            STRING_AGG(CONCAT(EquipmentName, ' x', Quantity), ', ') AS EquipmentUsed,
            SUM(EquipmentCharge) AS EquipmentCharge
        FROM EquipmentItems
        GROUP BY ReservationId, FacilityId, RoomId, StartDate, EndDate
    )
    SELECT
        COALESCE(C.CompanyCode, 'N/A') AS CompanyCode,
        COALESCE(DEP.DepartmentCode, 'N/A') AS Department,
        B.RoomName AS RoomUsed,
        CONCAT(CONVERT(NVARCHAR(10), B.StartDate, 101), ' - ', CONVERT(NVARCHAR(10), B.EndDate, 101)) AS Dates,
        B.NoOfDays,
        COALESCE(E.EquipmentUsed, '') AS EquipmentUsed,
        CAST(CASE
            WHEN DATEDIFF(MINUTE, B.StartDate, B.EndDate) <= 240 THEN B.CostPerDay / 2
            ELSE B.CostPerDay * B.NoOfDays
        END AS DECIMAL(18, 2)) AS RoomCharge,
        CAST(COALESCE(E.EquipmentCharge, 0) AS DECIMAL(18, 2)) AS EquipmentCharge,
        CAST(
            CASE
                WHEN DATEDIFF(MINUTE, B.StartDate, B.EndDate) <= 240 THEN B.CostPerDay / 2
                ELSE B.CostPerDay * B.NoOfDays
            END + COALESCE(E.EquipmentCharge, 0)
            AS DECIMAL(18, 2)) AS TotalCharge,
        @PreparedBy AS PreparedBy,
        'MRDR' AS NotedBy
    FROM BookingLines B
    LEFT JOIN EquipmentByLine E
        ON E.ReservationId = B.ReservationId
       AND ISNULL(E.FacilityId, -1) = ISNULL(B.FacilityId, -1)
       AND ISNULL(E.RoomId, -1) = ISNULL(B.RoomId, -1)
       AND E.StartDate = B.StartDate
       AND E.EndDate = B.EndDate
    LEFT JOIN dbo.Gen_Company C ON C.Id = B.ChargeToCompany
    LEFT JOIN dbo.Gen_Department DEP ON DEP.Id = B.Department
    ORDER BY C.CompanyCode, DEP.DepartmentCode, B.StartDate, B.RoomName;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[GFMS_GetReservationOccupancy]
    @StartDate DATE,
    @EndDate DATE,
    @ReservationType NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        RH.ReservationCode,
        RH.Requestor,
        CASE
            WHEN RH.ReservationType = 'GH' THEN COALESCE(NULLIF(LTRIM(RTRIM(RD.GuestName)), ''), RH.Requestor)
            ELSE RH.Requestor
        END AS Occupant,
        RD.StartDate,
        RD.EndDate,
        CASE RH.ReservationType
            WHEN 'FN' THEN 'Function Room'
            WHEN 'GH' THEN 'Guesthouse'
            WHEN 'DRM' THEN 'Dorm and Housing'
            ELSE RH.ReservationType
        END AS ReservationType,
        F.FacilityName,
        R.RoomName,
        CONCAT(CONVERT(VARCHAR(10), RD.StartDate, 101), ' - ', CONVERT(VARCHAR(10), RD.EndDate, 101)) AS DateOfStay,
        CASE
            WHEN DATEDIFF(DAY, RD.StartDate, RD.EndDate) < 0 THEN 0
            ELSE DATEDIFF(DAY, RD.StartDate, RD.EndDate) + 1
        END AS NoOfDays
    FROM dbo.GFMS_ReservationDetails RD
    INNER JOIN dbo.GFMS_ReservationHeader RH ON RH.ReservationId = RD.ReservationId
    INNER JOIN dbo.GFMS_Facility F ON F.FacilityId = RD.FacilityId
    INNER JOIN dbo.GFMS_Rooms R ON R.RoomId = RD.RoomId
    WHERE RD.StartDate IS NOT NULL
      AND RD.EndDate IS NOT NULL
      AND RD.StartDate < DATEADD(DAY, 1, @EndDate)
      AND RD.EndDate >= @StartDate
      AND UPPER(LTRIM(RTRIM(RH.ReservationType))) = UPPER(LTRIM(RTRIM(@ReservationType)))
      AND UPPER(LTRIM(RTRIM(RH.Status))) = 'APPROVED'
    ORDER BY RD.StartDate, RD.EndDate, F.FacilityName, R.RoomName, Occupant;
END;
GO
