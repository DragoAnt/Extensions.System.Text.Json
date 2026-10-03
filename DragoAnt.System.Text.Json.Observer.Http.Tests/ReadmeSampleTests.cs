using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public sealed class ReadmeSampleTests
{
    [Fact]
    public async Task ReadmeSample_CompilesAndMasksTheCardNumber()
    {
        var sink = new CapturingSink();
        var services = new ServiceCollection();
        services.AddSingleton<IJsonBodyLogSink>(sink);

        // README sample: registration
        services.AddSingleton<IJsonBodyMaskerProvider, PaymentMaskers>();
        services.AddHttpClient("PaymentApi", client => client.BaseAddress = new Uri("https://api.example.com/"))
            .AddJsonBodyLogging(options =>
            {
                options.When = JsonBodyLogWhen.OnFailure;
                options.MaxBodyBytes = 8 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new FuncHandler((_, _) =>
                Task.FromResult(HttpTestDoubles.Json(HttpStatusCode.PaymentRequired, """{"error":"declined"}"""))));

        await using var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("PaymentApi");
        var charge = new ChargeRequest("4111111111111111", 10.5m);
        var cancellationToken = CancellationToken.None;

        // README sample: call site
        using var request = new HttpRequestMessage(HttpMethod.Post, "charges")
        {
            Content = JsonContent.Create(charge),
        }.WithBodyLogging<ChargeRequest, ChargeResponse>("Charge");
        using var response = await httpClient.SendAsync(request, cancellationToken);

        var entry = await sink.WaitSingleAsync();
        Assert.Equal("Charge", entry.Operation);
        Assert.DoesNotContain("4111111111111111", entry.RequestBody);
        Assert.Equal(JsonBodyStatus.Withheld, entry.ResponseBodyStatus);
    }

    public sealed record ChargeRequest(string CardNumber, decimal Amount);

    public sealed record ChargeResponse(string Id);

    // README sample: masker provider
    public sealed class PaymentMaskers : IJsonBodyMaskerProvider
    {
        private static readonly JsonObserver Charge = JsonObserver.Obj(rules => rules.Match("cardNumber").MaskStr("****"));

        public JsonObserver? GetMasker(Type? modelType, string clientName) =>
            modelType == typeof(ChargeRequest) ? Charge : null;
    }
}
