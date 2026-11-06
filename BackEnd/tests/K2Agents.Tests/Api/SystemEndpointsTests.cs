using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using K2Agents.Tests.Support;

namespace K2Agents.Tests.Api;

public class SystemEndpointsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SystemInfo_ReportsDatabaseAndNeverExposesApiKey()
    {
        var json = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/system/info");

        Assert.Equal("K2 AI Agents", json.GetProperty("name").GetString());
        Assert.True(json.GetProperty("database").GetProperty("online").GetBoolean());
        Assert.False(json.GetProperty("aiProviderConfigured").GetBoolean());
        Assert.DoesNotContain("apiKey", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
