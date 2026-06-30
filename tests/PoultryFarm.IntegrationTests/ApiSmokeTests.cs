using Microsoft.AspNetCore.Mvc.Testing;

namespace PoultryFarm.IntegrationTests;

public sealed class ApiSmokeTests
{
    [Fact]
    public async Task SystemStatus_ReturnsReadyPayload()
    {
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/system/status");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Poultry Farm API", payload);
    }
}
