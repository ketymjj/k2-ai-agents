using K2Agents.Application.Reports;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace K2Agents.Infrastructure.Reports;

/// <summary>
/// Desenha o relatório em PDF A4 com a fonte Noto Sans embutida (as fontes padrão do PDF não cobrem acentos no PdfPig).
/// Caracteres fora do Latin-1 (ex.: emoji) viram "?" em vez de quebrar a geração.
/// </summary>
public class PdfReportRenderer : IReportPdfRenderer
{
    private const double Margin = 40;
    private const double RowHeight = 16;

    private static readonly byte[] RegularFont = LoadFont("NotoSans-Regular.ttf");
    private static readonly byte[] BoldFont = LoadFont("NotoSans-Bold.ttf");

    private static byte[] LoadFont(string name)
    {
        using var stream = typeof(PdfReportRenderer).Assembly.GetManifestResourceStream($"K2Agents.Fonts.{name}")
                           ?? throw new InvalidOperationException($"Fonte embutida não encontrada: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public byte[] Render(PdfReport report)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddTrueTypeFont(RegularFont);
        var bold = builder.AddTrueTypeFont(BoldFont);

        var size = PageSize.A4;
        PdfPageBuilder page = builder.AddPage(size);
        var width = page.PageSize.Width;
        var height = page.PageSize.Height;
        var usable = width - 2 * Margin;
        var y = height - Margin;

        void NewPage()
        {
            page = builder.AddPage(size);
            y = height - Margin;
        }

        void Ensure(double needed)
        {
            if (y - needed < Margin + 30) NewPage();
        }

        void Text(string text, double fontSize, double x, PdfDocumentBuilder.AddedFont font, double maxWidth)
        {
            var safe = Fit(Sanitize(text), fontSize, maxWidth, page, font);
            if (safe.Length > 0) page.AddText(safe, fontSize, new PdfPoint(x, y), font);
        }

        // Cabeçalho
        page.SetTextAndFillColor(91, 33, 182);
        Text(report.Title, 16, Margin, bold, usable);
        page.ResetColor();
        y -= 20;
        foreach (var line in report.Subtitle)
        {
            page.SetTextAndFillColor(90, 90, 90);
            Text(line, 9, Margin, regular, usable);
            page.ResetColor();
            y -= 13;
        }
        y -= 8;

        foreach (var section in report.Sections)
        {
            Ensure(RowHeight * 3 + 20);
            Text(section.Heading, 12, Margin, bold, usable);
            y -= 8;

            var widths = (section.Widths is { Count: > 0 } w && w.Count == section.Columns.Count
                    ? w.Select(f => (double)f)
                    : Enumerable.Repeat(1.0 / section.Columns.Count, section.Columns.Count))
                .Select(f => f * usable).ToArray();

            void Row(IReadOnlyList<string> cells, PdfDocumentBuilder.AddedFont font, bool shaded)
            {
                Ensure(RowHeight);
                if (shaded)
                {
                    page.SetTextAndFillColor(243, 240, 255);
                    page.DrawRectangle(new PdfPoint(Margin, y - RowHeight + 4), usable, RowHeight, 0, fill: true);
                    page.ResetColor();
                }
                var x = Margin + 4;
                for (var i = 0; i < widths.Length; i++)
                {
                    var saved = y;
                    y -= RowHeight - 8;
                    Text(i < cells.Count ? cells[i] : string.Empty, 8.5, x, font, widths[i] - 8);
                    y = saved;
                    x += widths[i];
                }
                y -= RowHeight;
            }

            Row(section.Columns, bold, shaded: true);
            if (section.Rows.Count == 0)
                Row(["—"], regular, shaded: false);
            foreach (var row in section.Rows)
            {
                Row(row, regular, shaded: false);
                page.SetStrokeColor(225, 225, 225);
                page.DrawLine(new PdfPoint(Margin, y + 4), new PdfPoint(Margin + usable, y + 4), 0.5);
                page.ResetColor();
            }
            y -= 14;
        }

        // Rodapé com a observação sobre custos, quebrado em linhas.
        Ensure(40);
        page.SetTextAndFillColor(110, 110, 110);
        foreach (var line in Wrap(Sanitize(report.Footer), 7.5, usable, page, regular))
        {
            Ensure(11);
            Text(line, 7.5, Margin, regular, usable);
            y -= 11;
        }
        page.ResetColor();

        return builder.Build();
    }

    /// <summary>Mantém só caracteres da WinAnsi (Latin-1 + tipografia comum); o resto vira "?".</summary>
    internal static string Sanitize(string text)
    {
        var chars = text.Replace('–', '-').Replace('—', '-').Replace('’', '\'').Replace("…", "...").Replace(' ', ' ').Replace(' ', ' ')
            .Select(c => c is >= ' ' and <= '~' or >= '¡' and <= 'ÿ' || c == '•' ? c : c is '\n' or '\r' or '\t' ? ' ' : '?');
        return new string(chars.ToArray());
    }

    private static string Fit(string text, double fontSize, double maxWidth, PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        if (Measure(text, fontSize, page, font) <= maxWidth) return text;
        var t = text;
        while (t.Length > 1 && Measure(t + "...", fontSize, page, font) > maxWidth) t = t[..^1];
        return t.TrimEnd() + "...";
    }

    private static IEnumerable<string> Wrap(string text, double fontSize, double maxWidth, PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Measure(candidate, fontSize, page, font) > maxWidth)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) yield return line;
    }

    private static double Measure(string text, double fontSize, PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        var letters = page.MeasureText(text, fontSize, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters.Max(l => l.EndBaseLine.X) - letters.Min(l => l.StartBaseLine.X);
    }
}
