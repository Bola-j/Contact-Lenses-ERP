using System.Globalization;
using System.Text;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;

namespace Lensee.Modules.Reporting.Infrastructure;

public sealed class CsvDocumentRenderer : IDocumentRenderer
{
    public ExportFormat Format => ExportFormat.Csv;

    public async Task<RenderedDocument> RenderAsync(DocumentModel document, CancellationToken cancellationToken = default)
    {
        if (document.Tables.Count != 1)
        {
            throw new NotSupportedException("CSV export requires exactly one table.");
        }

        var table = document.Tables[0];
        var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 1024, leaveOpen: true))
        {
            await writer.WriteLineAsync(string.Join(',', table.Columns.Select(column => Escape(column.Header))));
            foreach (var row in table.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = row.Cells.Select((cell, index) => Escape(CellText(cell, index < table.Columns.Count ? table.Columns[index] : null)));
                await writer.WriteLineAsync(string.Join(',', values));
            }
        }
        stream.Position = 0;
        return new RenderedDocument(stream, "text/csv; charset=utf-8", "csv");
    }

    private static string CellText(DocumentCell cell, TableColumn? column) => cell switch
    {
        DocumentCell.Text text => SecureText(text.Value),
        DocumentCell.Code code => SecureText(code.Value),
        DocumentCell.Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        DocumentCell.Number number => number.Value.ToString(column?.NumberFormat ?? "0.####", CultureInfo.InvariantCulture),
        DocumentCell.Decimal decimalValue => decimalValue.Value.ToString(column?.NumberFormat ?? "0.####", CultureInfo.InvariantCulture),
        DocumentCell.Money money => money.Value.ToString(column?.NumberFormat ?? "0.00", CultureInfo.InvariantCulture),
        DocumentCell.Percentage percentage => percentage.Value.ToString(column?.NumberFormat ?? "0.00%", CultureInfo.InvariantCulture),
        DocumentCell.Date date => date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DocumentCell.DateTimeValue dateTime => dateTime.Value.ToString("s", CultureInfo.InvariantCulture),
        DocumentCell.Boolean boolean => boolean.Value ? "true" : "false",
        _ => string.Empty
    };

    private static string SecureText(string? value)
    {
        var text = value ?? string.Empty;
        return text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal) ? "'" + text : text;
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
