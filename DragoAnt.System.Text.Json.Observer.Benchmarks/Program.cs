using DragoAnt.System.Text.Json.Observer.Benchmarks;
using DragoAnt.System.Text.Json.Observer.Benchmarks.Harness;

switch (args.FirstOrDefault())
{
    case "probes":
        var thread = new Thread(() => Probes.Run(args.Skip(1).FirstOrDefault()), 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        break;
    case "soak":
        Soak.Run(args.Skip(1).ToArray());
        break;
#if NET10_0_OR_GREATER
    case "redox-probes":
        Probes.RunRedox();
        break;
#endif
    default:
        BenchmarkSwitcher.FromAssembly(typeof(MaskBenchmarks).Assembly).Run(args);
        break;
}
