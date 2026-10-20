using System.Collections.Concurrent;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

public sealed class ConcurrencyIntegrationTests
{
    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task Parallel100_BodiesNeverCross(Wiring wiring)
    {
        await using var factory = new HostFactory();
        var masker = JsonObserver.Obj(
            JsonValuePolicy.AnyDepth(rules => rules.Match("secret").Mask("***"), ValuePolicy.BlockList));
        using var harness = JsonBodyLoggingIntegrationTests.Create(
            factory,
            wiring,
            options => options.When = JsonBodyLogWhen.Always,
            masker);
        var callerBodies = new ConcurrentDictionary<int, string>();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(async id =>
        {
            using var response = await harness.Client.GetAsync($"/item/{id}", HttpCompletionOption.ResponseHeadersRead);
            await Task.Delay(10);
            callerBodies[id] = await response.Content.ReadAsStringAsync();
        }));
        var entries = await harness.Sink!.WaitAsync(100);

        callerBodies.Should().HaveCount(100);
        foreach (var (id, body) in callerBodies)
        {
            using var document = JsonDocument.Parse(body);
            document.RootElement.GetProperty("id").GetString().Should().Be(id.ToString());
        }

        entries.Should().HaveCount(100);
        foreach (var entry in entries)
        {
            var id = entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
            using var document = JsonDocument.Parse(entry.ResponseBody!);
            document.RootElement.GetProperty("id").GetString().Should().Be(id);
            entry.ResponseBody.Should().NotContain($"s-{id}");
        }
    }
}
