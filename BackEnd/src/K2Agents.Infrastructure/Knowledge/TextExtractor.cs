using System.IO.Compression;
using System.Text;
using System.Xml;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Knowledge;
using K2Agents.Domain.Enums;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace K2Agents.Infrastructure.Knowledge;

/// <summary>
/// Extração de texto com validação do conteúdo real do arquivo (assinatura), limites de páginas e de
/// tamanho descompactado (proteção contra zip bomb) e XML sem DTD (proteção contra XXE).
/// </summary>
public class TextExtractor(KnowledgeSettings settings) : ITextExtractor
{
    private const long MaxDocxXmlBytes = 50L * 1024 * 1024;
    private const int MaxZipEntries = 5_000;

    public async Task<ExtractedText> ExtractAsync(Stream content, KnowledgeSourceType type, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();

        var text = type switch
        {
            KnowledgeSourceType.Pdf => ExtractPdf(bytes),
            KnowledgeSourceType.Docx => ExtractDocx(bytes),
            KnowledgeSourceType.Txt => ExtractTxt(bytes),
            _ => throw new AppException("Formato não suportado.", 400, "unsupported_file_type")
        };

        return text.Length > settings.MaxTextChars
            ? new ExtractedText(text[..settings.MaxTextChars], true)
            : new ExtractedText(text, false);
    }

    private string ExtractPdf(byte[] bytes)
    {
        if (!StartsWith(bytes, "%PDF-"u8, searchWindow: 1024))
            throw Invalid("O arquivo não é um PDF válido.");
        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages > settings.MaxPdfPages)
                throw new AppException($"O PDF tem {document.NumberOfPages} páginas; o limite é {settings.MaxPdfPages}.", 400, "too_many_pages");

            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(ContentOrderTextExtractor.GetText(page));
                sb.AppendLine();
                if (sb.Length > settings.MaxTextChars) break;
            }
            return sb.ToString();
        }
        catch (AppException)
        {
            throw;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw Invalid("O PDF está protegido por senha. Envie uma versão sem proteção.");
        }
        catch (Exception)
        {
            throw Invalid("Não foi possível ler o PDF. O arquivo pode estar corrompido.");
        }
    }

    private static string ExtractDocx(byte[] bytes)
    {
        if (!StartsWith(bytes, "PK\u0003\u0004"u8)) throw Invalid("O arquivo não é um DOCX válido.");
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries) throw Invalid("O DOCX contém arquivos internos demais.");

            var entry = zip.GetEntry("word/document.xml") ?? throw Invalid("O arquivo não é um documento do Word (DOCX).");
            if (entry.Length > MaxDocxXmlBytes) throw Invalid("O conteúdo do DOCX é grande demais.");

            using var limited = new LimitedReadStream(entry.Open(), MaxDocxXmlBytes);
            using var reader = XmlReader.Create(limited, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            });

            const string ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var sb = new StringBuilder();
            while (reader.Read())
            {
                if (reader.NamespaceURI != ns) continue;
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.LocalName)
                    {
                        case "t": sb.Append(reader.ReadElementContentAsString()); break;
                        case "tab": sb.Append('\t'); break;
                        case "br" or "cr": sb.Append('\n'); break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                {
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception)
        {
            throw Invalid("Não foi possível ler o DOCX. O arquivo pode estar corrompido.");
        }
    }

    private static string ExtractTxt(byte[] bytes)
    {
        var probe = bytes.AsSpan(0, Math.Min(bytes.Length, 8192));
        if (probe.Contains((byte)0)) throw Invalid("O arquivo não parece ser um texto simples.");
        try
        {
            // UTF-8 estrito (com ou sem BOM); se falhar, Latin-1 (comum em arquivos antigos do Windows).
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> signature, int searchWindow = 0)
    {
        if (searchWindow == 0) return bytes.AsSpan().StartsWith(signature);
        return bytes.AsSpan(0, Math.Min(bytes.Length, searchWindow)).IndexOf(signature) >= 0;
    }

    private static AppException Invalid(string message) => new(message, 400, "invalid_file");

    /// <summary>Interrompe a leitura além do limite, independentemente do tamanho declarado no ZIP.</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > limit) throw new AppException("O conteúdo do DOCX é grande demais.", 400, "invalid_file");
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
