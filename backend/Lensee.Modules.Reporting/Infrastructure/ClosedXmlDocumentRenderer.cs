using ClosedXML.Excel;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;

namespace Lensee.Modules.Reporting.Infrastructure;

public sealed class ClosedXmlDocumentRenderer(
    IDocumentBrandingProvider brandingProvider,
    IDocumentLocalizer localizer) : IDocumentRenderer
{
    public ExportFormat Format => ExportFormat.Excel;

    public Task<RenderedDocument> RenderAsync(DocumentModel document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook();
        if (document.Tables.Count <= 1)
        {
            WriteSheet(workbook, document, document.Tables.FirstOrDefault(), document.Header.Title, cancellationToken);
        }
        else
        {
            WriteSheet(workbook, document, null, localizer.Text("Summary", document.Metadata.Language), cancellationToken);
            foreach (var table in document.Tables)
            {
                WriteSheet(workbook, document, table, table.Title ?? "Data", cancellationToken);
            }
        }

        var stream = new MemoryStream();
        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return Task.FromResult(new RenderedDocument(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"));
    }

    private void WriteSheet(XLWorkbook workbook, DocumentModel document, TableSection? table, string sheetName, CancellationToken cancellationToken)
    {
        var worksheet = workbook.Worksheets.Add(SanitizeSheetName(sheetName, workbook));
        worksheet.RightToLeft = document.Metadata.Language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        worksheet.Cell(1, 1).Value = document.Metadata.Language == DocumentLanguage.English
            ? brandingProvider.Branding.CompanyNameEnglish
            : brandingProvider.Branding.CompanyNameArabic;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(2, 1).Value = document.Header.Title;
        worksheet.Cell(2, 1).Style.Font.Bold = true;
        worksheet.Cell(3, 1).Value = $"{localizer.Text("GeneratedAt", document.Metadata.Language)}: {document.Metadata.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC";

        var row = 5;
        foreach (var section in document.InfoSections)
        {
            foreach (var field in section.Fields)
            {
                worksheet.Cell(row, 1).Value = SafeText(field.Label);
                worksheet.Cell(row, 2).Value = SafeText(field.Value);
                row++;
            }
        }
        foreach (var metric in document.SummaryMetrics)
        {
            worksheet.Cell(row, 1).Value = SafeText(metric.Label);
            worksheet.Cell(row, 2).Value = SafeText(metric.Value);
            row++;
        }

        if (table is not null)
        {
            row++;
            var headerRow = row;
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var cell = worksheet.Cell(row, column + 1);
                cell.Value = SafeText(table.Columns[column].Header);
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.FromHtml("#172331");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9EDF1");
                ApplyAlignment(cell, table.Columns[column], worksheet.RightToLeft);
            }
            row++;
            for (var dataRowIndex = 0; dataRowIndex < table.Rows.Count; dataRowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dataRow = table.Rows[dataRowIndex];
                for (var column = 0; column < table.Columns.Count; column++)
                {
                    if (column >= dataRow.Cells.Count) continue;
                    SetValue(worksheet.Cell(row, column + 1), dataRow.Cells[column], table.Columns[column], worksheet.RightToLeft);
                }
                row++;
            }
            if (table.Columns.Count > 0)
            {
                worksheet.Range(headerRow, 1, Math.Max(headerRow, row - 1), table.Columns.Count).SetAutoFilter();
                worksheet.SheetView.FreezeRows(headerRow);
            }
        }

        if (document.Totals is { Values.Count: > 0 })
        {
            row++;
            foreach (var total in document.Totals.Values)
            {
                worksheet.Cell(row, 1).Value = SafeText(total.Label);
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 2).Value = SafeText(total.Value);
                worksheet.Cell(row, 2).Style.Font.Bold = true;
                row++;
            }
        }

        worksheet.Columns().AdjustToContents(1, 60);
        worksheet.Style.Alignment.WrapText = true;
        worksheet.PageSetup.PageOrientation = document.Metadata.Landscape ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        worksheet.PageSetup.FitToPages(1, 0);
    }

    private static void SetValue(IXLCell cell, DocumentCell value, TableColumn column, bool rightToLeft)
    {
        switch (value)
        {
            case DocumentCell.Text text:
                cell.Value = SafeText(text.Value);
                break;
            case DocumentCell.Code code:
                cell.Value = SafeText(code.Value);
                break;
            case DocumentCell.Integer integer:
                cell.Value = integer.Value;
                break;
            case DocumentCell.Number number:
                cell.Value = Convert.ToDouble(number.Value);
                break;
            case DocumentCell.Decimal decimalValue:
                cell.Value = Convert.ToDouble(decimalValue.Value);
                break;
            case DocumentCell.Money money:
                cell.Value = Convert.ToDouble(money.Value);
                break;
            case DocumentCell.Percentage percentage:
                cell.Value = Convert.ToDouble(percentage.Value);
                break;
            case DocumentCell.Date date:
                cell.Value = date.Value.ToDateTime(TimeOnly.MinValue);
                break;
            case DocumentCell.DateTimeValue dateTime:
                cell.Value = dateTime.Value;
                break;
            case DocumentCell.Boolean boolean:
                cell.Value = boolean.Value;
                break;
        }
        var direction = value switch
        {
            DocumentCell.Text text when text.Direction != DocumentTextDirection.Auto => text.Direction,
            DocumentCell.Code code => code.Direction,
            _ => column.Direction
        };
        if (direction == DocumentTextDirection.LeftToRight)
        {
            cell.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.LeftToRight;
        }
        else if (direction == DocumentTextDirection.RightToLeft)
        {
            cell.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;
        }
        ApplyAlignment(cell, column, rightToLeft);
        if (!string.IsNullOrWhiteSpace(column.NumberFormat)) cell.Style.NumberFormat.Format = column.NumberFormat;
    }

    private static void ApplyAlignment(IXLCell cell, TableColumn column, bool rightToLeft)
    {
        cell.Style.Alignment.Horizontal = column.Alignment switch
        {
            DocumentTextAlignment.Center => XLAlignmentHorizontalValues.Center,
            DocumentTextAlignment.End => rightToLeft ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right,
            _ => rightToLeft ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left
        };
    }

    private static string SafeText(string? value)
    {
        var text = value ?? string.Empty;
        return text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal) ? "'" + text : text;
    }

    private static string SanitizeSheetName(string value, XLWorkbook workbook)
    {
        var cleaned = new string(value.Where(character => !"[]:*?/\\".Contains(character, StringComparison.Ordinal)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Report";
        if (cleaned.Length > 31) cleaned = cleaned[..31];
        var candidate = cleaned;
        var suffix = 2;
        while (workbook.Worksheets.Any(sheet => string.Equals(sheet.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var marker = $" {suffix++}";
            candidate = cleaned[..Math.Min(cleaned.Length, 31 - marker.Length)] + marker;
        }
        return candidate;
    }
}
