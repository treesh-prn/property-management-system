using PropertyManagementSystem.Models;
using System.Globalization;
using System.Text;

namespace PropertyManagementSystem.Services
{
    /// <summary>
    /// Creates the company-charge PDF directly from the billing stored-procedure rows.
    /// The implementation uses standard PDF fonts so report generation has no SSRS or
    /// third-party rendering dependency.
    /// </summary>
    public sealed class CompanyChargesPdfService
    {
        private const double PageWidth = 842;
        private const double PageHeight = 595;
        private const double TableLeft = 50;
        private const double TableBottom = 44;
        private const double TableTop = 462;
        private const double HeaderHeight = 24;
        private const double LineHeight = 9;

        private static readonly string[] Headers =
        {
            "Company", "Department", "Room", "Dates", "Days", "Equipment",
            "Room charge", "Equip. charge", "Total"
        };

        private static readonly double[] Widths = { 70, 80, 90, 90, 35, 135, 75, 82, 85 };

        public byte[] Generate(
            IReadOnlyCollection<ReservationReport> rows,
            DateTime startDate,
            DateTime endDate,
            string preparedBy)
        {
            ArgumentNullException.ThrowIfNull(rows);

            var data = rows.ToList();
            var totalRoom = data.Sum(x => x.RoomCharge);
            var totalEquipment = data.Sum(x => x.EquipmentCharge);
            var grandTotal = data.Sum(x => x.TotalCharge);
            var notedBy = data.Select(x => x.NotedBy).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            var pages = BuildPages(data, startDate, endDate, preparedBy, notedBy, totalRoom, totalEquipment, grandTotal);

            return BuildDocument(pages);
        }

        private static List<string> BuildPages(
            IReadOnlyList<ReservationReport> rows,
            DateTime startDate,
            DateTime endDate,
            string preparedBy,
            string notedBy,
            decimal totalRoom,
            decimal totalEquipment,
            decimal grandTotal)
        {
            var pages = new List<string>();
            var rowIndex = 0;

            do
            {
                var page = new StringBuilder();
                DrawPageHeader(page, startDate, endDate, preparedBy, notedBy, rows.Count, totalRoom, totalEquipment, grandTotal);
                DrawTableHeader(page);

                var y = TableTop - HeaderHeight;
                if (rows.Count == 0)
                {
                    Fill(page, TableLeft, y - 48, Widths.Sum(), 48, "0.97 0.98 1");
                    Text(page, "No billing records were returned for the selected reporting period.", 10, TableLeft + 18, y - 29, bold: false, color: "0.28 0.35 0.43");
                }
                else
                {
                    while (rowIndex < rows.Count)
                    {
                        var row = rows[rowIndex];
                        var cells = GetCells(row);
                        var wrapped = cells.Select((value, index) => Wrap(value, Widths[index] - 10, 7, 3)).ToArray();
                        var rowHeight = Math.Max(28, wrapped.Max(lines => lines.Count) * LineHeight + 10);

                        if (y - rowHeight < TableBottom)
                        {
                            break;
                        }

                        DrawRow(page, wrapped, y, rowHeight, rowIndex % 2 == 1);
                        y -= rowHeight;
                        rowIndex++;
                    }
                }

                pages.Add(page.ToString());
            }
            while (rowIndex < rows.Count);

            for (var i = 0; i < pages.Count; i++)
            {
                var page = new StringBuilder(pages[i]);
                Text(page, $"Page {i + 1} of {pages.Count}", 7, PageWidth - 76, 20, color: "0.38 0.45 0.53");
                Text(page, $"Generated {DateTime.Now:MMM dd, yyyy h:mm tt}", 7, TableLeft, 20, color: "0.38 0.45 0.53");
                pages[i] = page.ToString();
            }

            return pages;
        }

