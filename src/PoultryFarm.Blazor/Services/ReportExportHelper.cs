using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PoultryFarm.Blazor.Services;

public static class ReportExportHelper
{
    private const double PageWidth = 842;   // A4 landscape
    private const double PageHeight = 595;
    private const double Margin = 36;
    private const double HeaderHeight = 22;
    private const double RowHeight = 18;
    private const double TitleBlock = 40;

    public static byte[] ToExcelWorkbook(
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        var ns = XNamespace.Get("urn:schemas-microsoft-com:office:spreadsheet");
        var o = XNamespace.Get("urn:schemas-microsoft-com:office:office");
        var x = XNamespace.Get("urn:schemas-microsoft-com:office:excel");
        var ss = ns;

        var headerCells = headers.Select(h =>
            new XElement(ss + "Cell",
                new XAttribute(ss + "StyleID", "Header"),
                new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), h ?? string.Empty))).ToArray();

        var rowElements = new List<XElement>
        {
            new(ss + "Row", headerCells)
        };

        foreach (var row in rows)
        {
            var cells = new XElement[headers.Count];
            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < row.Count ? row[i] ?? string.Empty : string.Empty;
                cells[i] = new XElement(ss + "Cell",
                    new XAttribute(ss + "StyleID", "Cell"),
                    new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), value));
            }

            rowElements.Add(new XElement(ss + "Row", cells));
        }

        var columnElements = headers.Select((_, index) =>
            new XElement(ss + "Column",
                new XAttribute(ss + "Index", index + 1),
                new XAttribute(ss + "AutoFitWidth", 0),
                new XAttribute(ss + "Width", Math.Max(80, Math.Min(220, 18 * (headers[index]?.Length ?? 10)))))).ToArray();

        var workbook = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ss + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", ss),
                new XAttribute(XNamespace.Xmlns + "o", o),
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(ss + "Styles",
                    new XElement(ss + "Style",
                        new XAttribute(ss + "ID", "Header"),
                        new XElement(ss + "Font",
                            new XAttribute(ss + "Bold", 1),
                            new XAttribute(ss + "Color", "#FFFFFF")),
                        new XElement(ss + "Interior", new XAttribute(ss + "Color", "#2563EB"), new XAttribute(ss + "Pattern", "Solid")),
                        new XElement(ss + "Alignment",
                            new XAttribute(ss + "Horizontal", "Left"),
                            new XAttribute(ss + "Vertical", "Center"),
                            new XAttribute(ss + "WrapText", 1)),
                        new XElement(ss + "Borders",
                            Border(ss, "Bottom"), Border(ss, "Left"), Border(ss, "Right"), Border(ss, "Top"))),
                    new XElement(ss + "Style",
                        new XAttribute(ss + "ID", "Cell"),
                        new XElement(ss + "Alignment",
                            new XAttribute(ss + "Vertical", "Center"),
                            new XAttribute(ss + "WrapText", 1)),
                        new XElement(ss + "Borders",
                            Border(ss, "Bottom"), Border(ss, "Left"), Border(ss, "Right"), Border(ss, "Top")))),
                new XElement(ss + "Worksheet",
                    new XAttribute(ss + "Name", SanitizeSheetName(sheetName)),
                    new XElement(ss + "Table", columnElements, rowElements),
                    new XElement(x + "WorksheetOptions",
                        new XElement(x + "PageSetup",
                            new XElement(x + "Layout", new XAttribute(x + "Orientation", "Landscape")))))));

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.Append(workbook.ToString(SaveOptions.DisableFormatting));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Backward-compatible CSV export (Excel-friendly).</summary>
    public static byte[] ToExcelCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        => ToExcelWorkbook("Export", headers, rows);

    public static byte[] ToTablePdf(
        string title,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        string? subtitle = null)
    {
        var rowList = rows.Select(r =>
        {
            var cells = new string[headers.Count];
            for (var i = 0; i < headers.Count; i++)
            {
                cells[i] = i < r.Count ? r[i] ?? string.Empty : string.Empty;
            }

            return (IReadOnlyList<string>)cells;
        }).ToList();

        var colWidths = ComputeColumnWidths(headers, rowList);
        var usableWidth = PageWidth - (Margin * 2);
        var rowsPerPage = Math.Max(1, (int)((PageHeight - Margin * 2 - TitleBlock - HeaderHeight) / RowHeight));
        var pages = new List<string>();
        var pageCount = Math.Max(1, (int)Math.Ceiling(rowList.Count / (double)rowsPerPage));

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var slice = rowList.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
            pages.Add(BuildTablePage(title, subtitle, headers, slice, colWidths, usableWidth, pageIndex + 1, pageCount));
        }

        return BuildMultiPagePdf(pages);
    }

    public static byte[] ToSimplePdf(string title, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        => ToTablePdf(title, headers, rows);

    private static XElement Border(XNamespace ss, string position) =>
        new(ss + "Border",
            new XAttribute(ss + "Position", position),
            new XAttribute(ss + "LineStyle", "Continuous"),
            new XAttribute(ss + "Weight", 1),
            new XAttribute(ss + "Color", "#CBD5E1"));

    private static string SanitizeSheetName(string name)
    {
        var cleaned = new string((name ?? "Export").Where(ch => ch is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "Export";
        }

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    private static double[] ComputeColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var usable = PageWidth - (Margin * 2);
        var weights = new double[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            var maxLen = Math.Max(headers[i]?.Length ?? 1, 4);
            foreach (var row in rows.Take(80))
            {
                maxLen = Math.Max(maxLen, row[i]?.Length ?? 0);
            }

            weights[i] = Math.Clamp(maxLen, 6, 42);
        }

        var total = weights.Sum();
        return weights.Select(w => usable * (w / total)).ToArray();
    }

    private static string BuildTablePage(
        string title,
        string? subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        double[] colWidths,
        double usableWidth,
        int pageNumber,
        int pageCount)
    {
        var sb = new StringBuilder();
        var y = PageHeight - Margin;

        // Title
        sb.AppendLine("BT");
        sb.AppendLine("/F1 14 Tf");
        sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(y - 14).ToString(CultureInfo.InvariantCulture)} Td");
        sb.AppendLine($"({EscapePdf(title)}) Tj");
        sb.AppendLine("ET");
        y -= 20;

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            sb.AppendLine("BT");
            sb.AppendLine("/F1 9 Tf");
            sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(y - 10).ToString(CultureInfo.InvariantCulture)} Td");
            sb.AppendLine($"({EscapePdf(subtitle)}) Tj");
            sb.AppendLine("ET");
            y -= 16;
        }

        sb.AppendLine("BT");
        sb.AppendLine("/F1 8 Tf");
        sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(y - 10).ToString(CultureInfo.InvariantCulture)} Td");
        sb.AppendLine($"({EscapePdf($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}  ·  Page {pageNumber}/{pageCount}  ·  {rows.Count} row(s) on this page")}) Tj");
        sb.AppendLine("ET");
        y -= 18;

        var tableTop = y;
        var tableBottom = tableTop - HeaderHeight - (rows.Count * RowHeight);

        // Header fill
        sb.AppendLine("0.145 0.388 0.922 rg");
        sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(tableTop - HeaderHeight).ToString(CultureInfo.InvariantCulture)} {usableWidth.ToString(CultureInfo.InvariantCulture)} {HeaderHeight.ToString(CultureInfo.InvariantCulture)} re f");

        // Alternating row fills
        for (var r = 0; r < rows.Count; r++)
        {
            if (r % 2 == 1)
            {
                var rowTop = tableTop - HeaderHeight - (r * RowHeight);
                sb.AppendLine("0.945 0.961 0.980 rg");
                sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(rowTop - RowHeight).ToString(CultureInfo.InvariantCulture)} {usableWidth.ToString(CultureInfo.InvariantCulture)} {RowHeight.ToString(CultureInfo.InvariantCulture)} re f");
            }
        }

        // Grid
        sb.AppendLine("0.7 0.75 0.8 RG");
        sb.AppendLine("0.6 w");
        // Outer box
        sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {tableBottom.ToString(CultureInfo.InvariantCulture)} {usableWidth.ToString(CultureInfo.InvariantCulture)} {(tableTop - tableBottom).ToString(CultureInfo.InvariantCulture)} re S");
        // Header line
        sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {(tableTop - HeaderHeight).ToString(CultureInfo.InvariantCulture)} m {(Margin + usableWidth).ToString(CultureInfo.InvariantCulture)} {(tableTop - HeaderHeight).ToString(CultureInfo.InvariantCulture)} l S");
        // Horizontal row lines
        for (var r = 1; r <= rows.Count; r++)
        {
            var yy = tableTop - HeaderHeight - (r * RowHeight);
            sb.AppendLine($"{Margin.ToString(CultureInfo.InvariantCulture)} {yy.ToString(CultureInfo.InvariantCulture)} m {(Margin + usableWidth).ToString(CultureInfo.InvariantCulture)} {yy.ToString(CultureInfo.InvariantCulture)} l S");
        }

        // Vertical column lines
        var x = Margin;
        for (var c = 0; c < colWidths.Length - 1; c++)
        {
            x += colWidths[c];
            sb.AppendLine($"{x.ToString(CultureInfo.InvariantCulture)} {tableBottom.ToString(CultureInfo.InvariantCulture)} m {x.ToString(CultureInfo.InvariantCulture)} {tableTop.ToString(CultureInfo.InvariantCulture)} l S");
        }

        // Header text
        x = Margin;
        for (var c = 0; c < headers.Count; c++)
        {
            var text = FitText(headers[c], colWidths[c]);
            var textX = x + 4;
            var textY = tableTop - HeaderHeight + 6;
            sb.AppendLine("1 1 1 rg");
            sb.AppendLine("BT");
            sb.AppendLine("/F2 8 Tf");
            sb.AppendLine($"{textX.ToString(CultureInfo.InvariantCulture)} {textY.ToString(CultureInfo.InvariantCulture)} Td");
            sb.AppendLine($"({EscapePdf(text)}) Tj");
            sb.AppendLine("ET");
            x += colWidths[c];
        }

        // Body text
        for (var r = 0; r < rows.Count; r++)
        {
            x = Margin;
            var textY = tableTop - HeaderHeight - ((r + 1) * RowHeight) + 5;
            for (var c = 0; c < headers.Count; c++)
            {
                var text = FitText(rows[r][c], colWidths[c]);
                sb.AppendLine("0.08 0.11 0.16 rg");
                sb.AppendLine("BT");
                sb.AppendLine("/F1 7.5 Tf");
                sb.AppendLine($"{(x + 4).ToString(CultureInfo.InvariantCulture)} {textY.ToString(CultureInfo.InvariantCulture)} Td");
                sb.AppendLine($"({EscapePdf(text)}) Tj");
                sb.AppendLine("ET");
                x += colWidths[c];
            }
        }

        return sb.ToString();
    }

    private static string FitText(string? value, double columnWidth)
    {
        value ??= string.Empty;
        var maxChars = Math.Max(4, (int)(columnWidth / 5.2));
        return value.Length <= maxChars ? value : value[..Math.Max(1, maxChars - 1)] + "…";
    }

    private static string EscapePdf(string value)
    {
        var ascii = new string(value.Select(ch => ch is >= (char)32 and <= (char)126 ? ch : '?').ToArray());
        return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static byte[] BuildMultiPagePdf(IReadOnlyList<string> pageContents)
    {
        var objects = new List<byte[]>();
        objects.Add(Encoding.ASCII.GetBytes("1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj\n"));

        var pageObjectNumbers = new List<int>();
        var nextObj = 3;
        var kids = new StringBuilder();
        for (var i = 0; i < pageContents.Count; i++)
        {
            var pageObj = nextObj++;
            var contentObj = nextObj++;
            pageObjectNumbers.Add(pageObj);
            if (i > 0)
            {
                kids.Append(' ');
            }

            kids.Append($"{pageObj} 0 R");

            var contentBytes = Encoding.ASCII.GetBytes(pageContents[i]);
            objects.Add(Encoding.ASCII.GetBytes(
                $"{pageObj} 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Contents {contentObj} 0 R /Resources << /Font << /F1 {nextObj} 0 R /F2 {nextObj + 1} 0 R >> >> >>endobj\n"));
            objects.Add(Encoding.ASCII.GetBytes(
                $"{contentObj} 0 obj<< /Length {contentBytes.Length} >>stream\n{pageContents[i]}\nendstream\nendobj\n"));
        }

        var fontRegular = nextObj++;
        var fontBold = nextObj++;
        objects.Add(Encoding.ASCII.GetBytes($"{fontRegular} 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj\n"));
        objects.Add(Encoding.ASCII.GetBytes($"{fontBold} 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>endobj\n"));

        // Pages object references fonts that are last - but page resources reference nextObj-2 and nextObj-1 which we just assigned.
        // Problem: page objects were written with nextObj before fonts were numbered. Fix by rebuilding pages properly.

        return BuildMultiPagePdfFixed(pageContents);
    }

    private static byte[] BuildMultiPagePdfFixed(IReadOnlyList<string> pageContents)
    {
        // Object layout:
        // 1: Catalog
        // 2: Pages
        // 3: Font regular
        // 4: Font bold
        // Then pairs of Page + Content for each page
        var parts = new List<string>
        {
            "1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj\n"
        };

        var pageRefs = new List<string>();
        var objNum = 5;
        var contentParts = new List<string>();

        for (var i = 0; i < pageContents.Count; i++)
        {
            var pageObj = objNum++;
            var contentObj = objNum++;
            pageRefs.Add($"{pageObj} 0 R");
            var content = pageContents[i];
            var length = Encoding.ASCII.GetByteCount(content);
            contentParts.Add(
                $"{pageObj} 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Contents {contentObj} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>endobj\n");
            contentParts.Add(
                $"{contentObj} 0 obj<< /Length {length} >>stream\n{content}\nendstream\nendobj\n");
        }

        parts.Add($"2 0 obj<< /Type /Pages /Kids [{string.Join(" ", pageRefs)}] /Count {pageContents.Count} >>endobj\n");
        parts.Add("3 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj\n");
        parts.Add("4 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>endobj\n");
        parts.AddRange(contentParts);

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var part in parts)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(part);
        }

        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {parts.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
        {
            sb.Append($"{offsets[i]:D10} 00000 n \n");
        }

        sb.Append($"trailer<< /Size {parts.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
