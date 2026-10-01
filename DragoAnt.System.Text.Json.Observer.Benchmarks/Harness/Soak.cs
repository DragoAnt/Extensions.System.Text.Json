using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks.Harness;

public static class Soak
{
    private const int Checkpoints = 10;

    public static void Run(string[] args)
    {
        var total = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 5_000_000;
        var threads = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 8;
        var output = args.Length > 2 ? args[2] : null;
        var size = 1024;

        var report = new StringBuilder();
        report.AppendLine($"Soak: total={total:N0} ops per phase, threads={threads}, payload=Nested ~{size} B, Server GC={(global::System.Runtime.GCSettings.IsServerGC)}, CPUs={Environment.ProcessorCount}");
        report.AppendLine();

        var observer = Baselines.BuildObserver();
        var variants = Enumerable.Range(0, 16).Select(seed => Payloads.Build(PayloadShape.Nested, size, seed)).ToArray();
        var expected = variants.Select(v => observer.Mask(v)!).ToArray();
        foreach (var e in expected)
        {
            if (e.Contains(Payloads.SensitiveMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected output leaks a sensitive value.");
            }
        }

        for (var i = 0; i < 100_000; i++)
        {
            observer.Mask(variants[i & 15]);
        }

        report.AppendLine("### Single thread, one built observer");
        RunPhase(report, total, (from, to) =>
        {
            var mismatches = 0;
            for (var i = from; i < to; i++)
            {
                if (!string.Equals(observer.Mask(variants[i & 15]), expected[i & 15], StringComparison.Ordinal))
                {
                    mismatches++;
                }
            }

            return mismatches;
        });

        report.AppendLine($"### {threads} threads sharing ONE built observer");
        RunPhase(report, total, (from, to) =>
        {
            var mismatches = 0;
            Parallel.For(from, to, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => 0, (i, _, local) =>
            {
                var idx = (int)((uint)(i * 2654435761) & 15);
                return string.Equals(observer.Mask(variants[idx]), expected[idx], StringComparison.Ordinal) ? local : local + 1;
            }, local => Interlocked.Add(ref mismatches, local));
            return mismatches;
        });

        var failureOps = Math.Max(total / 10, 10_000);
        report.AppendLine($"### Single thread, 10% truncated/invalid payloads (exception path), {failureOps:N0} ops");
        var truncated = variants.Select(v => v[..(v.Length * 2 / 3)]).ToArray();
        var thrown = 0L;
        RunPhase(report, failureOps, (from, to) =>
        {
            var mismatches = 0;
            for (var i = from; i < to; i++)
            {
                if (i % 10 == 0)
                {
                    try
                    {
                        observer.Mask(truncated[i & 15]);
                    }
                    catch (Exception)
                    {
                        thrown++;
                    }
                }
                else if (!string.Equals(observer.Mask(variants[i & 15]), expected[i & 15], StringComparison.Ordinal))
                {
                    mismatches++;
                }
            }

            return mismatches;
        });
        report.AppendLine($"Exceptions thrown on truncated input: {thrown:N0}");
        report.AppendLine();

        var builds = 100_000;
        report.AppendLine($"### Build {builds:N0} observers with distinct rule sets (static cache growth check)");
        var small = """{"id":1,"name":"x","fieldA":"v","fieldB":"w"}""";
        RunPhase(report, builds, (from, to) =>
        {
            var mismatches = 0;
            for (var i = from; i < to; i++)
            {
                var names = new[] { $"field{i}a", $"field{i}b", $"field{i}c", "fieldA" };
                var o = Baselines.BuildObserver(4, names);
                if (o.Mask(small)!.Contains("\"v\"", StringComparison.Ordinal))
                {
                    mismatches++;
                }
            }

            return mismatches;
        });

        Console.WriteLine(report.ToString());
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllText(output, report.ToString());
        }
    }

    private static void RunPhase(StringBuilder report, int total, Func<int, int, int> work)
    {
        report.AppendLine("| Checkpoint | Ops done | GetTotalMemory(true) | GC heap size | Alloc/op | Gen0 | Gen1 | Gen2 | ops/s (chunk) | Mismatches |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var chunk = total / Checkpoints;
        var allocStart = GC.GetTotalAllocatedBytes(true);
        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);
        report.AppendLine($"| start | 0 | {Mb(GC.GetTotalMemory(true))} | {Mb(GC.GetGCMemoryInfo().HeapSizeBytes)} | - | 0 | 0 | 0 | - | - |");
        var totalMismatches = 0;
        for (var c = 0; c < Checkpoints; c++)
        {
            var from = c * chunk;
            var to = c == Checkpoints - 1 ? total : from + chunk;
            var allocBefore = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            var mismatches = work(from, to);
            sw.Stop();
            var allocAfter = GC.GetTotalAllocatedBytes(true);
            totalMismatches += mismatches;
            var ops = to - from;
            var heldBytes = GC.GetTotalMemory(true);
            report.AppendLine(
                $"| {c + 1} | {to:N0} | {Mb(heldBytes)} | {Mb(GC.GetGCMemoryInfo().HeapSizeBytes)} | {(allocAfter - allocBefore) / (double)ops:N0} B | {GC.CollectionCount(0) - g0} | {GC.CollectionCount(1) - g1} | {GC.CollectionCount(2) - g2} | {ops / sw.Elapsed.TotalSeconds:N0} | {mismatches} |");
        }

        var allocTotal = GC.GetTotalAllocatedBytes(true) - allocStart;
        report.AppendLine();
        report.AppendLine($"Total allocated: {Mb(allocTotal)}, mean {allocTotal / (double)total:N0} B/op, mismatches: {totalMismatches}");
        report.AppendLine();
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.00} MB";
}