        private static void DrawPageHeader(
            StringBuilder page,
            DateTime startDate,
            DateTime endDate,
            string preparedBy,
            string notedBy,
            int rowCount,
            decimal roomTotal,
            decimal equipmentTotal,
            decimal grandTotal)
        {
            // Compact report masthead: brand, report identity, and ownership metadata
            // remain visually distinct from the financial summary and detail table.
            Fill(page, 0, 520, PageWidth, 75, "0.975 0.985 1");
            Fill(page, 0, 520, 174, 75, "0.06 0.12 0.20");
            Fill(page, 174, 520, 5, 75, "0.10 0.43 0.78");
            Text(page, "TECO", 21, 43, 555, bold: true, color: "1 1 1");
            Text(page, "PROPERTY MANAGEMENT", 6.5, 43, 540, bold: true, color: "0.58 0.78 1");
            Text(page, "REPORTING SUITE", 6, 43, 528, color: "0.76 0.82 0.89");

            Text(page, "FINANCIAL REPORT", 7, 201, 570, bold: true, color: "0.10 0.43 0.78");
            Text(page, "Company and Department Charges", 17, 201, 547, bold: true, color: "0.06 0.12 0.20");
            Text(page, $"Reporting period  |  {startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}", 8.5, 201, 529, color: "0.34 0.42 0.51");

            const double metadataX = 640;
            Fill(page, metadataX, 529, 174, 52, "0.93 0.95 0.98");
            Stroke(page, metadataX, 529, 174, 52, "0.82 0.87 0.93");
            Text(page, "PREPARED BY", 6, metadataX + 12, 568, bold: true, color: "0.38 0.47 0.57");
            Text(page, ShortText(preparedBy, 25), 8.2, metadataX + 12, 555, bold: true, color: "0.06 0.12 0.20");
            Text(page, "NOTED BY", 6, metadataX + 12, 543, bold: true, color: "0.38 0.47 0.57");
            Text(page, ShortText(notedBy, 25), 8, metadataX + 12, 531, color: "0.20 0.28 0.37");
            Fill(page, 179, 517, PageWidth - 179, 3, "0.10 0.43 0.78");

            var cardY = 479d;
            SummaryCard(page, TableLeft, cardY, 165, "BILLING LINES", rowCount.ToString("N0", CultureInfo.InvariantCulture));
            SummaryCard(page, TableLeft + 177, cardY, 165, "ROOM CHARGES", Money(roomTotal));
            SummaryCard(page, TableLeft + 354, cardY, 165, "EQUIPMENT CHARGES", Money(equipmentTotal));
            SummaryCard(page, TableLeft + 531, cardY, 161, "TOTAL CHARGES", Money(grandTotal), emphasized: true);
        }

        private static void SummaryCard(StringBuilder page, double x, double y, double width, string label, string value, bool emphasized = false)
        {
            Fill(page, x, y, width, 34, emphasized ? "0.10 0.43 0.78" : "0.94 0.96 0.98");
            Text(page, label, 6.5, x + 9, y + 22, bold: true, color: emphasized ? "0.84 0.92 1" : "0.35 0.43 0.52");
            Text(page, value, 10, x + 9, y + 8, bold: true, color: emphasized ? "1 1 1" : "0.06 0.12 0.20");
        }

        private static void DrawTableHeader(StringBuilder page)
        {
            Fill(page, TableLeft, TableTop - HeaderHeight, Widths.Sum(), HeaderHeight, "0.10 0.43 0.78");
            var x = TableLeft;
            for (var i = 0; i < Headers.Length; i++)
            {
                Text(page, Headers[i], 6.5, x + 5, TableTop - 15, bold: true, color: "1 1 1");
                x += Widths[i];
            }
        }

        private static void DrawRow(StringBuilder page, IReadOnlyList<List<string>> cells, double top, double height, bool alternate)
        {
            Fill(page, TableLeft, top - height, Widths.Sum(), height, alternate ? "0.965 0.975 0.985" : "1 1 1");
            Stroke(page, TableLeft, top - height, Widths.Sum(), height, "0.84 0.87 0.91");

            var x = TableLeft;
            for (var column = 0; column < cells.Count; column++)
            {
                var lineY = top - 12;
                foreach (var line in cells[column])
                {
                    Text(page, line, 7, x + 5, lineY, bold: column == 8, color: "0.10 0.15 0.22");
                    lineY -= LineHeight;
                }
                x += Widths[column];
            }
        }

