using PropertyManagementSystem.Models;
using System.Globalization;
using System.Text;

namespace PropertyManagementSystem.Services
{
    public sealed class OccupancyPdfService
    {
        private const double PageWidth = 842;
        private const double PageHeight = 595;
        private const double Left = 48;
        private const double TableTop = 464;
        private const double TableBottom = 45;
        private const double HeaderHeight = 25;
        private static readonly string[] Headers =
            { "Reservation", "Occupant", "Facility", "Room", "Arrival", "Departure", "Days" };
        private static readonly double[] Widths = { 92, 135, 125, 110, 120, 120, 42 };

        public byte[] Generate(
            IReadOnlyCollection<OccupancyReportRow> rows,
            DateTime startDate,
            DateTime endDate,
            string reservationType)
        {
            ArgumentNullException.ThrowIfNull(rows);
            var data = rows.OrderBy(row => row.StartDate).ThenBy(row => row.RoomName).ToList();
            var pages = BuildPages(data, startDate, endDate, reservationType);
            return BuildDocument(pages);
        }

        private static List<string> BuildPages(
            IReadOnlyList<OccupancyReportRow> rows,
            DateTime startDate,
            DateTime endDate,
            string reservationType)
        {
            var pages = new List<string>();
            var index = 0;
            do
            {
                var page = new StringBuilder();
                Header(page, rows, startDate, endDate, reservationType);
                TableHeader(page);
                var y = TableTop - HeaderHeight;

                if (rows.Count == 0)
                {
                    Fill(page, Left, y - 52, Widths.Sum(), 52, "0.97 0.98 1");
                    Text(page, "No approved reservations overlap the selected reporting period.", 10, Left + 18, y - 31, color: "0.28 0.35 0.43");
                }
                else
                {
                    while (index < rows.Count && y - 37 >= TableBottom)
                    {
                        Row(page, rows[index], y, index % 2 == 1);
                        y -= 37;
                        index++;
                    }
                }
                pages.Add(page.ToString());
            }
            while (index < rows.Count);

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var page = new StringBuilder(pages[pageIndex]);
                Text(page, $"Generated {DateTime.Now:MMM dd, yyyy h:mm tt}", 7, Left, 20, color: "0.38 0.45 0.53");
                Text(page, $"Page {pageIndex + 1} of {pages.Count}", 7, PageWidth - 76, 20, color: "0.38 0.45 0.53");
                pages[pageIndex] = page.ToString();
            }
            return pages;
        }

