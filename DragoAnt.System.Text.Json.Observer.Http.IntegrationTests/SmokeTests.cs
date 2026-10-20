using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Host_Starts_EchoRoundTrips()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateDirectClient(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Never });
        const string json = """{"value":"original"}""";

        using var response = await client.PostAsJsonAsync("/echo", new { value = "original" });

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().Be(json);
    }
}