        private static string[] GetCells(ReservationReport row) =>
        new[]
        {
            Safe(row.CompanyCode),
            Safe(row.Department),
            Safe(row.RoomUsed),
            Safe(row.Dates),
            row.NoOfDays.ToString("N0", CultureInfo.InvariantCulture),
            Safe(row.EquipmentUsed),
            Money(row.RoomCharge),
            Money(row.EquipmentCharge),
            Money(row.TotalCharge)
        };

        private static List<string> Wrap(string value, double width, double fontSize, int maximumLines)
        {
            var normalized = Safe(value);
            var maximumCharacters = Math.Max(4, (int)Math.Floor(width / (fontSize * 0.52)));
            if (normalized.Length <= maximumCharacters)
            {
                return new List<string> { normalized };
            }

            var lines = new List<string>();
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (candidate.Length <= maximumCharacters)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                }
                current = word.Length > maximumCharacters ? word[..maximumCharacters] : word;
                if (lines.Count == maximumLines - 1)
                {
                    break;
                }
            }

            if (lines.Count < maximumLines && !string.IsNullOrEmpty(current))
            {
                lines.Add(current);
            }

            if (lines.Count == maximumLines && string.Join(" ", lines).Length < normalized.Length)
            {
                var last = lines[^1];
                lines[^1] = last.Length > 3 ? last[..Math.Min(last.Length, maximumCharacters - 3)] + "..." : "...";
            }
            return lines;
        }

        private static string Safe(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return new string(value.Trim().Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());
        }

        private static string ShortText(string? value, int maximumLength)
        {
            var safe = Safe(value);
            return safe.Length <= maximumLength
                ? safe
                : safe[..(maximumLength - 3)] + "...";
        }

        private static string Money(decimal amount) => "PHP " + amount.ToString("N2", CultureInfo.InvariantCulture);

        private static void Text(StringBuilder page, string value, double size, double x, double y, bool bold = false, string color = "0 0 0")
        {
            page.Append(CultureInfo.InvariantCulture, $"BT {color} rg /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(value)}) Tj ET\n");
        }

        private static void Fill(StringBuilder page, double x, double y, double width, double height, string color)
        {
            page.Append(CultureInfo.InvariantCulture, $"q {color} rg {x:0.##} {y:0.##} {width:0.##} {height:0.##} re f Q\n");
        }

        private static void Stroke(StringBuilder page, double x, double y, double width, double height, string color)
        {
            page.Append(CultureInfo.InvariantCulture, $"q {color} RG 0.4 w {x:0.##} {y:0.##} {width:0.##} {height:0.##} re S Q\n");
        }

        private static string Escape(string value) => Safe(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        private static byte[] BuildDocument(IReadOnlyList<string> pageStreams)
        {
            var objects = new List<string>();
            var pageReferences = string.Join(" ", Enumerable.Range(0, pageStreams.Count).Select(index => $"{5 + (index * 2)} 0 R"));
            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add($"<< /Type /Pages /Kids [{pageReferences}] /Count {pageStreams.Count} >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

            for (var index = 0; index < pageStreams.Count; index++)
            {
                var pageObjectId = 5 + (index * 2);
                var contentObjectId = pageObjectId + 1;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");
                var stream = pageStreams[index];
                var streamLength = Encoding.ASCII.GetByteCount(stream);
                objects.Add($"<< /Length {streamLength} >>\nstream\n{stream}endstream");
            }

            using var output = new MemoryStream();
            WriteAscii(output, "%PDF-1.4\n%????\n");
            var offsets = new List<long> { 0 };
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(output.Position);
                WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }

            var crossReferenceOffset = output.Position;
            WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                WriteAscii(output, $"{offset:0000000000} 00000 n \n");
            }
            WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset}\n%%EOF");
            return output.ToArray();
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
