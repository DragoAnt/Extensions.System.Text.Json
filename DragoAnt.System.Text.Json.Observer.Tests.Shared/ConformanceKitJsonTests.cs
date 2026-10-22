using DragoAnt.Observer.Conformance;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ConformanceKitJsonTests
{
    [Fact]
    public async Task Json_PassesTheSeed()
    {
        var report = await ConformanceKit.RunAsync(JsonObserverFormat.Instance,
            new ConformanceTraits { Comments = true }, cancellationToken: TestContext.Current.CancellationToken);

        report.Failures.Should().BeEmpty(string.Join(Environment.NewLine, report.Failures.Take(20)));
        report.ScenariosRun.Should().Be(ConformanceKit.Seed.Count(s => s.Documents.ContainsKey("json")));
        report.ChecksRun.Should().BeGreaterThan(1000);
    }
}
