using K2Agents.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace K2Agents.Infrastructure.Knowledge;

/// <summary>
/// Arquivos em disco (volume Docker em produção). O nome é sempre um Guid gerado pelo sistema e todo
/// caminho é verificado para permanecer dentro da pasta raiz (proteção contra path traversal).
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration configuration)
    {
        var configured = configuration["Storage:RootPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "storage") : configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Guid companyId, string extension, Stream content, CancellationToken ct)
    {
        var safeExtension = extension is ".pdf" or ".txt" or ".docx" ? extension : ".bin";
        var relative = Path.Combine("knowledge", companyId.ToString("N"), $"{Guid.NewGuid():N}{safeExtension}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Stream? OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Caminho de arquivo inválido.");
        return full;
    }
}
