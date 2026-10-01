using System.Buffers;
using System.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

public sealed class AuditConfig : ManualConfig
{
    public AuditConfig()
    {
        var fast = Environment.GetEnvironmentVariable("AUDIT_FAST") == "1";
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(fast ? 2 : 3)
            .WithIterationCount(fast ? 5 : 8)
            .WithId("Audit"));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(CsvExporter.Default);
        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.Median);
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
        HideColumns("Job", "Toolchain", "LaunchCount", "WarmupCount", "IterationCount");
    }

    public static T[]? FromEnv<T>(string name, Func<string, T> parse)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(parse).ToArray();
    }
}

[Config(typeof(AuditConfig))]
public class MaskBenchmarks
{
    private JsonObserver _observer = null!;
    private string _json = null!;
    private byte[] _utf8 = null!;
    private ArrayBufferWriter<byte> _output = null!;
    private Utf8JsonWriter _writer = null!;

    [ParamsSource(nameof(Shapes))]
    public PayloadShape Shape { get; set; }

    [ParamsSource(nameof(Sizes))]
    public int Size { get; set; }

    public static IEnumerable<PayloadShape> Shapes =>
        AuditConfig.FromEnv("AUDIT_SHAPES", s => Enum.Parse<PayloadShape>(s, ignoreCase: true))
        ?? [PayloadShape.Flat, PayloadShape.Nested, PayloadShape.Array];

    public static IEnumerable<int> Sizes =>
        AuditConfig.FromEnv("AUDIT_SIZES", s => int.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture))
        ?? [1024, 8 * 1024, 64 * 1024];

    [GlobalSetup]
    public void Setup()
    {
        _observer = Baselines.BuildObserver();
        _json = Payloads.Build(Shape, Size);
        _utf8 = Encoding.UTF8.GetBytes(_json);
        _output = new ArrayBufferWriter<byte>(_utf8.Length * 2);
        _writer = new Utf8JsonWriter(_output);

        var masked = _observer.Mask(_json)!;
        if (masked.Contains(Payloads.SensitiveMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Library output leaks a sensitive value; benchmark setup is wrong.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _writer.Dispose();

    [Benchmark(Baseline = true, Description = "LowerBound copy string->string")]
    public string CopyString() => Baselines.CopyString(_json);

    [Benchmark(Description = "LowerBound copy bytes->pooled IBufferWriter")]
    public int CopyBytes()
    {
        ResetOutput();
        Baselines.CopyBytes(_utf8, _output, _writer, maskNames: false);
        return _output.WrittenCount;
    }

    [Benchmark(Description = "Lib JsonObserver.Mask string->string (built once)")]
    public string? Library() => _observer.Mask(_json);

    [Benchmark(Description = "Hand-rolled span masker string->string")]
    public string SpanMasker() => Baselines.SpanMaskString(_json);

    [Benchmark(Description = "Hand-rolled span masker bytes->pooled IBufferWriter")]
    public int SpanMaskerBytes()
    {
        ResetOutput();
        Baselines.CopyBytes(_utf8, _output, _writer, maskNames: true);
        return _output.WrittenCount;
    }

    [Benchmark(Description = "DOM JsonNode.Parse->replace->ToJsonString")]
    public string Dom() => Baselines.Dom(_json);

    [Benchmark(Description = "Regex [GeneratedRegex] over UTF-16")]
    public string Regex() => Baselines.Regex(_json);

    private void ResetOutput()
    {
#if NET8_0_OR_GREATER
        _output.ResetWrittenCount();
#else
        _output.Clear();
#endif
    }
}

[Config(typeof(AuditConfig))]
public class BuildBenchmarks
{
    [Params(3, 8)]
    public int Rules { get; set; }

    [Benchmark(Description = "Build JsonObserver (relative rules)")]
    public JsonObserver Build() => Baselines.BuildObserver(Rules);
}
