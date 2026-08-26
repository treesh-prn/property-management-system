using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml.Linq;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class ReportRequestTests
{
    [Fact]
    public void CompanyChargesPdf_UsesStoredProcedureRowsAndReportMetadata()
    {
        var rows = new[]
        {
            new ReservationReport
            {
                CompanyCode = "TECO",
                Department = "ADMIN",
                RoomUsed = "FUNCTION ROOM 1",
                Dates = "Aug 20, 2026",
                NoOfDays = 1,
                EquipmentUsed = "PROJECTOR",
                RoomCharge = 1000m,
                EquipmentCharge = 250m,
                TotalCharge = 1250m
            }
        };

        var pdf = new CompanyChargesPdfService().Generate(
            rows,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 21),
            "TLP");
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("Company and Department Charges", text);
        Assert.Contains("FUNCTION ROOM 1", text);
        Assert.Contains("PHP 1,250.00", text);
        Assert.EndsWith("%%EOF", text);
    }

    [Fact]
    public void CompanyChargesPdf_PaginatesLargeStoredProcedureResults()
    {
        var rows = Enumerable.Range(1, 60)
            .Select(index => new ReservationReport
            {
                CompanyCode = $"COMP-{index:000}",
                Department = "PROPERTY MANAGEMENT",
                RoomUsed = "ADMIN FUNCTION ROOM",
                Dates = "Aug 20, 2026",
                NoOfDays = 1,
                EquipmentUsed = "PROJECTOR AND FLATSCREEN TV",
                RoomCharge = 1000m,
                EquipmentCharge = 250m,
                TotalCharge = 1250m
            })
            .ToList();

        var pdf = new CompanyChargesPdfService().Generate(
            rows,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            "LORELEI CUNANAN");
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Page 1 of ", text);
        Assert.Contains("COMP-060", text);
        Assert.True(CountOccurrences(text, "/Type /Page ") > 1);
    }

    [Fact]
    public void OccupancyPdf_UsesStoredProcedureRowsAndReportMetadata()
    {
        var rows = new[]
        {
            new OccupancyReportRow
            {
                ReservationCode = "RSQ-GH-2026-0010",
                Occupant = "JAY KAMOTE",
                FacilityName = "APARTMENT",
                RoomName = "APARTMENT C",
                StartDate = new DateTime(2026, 8, 17, 13, 0, 0),
                EndDate = new DateTime(2026, 8, 18, 20, 0, 0),
                NoOfDays = 2
            }
        };

        var pdf = new OccupancyPdfService().Generate(
            rows,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            "GH");
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("Reservation Occupancy Report", text);
        Assert.Contains("RSQ-GH-2026-0010", text);
        Assert.Contains("JAY KAMOTE", text);
        Assert.Contains("Guesthouse", text);
        Assert.EndsWith("%%EOF", text);
    }

    [Fact]
    public void ChargesExcel_IsValidOpenXmlAndContainsTotals()
    {
        var rows = new[]
        {
            new ReservationReport
            {
                CompanyCode = "TECO",
                Department = "ICT",
                RoomUsed = "FUNCTION ROOM 1",
                Dates = "08/20/2026 - 08/20/2026",
                NoOfDays = 1,
                EquipmentUsed = "PROJECTOR x1",
                RoomCharge = 1000m,
                EquipmentCharge = 250m,
                TotalCharge = 1250m
            }
        };

        var workbook = new ExcelReportService().GenerateCharges(
            rows, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), "TLP");

        using var archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
        var sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("Company and Department Charges", sheet);
        Assert.Contains("FUNCTION ROOM 1", sheet);
        Assert.Contains("PROJECTOR x1", sheet);
        Assert.Contains("1250", sheet);
        Assert.True(
            sheet.IndexOf("<autoFilter", StringComparison.Ordinal) < sheet.IndexOf("<mergeCells", StringComparison.Ordinal),
            "SpreadsheetML requires autoFilter before mergeCells in worksheet element order.");
        AssertXmlEntriesAreWellFormed(archive);
    }

    [Fact]
    public void OccupancyExcel_ContainsReservationAndOccupant()
    {
        var rows = new[]
        {
            new OccupancyReportRow
            {
                ReservationCode = "RSQ-GH-2026-0010",
                Requestor = "TRISHA PARAN",
                Occupant = "JAY KAMOTE",
                ReservationType = "Guesthouse",
                FacilityName = "APARTMENT",
                RoomName = "APARTMENT C",
                StartDate = new DateTime(2026, 8, 17, 13, 0, 0),
                EndDate = new DateTime(2026, 8, 18, 20, 0, 0),
                NoOfDays = 2
            }
        };

        var workbook = new ExcelReportService().GenerateOccupancy(
            rows, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), "GH");
        using var archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        var sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");

        Assert.Contains("RSQ-GH-2026-0010", sheet);
        Assert.Contains("JAY KAMOTE", sheet);
        Assert.Contains("APARTMENT C", sheet);
        AssertXmlEntriesAreWellFormed(archive);
    }

    [Fact]
    public void ReportPeriod_RejectsEndDateBeforeStartDate()
    {
        var request = ValidCompanyRequest();
        request.EndDate = request.StartDate!.Value.AddDays(-1);

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(request.EndDate)) &&
            error.ErrorMessage!.Contains("on or after", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Occupancy_RejectsRangesLongerThanThirtyOneDays()
    {
        var request = new OccupancyReportRequest
        {
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 2, 1),
            ReservationType = "FN"
        };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(request.EndDate)) &&
            error.ErrorMessage!.Contains("31 days", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanyCharges_RequiresPreparedBy()
    {
        var request = ValidCompanyRequest();
        request.PreparedBy = string.Empty;

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.PreparedBy)));
    }

    [Fact]
    public void ValidReportRequests_HaveNoValidationErrors()
    {
        var companyErrors = Validate(ValidCompanyRequest());
        var occupancyErrors = Validate(new OccupancyReportRequest
        {
            StartDate = new DateTime(2026, 8, 1),
            EndDate = new DateTime(2026, 8, 31),
            ReservationType = "DRM"
        });

        Assert.Empty(companyErrors);
        Assert.Empty(occupancyErrors);
    }

    private static CompanyChargesReportRequest ValidCompanyRequest() => new()
    {
        StartDate = new DateTime(2026, 8, 1),
        EndDate = new DateTime(2026, 8, 21),
        PreparedBy = "TLP"
    };

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    private static void AssertXmlEntriesAreWellFormed(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            Assert.NotNull(XDocument.Load(stream));
        }
    }
}
