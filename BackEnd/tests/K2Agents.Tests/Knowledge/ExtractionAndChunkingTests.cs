using System.IO.Compression;
using System.Text;
using K2Agents.Application.Common;
using K2Agents.Application.Knowledge;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Knowledge;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace K2Agents.Tests.Knowledge;

public static class TestFiles
{
    public static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 760;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(40, y), font);
            y -= 20;
        }
        return builder.Build();
    }

    public static byte[] Docx(string documentXml)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(documentXml);
        }
        return memory.ToArray();
    }

    public static byte[] DocxWithParagraphs(params string[] paragraphs) => Docx(
        $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(p)}</w:t></w:r></w:p>"))}</w:body></w:document>""");
}

public class TextExtractorTests
{
    private readonly TextExtractor _extractor = new(new KnowledgeSettings { MaxTextChars = 5_000, MaxPdfPages = 5 });

    private Task<Application.Abstractions.ExtractedText> Extract(byte[] bytes, KnowledgeSourceType type) =>
        _extractor.ExtractAsync(new MemoryStream(bytes), type, CancellationToken.None);

    [Fact]
    public async Task Pdf_ExtractsText()
    {
        var result = await Extract(TestFiles.Pdf("Horario de funcionamento", "Segunda a sexta das 8h as 18h"), KnowledgeSourceType.Pdf);
        Assert.Contains("Horario de funcionamento", result.Text);
        Assert.Contains("Segunda a sexta", result.Text);
    }

    [Fact]
    public async Task Docx_ExtractsParagraphsInOrder()
    {
        var result = await Extract(TestFiles.DocxWithParagraphs("Política de trocas", "Trocas em até 7 dias."), KnowledgeSourceType.Docx);
        Assert.Equal("Política de trocas\nTrocas em até 7 dias.", result.Text.Trim());
    }

    [Fact]
    public async Task Docx_WithDtd_IsRejected_PreventingXxe()
    {
        var xml = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>&xxe;</w:t></w:r></w:p></w:body></w:document>""";
        var ex = await Assert.ThrowsAsync<AppException>(() => Extract(TestFiles.Docx(xml), KnowledgeSourceType.Docx));
        Assert.Equal("invalid_file", ex.Code);
    }

    [Fact]
    public async Task Txt_DecodesUtf8WithBom_AndLatin1Fallback()
    {
        var utf8 = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Endereço: Rua das Acácias")).ToArray();
        Assert.Equal("Endereço: Rua das Acácias", (await Extract(utf8, KnowledgeSourceType.Txt)).Text);

        var latin1 = Encoding.Latin1.GetBytes("Promoção de pão");
        Assert.Equal("Promoção de pão", (await Extract(latin1, KnowledgeSourceType.Txt)).Text);
    }

    [Theory]
    [InlineData(KnowledgeSourceType.Pdf)]
    [InlineData(KnowledgeSourceType.Docx)]
    public async Task FakeSignature_IsRejected(KnowledgeSourceType type)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Extract(Encoding.UTF8.GetBytes("isto não é um arquivo real"), type));
        Assert.Equal("invalid_file", ex.Code);
    }

    [Fact]
    public async Task BinaryContentDisguisedAsTxt_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Extract([0x4D, 0x5A, 0x00, 0x00, 0x01], KnowledgeSourceType.Txt));
        Assert.Equal("invalid_file", ex.Code);
    }

    [Fact]
    public async Task ZipWithoutWordDocument_IsRejected()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("outro.txt");
        await Assert.ThrowsAsync<AppException>(() => Extract(memory.ToArray(), KnowledgeSourceType.Docx));
    }

    [Fact]
    public async Task LongText_IsTruncatedAndFlagged()
    {
        var result = await Extract(Encoding.UTF8.GetBytes(new string('a', 6_000)), KnowledgeSourceType.Txt);
        Assert.True(result.Truncated);
        Assert.Equal(5_000, result.Text.Length);
    }
}

public class KnowledgeChunkerTests
{
    [Fact]
    public void ShortText_IsSingleChunk()
    {
        Assert.Equal(["Texto curto."], KnowledgeChunker.Split("  Texto   curto.  "));
    }

    [Fact]
    public void LongText_IsSplitWithinLimit_KeepingAllContentAndOverlap()
    {
        var paragraphs = Enumerable.Range(1, 30).Select(i => $"Parágrafo {i}. " + string.Join(' ', Enumerable.Repeat($"palavra{i}", 20)));
        var text = string.Join("\n\n", paragraphs);

        var chunks = KnowledgeChunker.Split(text);

        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.True(c.Length <= KnowledgeChunker.TargetSize + KnowledgeChunker.Overlap + 2, $"Trecho com {c.Length} caracteres"));
        for (var i = 1; i <= 30; i++) Assert.Contains(chunks, c => c.Contains($"Parágrafo {i}."));
        // Sobreposição: o início de cada trecho repete o final do anterior.
        Assert.Contains(chunks[0][^40..].Split(' ').Last(), chunks[1]);
    }

    [Fact]
    public void HugeParagraphWithoutPunctuation_IsHardSplit()
    {
        var chunks = KnowledgeChunker.Split(new string('x', 5_000));
        Assert.All(chunks, c => Assert.True(c.Length <= KnowledgeChunker.TargetSize + KnowledgeChunker.Overlap + 2));
    }

    [Fact]
    public void Normalize_RemovesControlCharsAndExtraBlankLines()
    {
        Assert.Equal("a b\n\nc", KnowledgeChunker.Normalize("a\u0007  b\r\n\r\n\r\n\r\nc"));
    }
}