        private static void Header(StringBuilder page, IReadOnlyCollection<OccupancyReportRow> rows, DateTime startDate, DateTime endDate, string reservationType)
        {
            var distinctReservations = rows.Select(row => row.ReservationCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var roomCount = rows.Select(row => row.RoomName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            Fill(page, 0, 520, PageWidth, 75, "0.975 0.985 1");
            Fill(page, 0, 520, 174, 75, "0.06 0.12 0.20");
            Fill(page, 174, 520, 5, 75, "0.10 0.43 0.78");
            Text(page, "TECO", 21, 43, 555, bold: true, color: "1 1 1");
            Text(page, "PROPERTY MANAGEMENT", 6.5, 43, 540, bold: true, color: "0.58 0.78 1");
            Text(page, "REPORTING SUITE", 6, 43, 528, color: "0.76 0.82 0.89");

            Text(page, "OPERATIONS REPORT", 7, 201, 570, bold: true, color: "0.10 0.43 0.78");
            Text(page, "Reservation Occupancy Report", 17, 201, 547, bold: true, color: "0.06 0.12 0.20");
            Text(page, $"Reporting period  |  {startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}", 8.5, 201, 529, color: "0.34 0.42 0.51");

            const double metadataX = 640;
            Fill(page, metadataX, 529, 174, 52, "0.93 0.95 0.98");
            Stroke(page, metadataX, 529, 174, 52, "0.82 0.87 0.93");
            Text(page, "RESERVATION TYPE", 6, metadataX + 12, 568, bold: true, color: "0.38 0.47 0.57");
            Text(page, TypeName(reservationType), 8.2, metadataX + 12, 555, bold: true, color: "0.06 0.12 0.20");
            Text(page, "REPORT SCOPE", 6, metadataX + 12, 543, bold: true, color: "0.38 0.47 0.57");
            Text(page, $"{distinctReservations:N0} reservations / {roomCount:N0} rooms", 8, metadataX + 12, 531, color: "0.20 0.28 0.37");
            Fill(page, 179, 517, PageWidth - 179, 3, "0.10 0.43 0.78");

            Summary(page, Left, 483, 220, "RESERVATIONS", distinctReservations.ToString("N0", CultureInfo.InvariantCulture));
            Summary(page, Left + 232, 483, 220, "OCCUPANCY LINES", rows.Count.ToString("N0", CultureInfo.InvariantCulture));
            Summary(page, Left + 464, 483, 282, "ROOMS USED", roomCount.ToString("N0", CultureInfo.InvariantCulture));
        }

        private static void Summary(StringBuilder page, double x, double y, double width, string label, string value)
        {
            Fill(page, x, y, width, 32, "0.94 0.96 0.98");
            Text(page, label, 6.5, x + 9, y + 20, bold: true, color: "0.35 0.43 0.52");
            Text(page, value, 10, x + 9, y + 7, bold: true, color: "0.06 0.12 0.20");
        }

        private static void TableHeader(StringBuilder page)
        {
            Fill(page, Left, TableTop - HeaderHeight, Widths.Sum(), HeaderHeight, "0.10 0.43 0.78");
            var x = Left;
            for (var i = 0; i < Headers.Length; i++)
            {
                Text(page, Headers[i], 7, x + 6, TableTop - 16, bold: true, color: "1 1 1");
                x += Widths[i];
            }
        }

        private static void Row(StringBuilder page, OccupancyReportRow row, double top, bool alternate)
        {
            const double height = 37;
            Fill(page, Left, top - height, Widths.Sum(), height, alternate ? "0.965 0.975 0.985" : "1 1 1");
            Stroke(page, Left, top - height, Widths.Sum(), height, "0.84 0.87 0.91");
            var cells = new[]
            {
                Safe(row.ReservationCode), Safe(row.Occupant), Safe(row.FacilityName), Safe(row.RoomName),
                row.StartDate == DateTime.MinValue ? "-" : row.StartDate.ToString("MMM dd, yyyy h:mm tt", CultureInfo.InvariantCulture),
                row.EndDate == DateTime.MinValue ? "-" : row.EndDate.ToString("MMM dd, yyyy h:mm tt", CultureInfo.InvariantCulture),
                row.NoOfDays.ToString("N0", CultureInfo.InvariantCulture)
            };
            var x = Left;
            for (var i = 0; i < cells.Length; i++)
            {
                foreach (var line in Wrap(cells[i], Widths[i] - 10, 7, 2).Select((value, line) => (value, line)))
                    Text(page, line.value, 7, x + 5, top - 13 - (line.line * 10), bold: i == 0, color: "0.10 0.15 0.22");
                x += Widths[i];
            }
        }

        private static List<string> Wrap(string value, double width, double size, int maxLines)
        {
            var limit = Math.Max(4, (int)Math.Floor(width / (size * 0.52)));
            if (value.Length <= limit) return new List<string> { value };
            var output = new List<string>();
            var remaining = value;
            while (remaining.Length > 0 && output.Count < maxLines)
            {
                var take = Math.Min(limit, remaining.Length);
                var split = take < remaining.Length ? remaining.LastIndexOf(' ', take - 1, take) : take;
                if (split <= 0) split = take;
                output.Add(remaining[..split].Trim());
                remaining = remaining[split..].Trim();
            }
            if (remaining.Length > 0 && output.Count > 0)
                output[^1] = output[^1].Length > 3 ? output[^1][..Math.Min(output[^1].Length, limit - 3)] + "..." : "...";
            return output;
        }

        private static string TypeName(string value) => value.ToUpperInvariant() switch
        {
            "FN" => "Function Room",
            "GH" => "Guesthouse",
            "DRM" => "Dorm and Housing",
            _ => Safe(value)
        };

        private static string Safe(string? value) => string.IsNullOrWhiteSpace(value)
            ? "-"
            : new string(value.Trim().Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());

        private static void Text(StringBuilder page, string value, double size, double x, double y, bool bold = false, string color = "0 0 0") =>
            page.Append(CultureInfo.InvariantCulture, $"BT {color} rg /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(value)}) Tj ET\n");

        private static void Fill(StringBuilder page, double x, double y, double width, double height, string color) =>
            page.Append(CultureInfo.InvariantCulture, $"q {color} rg {x:0.##} {y:0.##} {width:0.##} {height:0.##} re f Q\n");

        private static void Stroke(StringBuilder page, double x, double y, double width, double height, string color) =>
            page.Append(CultureInfo.InvariantCulture, $"q {color} RG 0.4 w {x:0.##} {y:0.##} {width:0.##} {height:0.##} re S Q\n");

        private static string Escape(string value) => Safe(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        private static byte[] BuildDocument(IReadOnlyList<string> streams)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, streams.Count).Select(i => $"{5 + i * 2} 0 R"))}] /Count {streams.Count} >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
            };
            for (var i = 0; i < streams.Count; i++)
            {
                var contentId = 6 + i * 2;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(streams[i])} >>\nstream\n{streams[i]}endstream");
            }
            using var output = new MemoryStream();
            Write(output, "%PDF-1.4\n%????\n");
            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                Write(output, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            var xref = output.Position;
            Write(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) Write(output, $"{offset:0000000000} 00000 n \n");
            Write(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return output.ToArray();
        }

        private static void Write(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
