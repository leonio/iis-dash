using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace IisDash.Features.Export;

/// <summary>A share, written as a percentage cell.</summary>
public readonly record struct Percent(double Value);

/// <summary>
/// The smallest useful .xlsx writer: sheets of rows, a bold frozen header with a filter on it, numbers, dates and
/// percentages that Excel treats as such. Rows are streamed into the zip as they come, so an export of a million
/// requests needs no more memory than one of ten.
/// </summary>
/// <remarks>
/// Written by hand (SpreadsheetML is a zip of a few XML files) rather than with a spreadsheet library: those reach for
/// reflection, which the native build leaves out, and this needs a fraction of them.
/// </remarks>
public sealed class XlsxWriter : IDisposable
{
    /// <summary>The most rows a sheet can hold, header included.</summary>
    public const int MaxRows = 1_048_576;

    // Cell styles, by index into cellXfs in styles.xml.
    private const int Header = 1, DateTimeStyle = 2, Integer = 3, PercentStyle = 4, Decimal = 5;

    private static readonly XmlWriterSettings Settings = new() { Encoding = new System.Text.UTF8Encoding(false) };

    private readonly ZipArchive _zip;
    private readonly List<string> _sheets = [];
    private XmlWriter? _sheet;
    private Stream? _entry;
    private int _row;
    private int _columns;

    public XlsxWriter(Stream output) => _zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

    /// <summary>Starts a sheet with its header row; the columns' widths are in characters.</summary>
    public void Sheet(string name, params (string Title, double Width)[] columns)
    {
        EndSheet();
        _sheets.Add(name.Length > 31 ? name[..31] : name);
        _entry = _zip.CreateEntry($"xl/worksheets/sheet{_sheets.Count}.xml", CompressionLevel.Fastest).Open();
        _sheet = XmlWriter.Create(_entry, Settings);
        var w = _sheet;
        w.WriteStartDocument();
        w.WriteStartElement("worksheet", Ns);
        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane");
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", "A2");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("cols");
        for (int i = 0; i < columns.Length; i++)
        {
            w.WriteStartElement("col");
            w.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("width", columns[i].Width.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteStartElement("sheetData");
        _row = 0;
        _columns = columns.Length;
        WriteRow(columns.Select(c => (object?)c.Title).ToArray(), Header);
    }

    /// <summary>A row: strings, whole and decimal numbers, <see cref="DateTime"/>s and <see cref="Percent"/>s; null leaves the cell empty.</summary>
    public bool Row(params object?[] values)
    {
        if (_row >= MaxRows) return false;
        WriteRow(values, null);
        return true;
    }

    private void WriteRow(object?[] values, int? style)
    {
        var w = _sheet ?? throw new InvalidOperationException("Start a sheet first.");
        _row++;
        w.WriteStartElement("row");
        w.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is null) continue;
            w.WriteStartElement("c");
            w.WriteAttributeString("r", Column(i) + _row.ToString(CultureInfo.InvariantCulture));
            switch (values[i])
            {
                case string text:
                    w.WriteAttributeString("t", "inlineStr");
                    if (style is { } s) w.WriteAttributeString("s", s.ToString(CultureInfo.InvariantCulture));
                    w.WriteStartElement("is");
                    w.WriteStartElement("t");
                    w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(Clean(text));
                    w.WriteEndElement();
                    w.WriteEndElement();
                    break;
                case DateTime time:
                    Number(w, time.ToOADate().ToString("R", CultureInfo.InvariantCulture), DateTimeStyle);
                    break;
                case Percent percent:
                    Number(w, percent.Value.ToString("R", CultureInfo.InvariantCulture), PercentStyle);
                    break;
                case double d:
                    Number(w, d.ToString("R", CultureInfo.InvariantCulture), Decimal);
                    break;
                case long or int:
                    Number(w, Convert.ToString(values[i], CultureInfo.InvariantCulture)!, Integer);
                    break;
                default:
                    throw new ArgumentException($"Cannot write a {values[i]!.GetType().Name} to a cell.");
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void Number(XmlWriter w, string value, int style)
    {
        w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("v", value);
    }

    private void EndSheet()
    {
        if (_sheet is null) return;
        _sheet.WriteEndElement(); // sheetData
        if (_row > 1)
        {
            _sheet.WriteStartElement("autoFilter");
            _sheet.WriteAttributeString("ref", $"A1:{Column(_columns - 1)}{_row.ToString(CultureInfo.InvariantCulture)}");
            _sheet.WriteEndElement();
        }

        _sheet.WriteEndElement(); // worksheet
        _sheet.Dispose();
        _entry!.Dispose();
        _sheet = null;
    }

    /// <summary>Writes the workbook's own parts. The archive is complete once this returns.</summary>
    public void Finish()
    {
        EndSheet();
        Part("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""" +
            string.Concat(_sheets.Select((_, i) => $"""<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""")) +
            "</Types>");
        Part("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Part("xl/workbook.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="{Ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""" +
            string.Concat(_sheets.Select((name, i) => $"""<sheet name="{System.Security.SecurityElement.Escape(name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""")) +
            "</sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            string.Concat(_sheets.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>""")) +
            $"""<Relationship Id="rId{_sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        // Styles: 0 plain, 1 header (bold, grey fill), 2 date and time, 3 whole number with separators, 4 percentage, 5 decimal.
        Part("xl/styles.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="{Ns}"><numFmts count="2"><numFmt numFmtId="164" formatCode="yyyy-mm-dd hh:mm:ss"/><numFmt numFmtId="165" formatCode="0.0%"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAECEF"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="6"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
        _zip.Dispose();
    }

    public void Dispose()
    {
        _sheet?.Dispose();
        _entry?.Dispose();
        _zip.Dispose();
    }

    private void Part(string path, string xml)
    {
        using var stream = _zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        stream.Write(System.Text.Encoding.UTF8.GetBytes(xml));
    }

    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>A column's letters: 0 → A, 25 → Z, 26 → AA.</summary>
    private static string Column(int index)
    {
        string letters = "";
        for (index++; index > 0; index = (index - 1) / 26) letters = (char)('A' + (index - 1) % 26) + letters;
        return letters;
    }

    /// <summary>Characters XML 1.0 cannot carry (control characters in a log line) are dropped.</summary>
    private static string Clean(string text)
    {
        if (!text.Any(c => c < 0x20 && c is not '\t' and not '\n' and not '\r')) return text;
        return new string(text.Where(c => c >= 0x20 || c is '\t' or '\n' or '\r').ToArray());
    }
}
