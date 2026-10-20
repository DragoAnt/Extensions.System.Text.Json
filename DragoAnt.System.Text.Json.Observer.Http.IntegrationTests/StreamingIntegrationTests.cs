using System.Text;
using AwesomeAssertions;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

public sealed class StreamingIntegrationTests
{
    [Fact]
    public async Task StreamingJson_SendAsyncReturnsBeforeBodyEnds()
    {
        await using var factory = new HostFactory();
        using var harness = JsonBodyLoggingIntegrationTests.Create(
            factory,
            Wiring.Direct,
            options => options.When = JsonBodyLogWhen.Always);

        var send = harness.Client.GetAsync("/stream-json", HttpCompletionOption.ResponseHeadersRead);
        var winner = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));
        winner.Should().BeSameAs(send);
        using var response = await send;
        var stream = await response.Content.ReadAsStreamAsync();
        var prefix = new byte[10];
        await stream.ReadExactlyAsync(prefix);

        Encoding.UTF8.GetString(prefix).Should().Be("""{"event":1""");
        factory.Gate.Release.TrySetResult();
        using var remainder = new MemoryStream();
        await stream.CopyToAsync(remainder);
        (Encoding.UTF8.GetString(prefix) + Encoding.UTF8.GetString(remainder.ToArray())).Should().Be("""{"event":1}""");
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
        entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Masked);
    }

    [Fact]
    public async Task StreamingJson_DisposedEarly_LogsIncomplete()
    {
        await using var factory = new HostFactory();
        using var harness = JsonBodyLoggingIntegrationTests.Create(
            factory,
            Wiring.Direct,
            options => options.When = JsonBodyLogWhen.Always);
        HttpResponseMessage? response = null;
        try
        {
            response = await harness.Client.GetAsync("/stream-json", HttpCompletionOption.ResponseHeadersRead);
            response.Dispose();
            var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
            entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Incomplete);
        }
        finally
        {
            response?.Dispose();
            factory.Gate.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Sse_NotBlocked_BodySkipped()
    {
        await using var factory = new HostFactory();
        using var harness = JsonBodyLoggingIntegrationTests.Create(
            factory,
            Wiring.Direct,
            options => options.When = JsonBodyLogWhen.Always);
        try
        {
            var send = harness.Client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead);
            (await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(send);
            using var response = await send;
            var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
            entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Skipped);
            entry.ResponseBody.Should().Contain("text/event-stream");
        }
        finally
        {
            factory.Gate.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task ChunkedFlushedWrites_CallerGetsAll_LoggedMasked()
    {
        await using var factory = new HostFactory();
        var masker = JsonObserver.Obj(
            JsonValuePolicy.AnyDepth(rules => rules.Match("secret").Mask("***"), ValuePolicy.BlockList));
        using var harness = JsonBodyLoggingIntegrationTests.Create(
            factory,
            Wiring.Factory,
            options => options.When = JsonBodyLogWhen.Always,
            masker);

        using var response = await harness.Client.GetAsync("/chunked", HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;

        body.Should().Be("""{"parts":[1,2,3],"secret":"pw-c"}""");
        entry.ResponseBody.Should().NotContain("pw-c");
        entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Masked);
    }
}
