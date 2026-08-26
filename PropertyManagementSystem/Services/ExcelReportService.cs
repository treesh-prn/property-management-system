using PropertyManagementSystem.Models;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace PropertyManagementSystem.Services
{
    public sealed class ExcelReportService
    {
        public byte[] GenerateCharges(
            IReadOnlyCollection<ReservationReport> rows,
            DateTime startDate,
            DateTime endDate,
            string preparedBy)
        {
            var headers = new[]
            {
                "Company", "Department", "Room", "Dates", "Days", "Equipment",
                "Room Charge", "Equipment Charge", "Total Charge"
            };
            var data = rows.Select(row => new ExcelCell[]
            {
                Text(row.CompanyCode), Text(row.Department), Text(row.RoomUsed), Text(row.Dates),
                Number(row.NoOfDays), Text(row.EquipmentUsed), Money(row.RoomCharge),
                Money(row.EquipmentCharge), Money(row.TotalCharge)
            }).ToList();
            var totals = new ExcelCell[]
            {
                Text("TOTAL", 3), Text(string.Empty), Text(string.Empty), Text(string.Empty), Text(string.Empty), Text(string.Empty),
                Money(rows.Sum(row => row.RoomCharge), 6),
                Money(rows.Sum(row => row.EquipmentCharge), 6),
                Money(rows.Sum(row => row.TotalCharge), 6)
            };

            return BuildWorkbook(
                "Company Charges",
                "Company and Department Charges",
                $"Reporting period: {startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy} | Prepared by: {preparedBy}",
                headers,
                data,
                totals,
                new[] { 16d, 18d, 24d, 24d, 10d, 34d, 17d, 19d, 17d });
        }

        public byte[] GenerateOccupancy(
            IReadOnlyCollection<OccupancyReportRow> rows,
            DateTime startDate,
            DateTime endDate,
            string reservationType)
        {
            var headers = new[]
            {
                "Reservation", "Requestor", "Occupant", "Reservation Type", "Facility", "Room",
                "Arrival", "Departure", "Days"
            };
            var data = rows.Select(row => new ExcelCell[]
            {
                Text(row.ReservationCode), Text(row.Requestor), Text(row.Occupant), Text(row.ReservationType),
                Text(row.FacilityName), Text(row.RoomName), Text(row.StartDate.ToString("MMM dd, yyyy h:mm tt", CultureInfo.InvariantCulture)),
                Text(row.EndDate.ToString("MMM dd, yyyy h:mm tt", CultureInfo.InvariantCulture)), Number(row.NoOfDays)
            }).ToList();

            return BuildWorkbook(
                "Occupancy",
                "Reservation Occupancy Report",
                $"{ReservationTypeName(reservationType)} | {startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                headers,
                data,
                null,
                new[] { 23d, 24d, 24d, 20d, 24d, 22d, 24d, 24d, 10d });
        }

        private static byte[] BuildWorkbook(
            string sheetName,
            string title,
            string subtitle,
            IReadOnlyList<string> headers,
            IReadOnlyList<ExcelCell[]> rows,
            ExcelCell[]? totals,
            IReadOnlyList<double> widths)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(archive, "[Content_Types].xml", ContentTypes);
                Add(archive, "_rels/.rels", RootRelationships);
                Add(archive, "xl/workbook.xml", Workbook(sheetName));
                Add(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                Add(archive, "xl/styles.xml", Styles);
                Add(archive, "xl/worksheets/sheet1.xml", Worksheet(title, subtitle, headers, rows, totals, widths));
            }
            return output.ToArray();
        }

        private static string Worksheet(
            string title,
            string subtitle,
            IReadOnlyList<string> headers,
            IReadOnlyList<ExcelCell[]> rows,
            ExcelCell[]? totals,
            IReadOnlyList<double> widths)
        {
            var lastColumn = ColumnName(headers.Count);
            var lastDataRow = 4 + rows.Count;
            var finalRow = lastDataRow + (totals is null ? 0 : 1);
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            xml.Append($"<dimension ref=\"A1:{lastColumn}{Math.Max(4, finalRow)}\"/>");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"4\" topLeftCell=\"A5\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            xml.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
            xml.Append("<cols>");
            for (var i = 0; i < widths.Count; i++)
                xml.Append(CultureInfo.InvariantCulture, $"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{widths[i]:0.##}\" customWidth=\"1\"/>");
            xml.Append("</cols><sheetData>");
            xml.Append($"<row r=\"1\" ht=\"28\" customHeight=\"1\">{InlineCell("A1", title, 1)}</row>");
            xml.Append($"<row r=\"2\" ht=\"22\" customHeight=\"1\">{InlineCell("A2", subtitle, 2)}</row>");
            xml.Append("<row r=\"3\"/>");
            xml.Append("<row r=\"4\" ht=\"24\" customHeight=\"1\">");
            for (var column = 0; column < headers.Count; column++)
                xml.Append(InlineCell($"{ColumnName(column + 1)}4", headers[column], 3));
            xml.Append("</row>");

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var excelRow = rowIndex + 5;
                xml.Append($"<row r=\"{excelRow}\">");
                for (var column = 0; column < rows[rowIndex].Length; column++)
                    xml.Append(Cell($"{ColumnName(column + 1)}{excelRow}", rows[rowIndex][column]));
                xml.Append("</row>");
            }

            if (totals is not null)
            {
                var totalRow = lastDataRow + 1;
                xml.Append($"<row r=\"{totalRow}\" ht=\"22\" customHeight=\"1\">");
                for (var column = 0; column < totals.Length; column++)
                    xml.Append(Cell($"{ColumnName(column + 1)}{totalRow}", totals[column]));
                xml.Append("</row>");
            }
            xml.Append("</sheetData>");
            if (rows.Count > 0) xml.Append($"<autoFilter ref=\"A4:{lastColumn}{lastDataRow}\"/>");
            xml.Append($"<mergeCells count=\"2\"><mergeCell ref=\"A1:{lastColumn}1\"/><mergeCell ref=\"A2:{lastColumn}2\"/></mergeCells>");
            xml.Append("<pageMargins left=\"0.35\" right=\"0.35\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/>");
            xml.Append("<pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            xml.Append("</worksheet>");
            return xml.ToString();
        }

        private static string Cell(string reference, ExcelCell cell) => cell.IsNumber
            ? $"<c r=\"{reference}\" s=\"{cell.Style}\"><v>{cell.Value}</v></c>"
            : InlineCell(reference, cell.Value, cell.Style);

        private static string InlineCell(string reference, string value, int style) =>
            $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(value)}</t></is></c>";

        private static ExcelCell Text(string? value, int style = 0) => new(value ?? string.Empty, style, false);
        private static ExcelCell Number(int value, int style = 0) => new(value.ToString(CultureInfo.InvariantCulture), style, true);
        private static ExcelCell Money(decimal value, int style = 4) => new(value.ToString(CultureInfo.InvariantCulture), style, true);
        private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
        private static string ColumnName(int number)
        {
            var result = string.Empty;
            while (number > 0)
            {
                number--;
                result = (char)('A' + number % 26) + result;
                number /= 26;
            }
            return result;
        }

        private static string ReservationTypeName(string value) => value.ToUpperInvariant() switch
        {
            "FN" => "Function Room",
            "GH" => "Guesthouse",
            "DRM" => "Dorm and Housing",
            _ => value
        };

        private static void Add(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private sealed record ExcelCell(string Value, int Style, bool IsNumber);

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private const string RootRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private static string Workbook(string sheetName) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<sheets><sheet name=\"{Escape(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"&quot;PHP &quot;#,##0.00\"/></numFmts>" +
            "<fonts count=\"3\"><font><sz val=\"10\"/><name val=\"Aptos\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"16\"/><name val=\"Aptos Display\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"10\"/><name val=\"Aptos\"/></font></fonts>" +
            "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0F2136\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1769C2\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFD9E2EC\"/></left><right style=\"thin\"><color rgb=\"FFD9E2EC\"/></right><top style=\"thin\"><color rgb=\"FFD9E2EC\"/></top><bottom style=\"thin\"><color rgb=\"FFD9E2EC\"/></bottom><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"7\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"164\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
            "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
    }
}
