using System.Text;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

public sealed class ExtractContext
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public int? CustomerId { get; set; }
}

[Config(typeof(AuditConfig))]
public class ExtractBenchmarks
{
    private JsonObserver<ExtractContext> _extractor = null!;
    private JsonObserver<ExtractContext> _maskAndExtract = null!;
    private JsonObserver _maskOnly = null!;
    private string _json = null!;
    private byte[] _utf8 = null!;

    [Params(PayloadShape.Nested)]
    public PayloadShape Shape { get; set; }

    [Params(8 * 1024)]
    public int Size { get; set; }

    public static JsonObserver<ExtractContext> BuildExtractor() =>
        JsonObserver.Obj<ExtractContext>(b => b
            .Match("id").ReadStr((v, c) => c.Id = v)
            .Match("type").ReadStr((v, c) => c.Type = v)
            .Match("customer").Obj(c => c
                .Match("customerId").ReadInt((v, ctx) => ctx.CustomerId = v)));

    public static JsonObserver<ExtractContext> BuildMaskAndExtract()
    {
        var masking = JsonObserverValuePolicies<ExtractContext>.Relative(b =>
            {
                foreach (var name in Payloads.SensitiveNames)
                {
                    b.Match(name).MaskStr((_, _) => Baselines.Mask);
                }
            },
            JsonObserverValuePolicies<ExtractContext>.BlockList);

        return JsonObserver.Obj<ExtractContext>(b => b
                .Match("id").ReadStr((v, c) => c.Id = v)
                .Match("type").ReadStr((v, c) => c.Type = v)
                .Match("customer").Obj(c => c
                    .Match("customerId").ReadInt((v, ctx) => ctx.CustomerId = v), masking),
            masking);
    }

    [GlobalSetup]
    public void Setup()
    {
        _extractor = BuildExtractor();
        _maskAndExtract = BuildMaskAndExtract();
        _maskOnly = Baselines.BuildObserver();
        _json = Payloads.Build(Shape, Size);
        _utf8 = Encoding.UTF8.GetBytes(_json);

        var ctx = new ExtractContext();
        _extractor.Read(_utf8, ctx);
        Check(ctx);
        var dom = JsonDocumentLookup();
        Check(dom);
        Check(LowerBoundScan());
        ctx = new ExtractContext();
        var masked = _maskAndExtract.Mask(_json, ctx)!;
        Check(ctx);
        if (masked.Contains(Payloads.SensitiveMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("mask+extract leaks a sensitive value");
        }
    }

    private static void Check(ExtractContext ctx)
    {
        if (ctx.Id != "ord-100000" || ctx.Type != "order.created" || ctx.CustomerId != 5000)
        {
            throw new InvalidOperationException($"Extraction mismatch: {ctx.Id}/{ctx.Type}/{ctx.CustomerId}");
        }
    }

    [Benchmark(Baseline = true, Description = "LowerBound Utf8JsonReader scan (bytes)")]
    public ExtractContext LowerBoundScan()
    {
        var ctx = new ExtractContext();
        var reader = new Utf8JsonReader(_utf8);
        var inCustomer = false;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName when reader.CurrentDepth == 1:
                    if (reader.ValueTextEquals("id"u8))
                    {
                        reader.Read();
                        ctx.Id = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("type"u8))
                    {
                        reader.Read();
                        ctx.Type = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("customer"u8))
                    {
                        inCustomer = true;
                    }

                    break;
                case JsonTokenType.PropertyName when inCustomer && reader.CurrentDepth == 2 && reader.ValueTextEquals("customerId"u8):
                    reader.Read();
                    ctx.CustomerId = reader.GetInt32();
                    break;
                case JsonTokenType.EndObject when reader.CurrentDepth == 1:
                    inCustomer = false;
                    break;
            }
        }

        return ctx;
    }

    [Benchmark(Description = "JsonDocument.Parse + GetProperty (bytes)")]
    public ExtractContext JsonDocumentLookup()
    {
        using var doc = JsonDocument.Parse(_utf8);
        var root = doc.RootElement;
        return new ExtractContext
        {
            Id = root.GetProperty("id").GetString(),
            Type = root.GetProperty("type").GetString(),
            CustomerId = root.GetProperty("customer").GetProperty("customerId").GetInt32(),
        };
    }

    [Benchmark(Description = "Lib JsonObserver.Read (bytes, extract only)")]
    public ExtractContext LibraryRead()
    {
        var ctx = new ExtractContext();
        _extractor.Read(_utf8, ctx);
        return ctx;
    }

    [Benchmark(Description = "Lib Mask only string->string")]
    public string? LibraryMaskOnly() => _maskOnly.Mask(_json);

    [Benchmark(Description = "Lib Mask + extract in one pass string->string")]
    public ExtractContext LibraryMaskAndExtract()
    {
        var ctx = new ExtractContext();
        _maskAndExtract.Mask(_json, ctx);
        return ctx;
    }
}
