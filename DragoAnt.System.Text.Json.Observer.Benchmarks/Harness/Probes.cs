using System.Diagnostics;
using System.Text;

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

    private static Func<string, JsonReaderOptions, string?> Wrap(JsonObserver observer) => (json, options) => observer.Mask(json, options);

    private static string? MaskAndExtract(string json, JsonReaderOptions options)
    {
        var observer = JsonObserver.Obj<ProbeContext>(b => b
                .Match("id").ReadInt((v, c) => c.Id = v)
                .Match("active").ReadBool((v, c) => c.Active = v),
            JsonObserverValuePolicies<ProbeContext>.Relative(b => b
                    .Match("password").MaskStr((_, _) => "***")
                    .Match("pin").MaskStr((_, _) => "***"),
                JsonObserverValuePolicies<ProbeContext>.BlockList));
        return observer.Mask(json, new ProbeContext(), options);
    }

    private sealed class ProbeContext
    {
        public int? Id { get; set; }
        public bool? Active { get; set; }
    }

    private static JsonObserver BuildObserver(bool raw)
    {
        var policy = JsonObserverValuePolicies.Relative(b =>
            {
                foreach (var name in new[] { "password", "pin" })
                {
                    if (raw)
                    {
                        b.Match(name).MaskRawValue((_, _) => "***");
                    }
                    else
                    {
                        b.Match(name).MaskStr((_, _) => "***");
                    }
                }
            },
            JsonObserverValuePolicies.BlockList);
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
