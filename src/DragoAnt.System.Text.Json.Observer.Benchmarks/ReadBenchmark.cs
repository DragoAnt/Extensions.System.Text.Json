using DragoAnt.System.Text.Json.Observer.Tests.Shared;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

[MemoryDiagnoser]
public class ReadBenchmark
{
    private JsonReadTests.ReadContext _context = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        JsonReadTests.BenchmarkRead(new JsonReadTests.ReadContext());
        GC.Collect(2);
        JsonReadTests.BenchmarkRead(new JsonReadTests.ReadContext());
        GC.Collect(2);
        JsonReadTests.BenchmarkRead(new JsonReadTests.ReadContext());
        GC.Collect(2);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _context = new JsonReadTests.ReadContext();
    }

    [Benchmark]
    public void Read()
    {
        JsonReadTests.BenchmarkRead(_context);
    }

    [Benchmark]
    public void Read2()
    {
        JsonReadTests.BenchmarkRead(_context);
    }

    [Benchmark]
    public void Read3()
    {
        JsonReadTests.BenchmarkRead(_context);
    }
}