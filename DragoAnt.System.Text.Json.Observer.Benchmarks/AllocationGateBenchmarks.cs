using System.Buffers;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllocationBudgetAttribute(long bytes) : Attribute
{
    public long Bytes { get; } = bytes;

    public long AtLeast { get; init; }
}

public sealed class AllocationGateConfig : ManualConfig
{
    public AllocationGateConfig()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithStrategy(RunStrategy.Throughput)
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(3)
            .WithId("AllocationGate"));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(new AllocationGateExporter());
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}

public sealed class AllocationGateExporter : IExporter
{
    // Measured meter noise is 2-4 B per call at 64 KB; a real allocation is at least one 24 B object.
    private const long Noise = 23;

    public string Name => "allocation-gate";

    public void ExportToLog(Summary summary, ILogger logger)
    {
    }

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger consoleLogger)
    {
        var lines = new List<string> { "## Allocation gate (net10.0, BenchmarkDotNet)", "", "| Benchmark | Allocated / op | Budget | Result |", "| --- | ---: | ---: | --- |" };
        var failed = summary.Reports.Length == 0;
        foreach (var report in summary.Reports)
        {
            var budget = report.BenchmarkCase.Descriptor.WorkloadMethod.GetCustomAttribute<AllocationBudgetAttribute>();
            if (budget is null)
            {
                failed = true;
                var missing = $"| {report.BenchmarkCase.Descriptor.WorkloadMethod.Name} | n/a | no [AllocationBudget] | FAIL |";
                lines.Add(missing);
                consoleLogger.WriteLine(LogKind.Error, missing);
                continue;
            }

            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
            var pass = report.Success && allocated is { } bytes && bytes <= budget.Bytes + Noise && bytes >= budget.AtLeast;
            failed |= !pass;
            var line = $"| {report.BenchmarkCase.Descriptor.WorkloadMethod.Name} {report.BenchmarkCase.Parameters.DisplayInfo} | {allocated?.ToString() ?? "n/a"} B | {budget.Bytes} B | {(pass ? "pass" : "FAIL")} |";
            lines.Add(line);
            consoleLogger.WriteLine(pass ? LogKind.Result : LogKind.Error, line);
        }

        if (failed)
        {
            Environment.ExitCode = 1;
        }

        var path = Path.Combine(summary.ResultsDirectoryPath, "allocation-gate.md");
        File.WriteAllLines(path, lines);
        return [path];
    }
}

[Config(typeof(AllocationGateConfig))]
public class AllocationGateBenchmarks
{
    private static readonly JsonObserverOptions Keyed = new JsonObserverOptions { HashKeyId = 3 }
        .WithBase64HashKey("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==");

    private JsonObserver _constant = null!;
    private JsonObserver _tags = null!;
    private string _json = null!;
    private byte[] _utf8 = null!;
    private ArrayBufferWriter<byte> _bytes = null!;
    private ArrayBufferWriter<char> _chars = null!;

    [Params(1024, 64 * 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _constant = JsonObserver.Obj(AnyDepth(b =>
        {
            foreach (var name in Payloads.SensitiveNames)
            {
                b.Match(name).Mask("***");
            }
        }, BlockList));
        _tags = JsonObserver.Obj(AnyDepth(b =>
        {
            foreach (var name in Payloads.SensitiveNames)
            {
                b.Match(name).Mask(name == "cardNumber" ? MaskTag.Last4 : MaskTag.Hash);
            }
        }, BlockList));
        _json = Payloads.Build(PayloadShape.Flat, Size);
        _utf8 = Encoding.UTF8.GetBytes(_json);
        _bytes = new ArrayBufferWriter<byte>(_utf8.Length * 2);
        _chars = new ArrayBufferWriter<char>(_json.Length * 2);

        if (_constant.Mask(_json)!.Contains(Payloads.SensitiveMarker, StringComparison.Ordinal)
            || _tags.Mask(_json, Keyed)!.Contains(Payloads.SensitiveMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A gate observer leaks a sensitive value; the benchmark setup is wrong.");
        }
    }

    [Benchmark]
    [AllocationBudget(0)]
    public long ConstantBytes()
    {
        _bytes.ResetWrittenCount();
        return _constant.Mask(_utf8, _bytes).BytesWritten;
    }

    [Benchmark]
    [AllocationBudget(0)]
    public long TagHashBytes()
    {
        _bytes.ResetWrittenCount();
        return _tags.Mask(_utf8, _bytes, Keyed).BytesWritten;
    }

    [Benchmark]
    [AllocationBudget(0)]
    public long ConstantChars()
    {
        _chars.ResetWrittenCount();
        return _constant.Mask(_json.AsSpan(), _chars).BytesWritten;
    }

    [Benchmark]
    [AllocationBudget(32, AtLeast = 24)]
    public object Control() => new object[1];
}
