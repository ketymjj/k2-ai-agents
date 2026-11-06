using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using K2Agents.Api.Localization;
using K2Agents.Tests.Support;

namespace K2Agents.Tests.Api;

public class LocalizationTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static HttpRequestMessage Login(string language, string password) =>
        new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Headers = { { "Accept-Language", language } },
            Content = JsonContent.Create(new { email = Demo.PadariaAdmin, password })
        };

    [Fact]
    public async Task BusinessErrors_FollowAcceptLanguage_PortugueseByDefault()
    {
        var client = factory.CreateClient();
        var pt = await (await client.SendAsync(Login("pt-BR", "senha-errada"))).ReadJsonAsync();
        Assert.Equal("E-mail ou senha inválidos.", pt.GetProperty("title").GetString());

        var en = await (await client.SendAsync(Login("en-US,en;q=0.9", "wrong-password"))).ReadJsonAsync();
        Assert.Equal("Invalid e-mail or password.", en.GetProperty("title").GetString());

        var none = await client.PostJsonAsync("/api/v1/auth/login", new { email = Demo.PadariaAdmin, password = "x" });
        Assert.Equal("E-mail ou senha inválidos.", (await none.ReadJsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task NotFound_AndValidationErrors_AreTranslated()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var missing = await client.GetAsync($"/api/v1/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Agent not found.", (await missing.ReadJsonAsync()).GetProperty("title").GetString());

        var invalid = await client.PostJsonAsync("/api/v1/contacts", new { name = "", phone = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var body = await invalid.ReadJsonAsync();
        Assert.Equal("Invalid data.", body.GetProperty("title").GetString());
        var messages = body.GetProperty("errors").EnumerateObject().SelectMany(p => p.Value.EnumerateArray()).Select(e => e.GetString()).ToList();
        Assert.Contains("Enter the name.", messages);
        Assert.DoesNotContain(messages, m => Regex.IsMatch(m!, "[ãçõé]"));
    }

    [Fact]
    public async Task Preferences_SaveBeginnerMode_AndLanguage_Independently()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        Assert.False(me.GetProperty("beginnerMode").GetBoolean()); // opcional: desligado por padrão

        var on = await (await client.PutJsonAsync("/api/v1/auth/me/preferences", new { beginnerMode = true })).ReadJsonAsync();
        Assert.True(on.GetProperty("beginnerMode").GetBoolean());
        Assert.Equal(me.GetProperty("preferredLanguage").GetString(), on.GetProperty("preferredLanguage").GetString());

        // Trocar só o idioma não desliga o Modo Iniciante.
        var en = await (await client.PutJsonAsync("/api/v1/auth/me/preferences", new { preferredLanguage = "en" })).ReadJsonAsync();
        Assert.True(en.GetProperty("beginnerMode").GetBoolean());
        Assert.Equal("en", en.GetProperty("preferredLanguage").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutJsonAsync("/api/v1/auth/me/preferences", new { preferredLanguage = "fr" })).StatusCode);
        await client.PutJsonAsync("/api/v1/auth/me/preferences", new { preferredLanguage = "pt-BR", beginnerMode = false });
    }

    [Fact]
    public void Catalog_TranslatesParameters_PreferringTheMostSpecificTemplate()
    {
        Assert.Equal("The message exceeds the 2000-character limit.", MessageCatalog.Translate("A mensagem excede o limite de 2000 caracteres.", En));
        Assert.Equal("The message exceeds 500 characters.", MessageCatalog.Translate("A mensagem excede 500 caracteres.", En));
        Assert.Equal("The PDF has 300 pages; the limit is 200.", MessageCatalog.Translate("O PDF tem 300 páginas; o limite é 200.", En));
        Assert.Equal("Contact not found.", MessageCatalog.Translate("Contato não encontrado(a).", En));
        // Sem tradução conhecida ou em português: mantém o texto.
        Assert.Equal("Texto livre", MessageCatalog.Translate("Texto livre", En));
        Assert.Equal("Informe o nome.", MessageCatalog.Translate("Informe o nome.", CultureInfo.GetCultureInfo("pt-BR")));
    }

    /// <summary>Toda mensagem de erro escrita no código precisa ter tradução no catálogo (evita regressão silenciosa).</summary>
    [Fact]
    public void EveryUserFacingMessageInSource_HasAnEnglishTranslation()
    {
        var src = FindSourceRoot();
        const string literal = "(\\$?)\"((?:[^\"\\\\]|\\\\.)*)\"";
        var messagePatterns = new[]
        {
            @"new AppException\(\s*" + literal,
            @"AppException\.(?:Forbidden|Conflict)\(\s*" + literal,
            @"WithMessage\(\s*" + literal,
            @"=>\s*new\(\s*" + literal + @"\s*,\s*\d{3}"
        };
        var entityPattern = new Regex(@"AppException\.NotFound\(\s*" + literal);

        var missing = new List<string>();
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"[\\/](bin|obj|Migrations)[\\/]"));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in messagePatterns)
                foreach (Match m in Regex.Matches(text, pattern))
                {
                    var message = m.Groups[2].Value;
                    if (m.Groups[1].Value == "$")
                    {
                        var i = 0;
                        message = Regex.Replace(message, @"\{[^{}]+\}", _ => $"{{{i++}}}");
                    }
                    if (!MessageCatalog.English.ContainsKey(message)) missing.Add($"{Path.GetFileName(file)}: {message}");
                }
            foreach (Match m in entityPattern.Matches(text))
                if (!MessageCatalog.EnglishEntities.ContainsKey(m.Groups[2].Value)) missing.Add($"{Path.GetFileName(file)}: entidade {m.Groups[2].Value}");
        }
        Assert.True(missing.Count == 0, "Mensagens sem tradução em MessageCatalog:\n" + string.Join("\n", missing.Distinct()));
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "K2Agents.Api"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("BackEnd/src não encontrado"), "src");
    }
}
