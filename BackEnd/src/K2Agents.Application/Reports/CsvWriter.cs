using System.Globalization;
using System.Text;

namespace K2Agents.Application.Reports;

/// <summary>
/// CSV compatível com Excel: UTF-8 com BOM; em pt-BR usa ";" e vírgula decimal. Valores que começam com
/// = + - @ (ou tab/CR) recebem um apóstrofo, para não serem interpretados como fórmula (CSV injection).
/// </summary>
public sealed class CsvWriter(bool portuguese)
{
    private readonly StringBuilder _sb = new();
    private readonly char _sep = portuguese ? ';' : ',';
    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(portuguese ? "pt-BR" : "en-US");

    public CsvWriter Row(params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) _sb.Append(_sep);
            _sb.Append(Escape(Format(values[i])));
        }
        _sb.Append("\r\n");
        return this;
    }

    public byte[] ToBytes() => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(_sb.ToString())).ToArray();

    private string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? (Culture.Name == "pt-BR" ? "sim" : "yes") : (Culture.Name == "pt-BR" ? "não" : "no"),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.######", Culture),
        double x => x.ToString("0.##", Culture),
        IFormattable f => f.ToString(null, Culture),
        _ => value.ToString() ?? string.Empty
    };

    private string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !IsNumber(value)) value = "'" + value;
        var needsQuotes = value.Contains(_sep) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    // Números negativos ("-1,5") são legítimos e não viram fórmula.
    private bool IsNumber(string value) => decimal.TryParse(value, NumberStyles.Number, Culture, out _);
}
