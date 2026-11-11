using System.Diagnostics;
using System.Text;
#if NET10_0_OR_GREATER
using System.Buffers;
using RedoxJsonDocument = REDox.Json.JsonDocument;
using RedoxJsonValueKind = REDox.Json.JsonValueKind;
#endif

namespace DragoAnt.System.Text.Json.Observer.Benchmarks.Harness;

public static class Probes
{
    private const string Secret = "S3cr3tV4l";

    private sealed record Probe(string Name, Func<string> Input, string[] Sensitive, JsonReaderOptions ReaderOptions = default);

    private sealed record Outcome(string Observer, string Probe, string Result, string Error, string Leak, long Allocated, double Ms);

    public static void Run(string? outputPath)
    {
        var observers = new (string Name, Func<string, JsonReaderOptions, string?> Mask)[]
        {
            ("MaskStr", Wrap(BuildObserver(raw: false))),
            ("MaskRawValue", Wrap(BuildObserver(raw: true))),
            ("MaskAny", Wrap(BuildMaskAnyObserver())),
            ("Bytes API + MaskAny", MaskBytes(BuildMaskAnyObserver())),
            ("Bytes API + MaskAny, 4 KB output / 256 B value caps", MaskBytes(BuildMaskAnyObserver(), new JsonObserverOptions { MaxOutputBytes = 4096, MaxValueBytes = 256 })),
            ("Read-mode mask+extract", MaskAndExtract),
        };

        var outcomes = new List<Outcome>();
        foreach (var probe in BuildProbes())
        {
            foreach (var (name, mask) in observers)
            {
                outcomes.Add(Execute(name, probe, mask));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("| Observer | Probe | Throws | Output (first 160 chars) | Sensitive value in output | Alloc | ms |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var o in outcomes)
        {
            sb.Append("| ").Append(o.Observer)
                .Append(" | ").Append(o.Probe)
                .Append(" | ").Append(Cell(o.Error))
                .Append(" | ").Append(Cell(o.Result))
                .Append(" | ").Append(o.Leak)
                .Append(" | ").Append(FormatBytes(o.Allocated))
                .Append(" | ").Append(o.Ms.ToString("0.0", global::System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        Console.WriteLine(sb.ToString());
        if (!string.IsNullOrEmpty(outputPath))
        {
            File.WriteAllText(outputPath, sb.ToString());
        }
    }

#if NET10_0_OR_GREATER
    public static void RunRedox()
    {
        var maskChecks = 0;
        foreach (var shape in Enum.GetValues<PayloadShape>())
        {
            foreach (var size in new[] { 1024, 8 * 1024, 64 * 1024 })
            {
                var json = Payloads.Build(shape, size);
                var expected = Baselines.Dom(json);
                var actual = Baselines.RedoxMask(json);
                AssertEqual(expected, actual, $"mask string {shape}/{size}");

                var output = new ArrayBufferWriter<byte>();
                Baselines.RedoxMask(Encoding.UTF8.GetBytes(json), output);
                AssertEqual(expected, Encoding.UTF8.GetString(output.WrittenSpan), $"mask bytes {shape}/{size}");
                maskChecks += 2;
            }
        }

        var extractChecks = 0;
        foreach (var shape in new[] { PayloadShape.Flat, PayloadShape.Nested })
        {
            foreach (var size in new[] { 1024, 8 * 1024, 64 * 1024 })
            {
                var utf8 = Encoding.UTF8.GetBytes(Payloads.Build(shape, size));
                using var expectedDocument = JsonDocument.Parse(utf8);
                var expectedRoot = expectedDocument.RootElement;
                var expected = new ExtractContext
                {
                    Id = expectedRoot.GetProperty("id").GetString(),
                    Type = expectedRoot.GetProperty("type").GetString(),
                    CustomerId = expectedRoot.GetProperty("customer").GetProperty("customerId").GetInt32(),
                };
                var actual = Baselines.RedoxExtract(utf8);
                AssertEqual(expected.Id, actual.Id, $"extract id {shape}/{size}");
                AssertEqual(expected.Type, actual.Type, $"extract type {shape}/{size}");
                AssertEqual(expected.CustomerId, actual.CustomerId, $"extract customerId {shape}/{size}");
                extractChecks++;
            }
        }

        var invalidUtf8 = new (string Name, byte[] Bytes)[]
        {
            ("overlong sequence C0 AF", BuildInvalidUtf8(0xC0, 0xAF)),
            ("lone continuation byte 80", BuildInvalidUtf8(0x80)),
            ("truncated multi-byte sequence E2 82", BuildInvalidUtf8(0xE2, 0x82)),
        };

        foreach (var (name, input) in invalidUtf8)
        {
            var output = new ArrayBufferWriter<byte>();
            using var document = RedoxJsonDocument.Parse(input.AsMemory(), options: new REDox.Json.JsonDocumentOptions
            {
                EnableValueValidation = true,
                MaxDepth = 64,
            });
            RedoxJsonDocument.EncodeTo(document.RootElement, output);
            if (!input.AsSpan().SequenceEqual(output.WrittenSpan))
            {
                throw new InvalidOperationException($"REDox did not pass through {name} in strict mode.");
            }

            Console.WriteLine($"REDox strict invalid UTF-8: {name} => accepted and passed through unchanged");
        }

        Console.WriteLine($"REDox correctness: {maskChecks} mask outputs, {extractChecks} extraction contexts, {invalidUtf8.Length} invalid UTF-8 cases passed");
    }

    private static byte[] BuildInvalidUtf8(params byte[] invalidBytes) =>
    [
        .. Encoding.UTF8.GetBytes("{\"value\":\""),
        .. invalidBytes,
        .. Encoding.UTF8.GetBytes("\"}"),
    ];

    private static void AssertEqual<T>(T expected, T actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"REDox correctness mismatch for {scenario}.");
        }
    }
#endif

    private static Func<string, JsonReaderOptions, string?> Wrap(JsonObserver observer) => (json, options) => observer.Mask(json, ToOptions(options));

    private static JsonObserverOptions ToOptions(JsonReaderOptions options) => new() { MaxDepth = options.MaxDepth == 0 ? 64 : options.MaxDepth };

    private static string? MaskAndExtract(string json, JsonReaderOptions options)
    {
        var observer = JsonObserver.Obj<ProbeContext>(b => b
                .Match("id").ReadInt((v, c) => c.Id = v)
                .Match("active").ReadBool((v, c) => c.Active = v),
            JsonValuePolicy.AnyDepth<ProbeContext>(b => b
                    .Match("password").Mask((_, _) => "***", MaskNulls.Mask)
                    .Match("pin").Mask((_, _) => "***", MaskNulls.Mask),
                ValuePolicy.BlockList));
        return observer.Mask(json, new ProbeContext(), ToOptions(options));
    }

    private sealed class ProbeContext
    {
        public int? Id { get; set; }
        public bool? Active { get; set; }
    }

    private static Func<string, JsonReaderOptions, string?> MaskBytes(JsonObserver observer, JsonObserverOptions? options = null) => (json, _) =>
    {
        var output = new global::System.Buffers.ArrayBufferWriter<byte>();
        var result = observer.Mask(Encoding.UTF8.GetBytes(json), output, options);
        return $"[{result.Status}] {Encoding.UTF8.GetString(output.WrittenSpan)}";
    };

    private static JsonObserver BuildMaskAnyObserver() =>
        JsonObserver.Any(_ => { }, _ => { }, JsonValuePolicy.AnyDepth(
            b => b.Match(Names.OneOf("password", "pin")).Mask("***"),
            ValuePolicy.BlockList));

    private static JsonObserver BuildObserver(bool raw)
    {
        var policy = JsonValuePolicy.AnyDepth(b =>
            {
                foreach (var name in new[] { "password", "pin" })
                {
                    if (raw)
                    {
                        b.Match(name).Mask(MaskTag.Full);
                    }
                    else
                    {
                        b.Match(name).Mask((_, _) => "***", MaskNulls.Mask);
                    }
                }
            },
            ValuePolicy.BlockList);
        return JsonObserver.Any(o => { }, a => { }, policy);
    }

    private static IEnumerable<Probe> BuildProbes()
    {
        yield return new Probe("invalid JSON (missing value)", () => $$"""{"password":"{{Secret}}","a": }""", [Secret]);
        yield return new Probe("truncated mid-string (size cap)", () => $$"""{"user":"bob","password":"{{Secret}}","pin":"12""", [Secret]);
        yield return new Probe("truncated mid-object after secret", () => $$"""{"password":"{{Secret}}","nested":{"a":1""", [Secret]);
        yield return new Probe("escaped property name pa\\u0073sword", () => $$"""{"pa\u0073sword":"{{Secret}}"}""", [Secret]);
        yield return new Probe("escaped value", () => """{"password":"S3cr\u0033tV4l"}""", [Secret, "S3cr\\u0033tV4l"]);
        yield return new Probe("case variants Password/PASSWORD", () => """{"Password":"S3cr3tA","PASSWORD":"S3cr3tB","pAsSwOrD":"S3cr3tC"}""", ["S3cr3tA", "S3cr3tB", "S3cr3tC"]);
        yield return new Probe("sensitive number", () => """{"pin":987654321}""", ["987654321"]);
        yield return new Probe("sensitive bool", () => """{"password":true}""", ["true"]);
        yield return new Probe("sensitive null", () => """{"password":null}""", []);
        yield return new Probe("sensitive object", () => "{\"password\":{\"value\":\"" + Secret + "\"}}", [Secret]);
        yield return new Probe("sensitive array", () => $$"""{"password":["{{Secret}}","x"]}""", [Secret]);
        yield return new Probe("array of objects", () => $$"""{"users":[{"name":"a","password":"{{Secret}}1"},{"password":"{{Secret}}2"}]}""", [Secret]);
        yield return new Probe("root array of objects", () => $$"""[{"password":"{{Secret}}1"},{"pin":"{{Secret}}2"}]""", [Secret]);
        yield return new Probe("nested 4 levels", () => "{\"a\":{\"b\":{\"c\":{\"password\":\"" + Secret + "\"}}}}", [Secret]);
        yield return new Probe("duplicate keys", () => $$"""{"password":"{{Secret}}1","password":"{{Secret}}2"}""", [Secret]);
        yield return new Probe("depth 16 objects + secret", () => Nest(15, $$"""{"password":"{{Secret}}"}"""), [Secret]);
        yield return new Probe("depth 17 objects + secret", () => Nest(16, $$"""{"password":"{{Secret}}"}"""), [Secret]);
        yield return new Probe("depth 17 arrays (no secret)", () => new string('[', 17) + new string(']', 17), []);
        yield return new Probe("depth 64 (reader default max)", () => Nest(63, "{}"), []);
        yield return new Probe("depth 10000", () => Nest(9999, "{}"), []);
        yield return new Probe("depth 10000, reader MaxDepth=20000", () => Nest(9999, "{}"), [], new JsonReaderOptions { MaxDepth = 20000 });
        yield return new Probe("5 MB unmasked string value", () => $$"""{"note":"{{new string('x', 5 * 1024 * 1024)}}","password":"{{Secret}}"}""", [Secret]);
        yield return new Probe("5 MB sensitive string value", () => $$"""{"password":"{{Secret}}{{new string('y', 5 * 1024 * 1024)}}"}""", [Secret]);
        yield return new Probe("root primitive string", () => "\"hello\"", []);
        yield return new Probe("root primitive number", () => "123", []);
        yield return new Probe("empty body", () => "", []);
        yield return new Probe("comment, default reader options", () => $$"""{/*c*/"password":"{{Secret}}"}""", [Secret]);
        yield return new Probe("number outside decimal range", () => $$"""{"password":"{{Secret}}","big":1e400}""", [Secret]);
        yield return new Probe("number fidelity 1.50 / 1e2 / -0", () => """{"a":1.50,"b":1e2,"c":-0,"d":12345678901234567890}""", []);
        yield return new Probe("non-ASCII + HTML chars", () => """{"name":"Иван <b>&'+"}""", []);
        yield return new Probe("mask+extract types id/active", () => """{"id":42,"active":true,"password":"p"}""", ["\"p\""]);
    }

    private static string Nest(int levels, string inner)
    {
        var sb = new StringBuilder(levels * 8 + inner.Length);
        for (var i = 0; i < levels; i++)
        {
            sb.Append("{\"a\":");
        }

        sb.Append(inner);
        sb.Append('}', levels);
        return sb.ToString();
    }

    private static Outcome Execute(string observerName, Probe probe, Func<string, JsonReaderOptions, string?> mask)
    {
        var input = probe.Input();
        string? result = null;
        string error = string.Empty;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        try
        {
            result = mask(input, probe.ReaderOptions);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {Shorten(ex.Message, 90)}";
        }

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var leaked = result is not null && probe.Sensitive.Any(s => result.Contains(s, StringComparison.Ordinal));
        var leak = result is null ? "n/a (no output)" : leaked ? "LEAK" : "no";
        return new Outcome(observerName, probe.Name, result is null ? "(none)" : Shorten(result, 160), error, leak, allocated, sw.Elapsed.TotalMilliseconds);
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + $"…(+{value.Length - max})";

    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0.0} KB" : $"{bytes} B";
}
