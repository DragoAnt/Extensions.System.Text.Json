using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DragoAnt.System.Text.Json.Observer.Docs.Tests;

/// <summary>
/// Every <c>```csharp</c> block of the README and the package readme is a complete program: it must compile against the
/// current library, run without throwing, and print what its <c>// Output:</c> comment says.
/// A block preceded by <c>&lt;!-- doc-test: skip --&gt;</c> is a fragment and is not compiled.
/// </summary>
public sealed partial class DocSnippetTests
{
    private static readonly Lazy<MetadataReference[]> References = new(LoadReferences);

    public static TheoryData<string, int> Snippets()
    {
        var data = new TheoryData<string, int>();
        foreach (var document in Documents)
        {
            var count = Extract(document).Count;
            for (var i = 0; i < count; i++)
            {
                data.Add(document, i);
            }
        }

        return data;
    }

    [Fact]
    public void EveryDocument_HasSnippets()
    {
        foreach (var document in Documents)
        {
            Extract(document).Should().NotBeEmpty(document);
        }
    }

    [Theory]
    [MemberData(nameof(Snippets))]
    public void Snippet_CompilesRunsAndPrintsItsOutput(string document, int index)
    {
        var snippet = Extract(document)[index];

        var assembly = Compile(snippet.Code, $"{Path.GetFileNameWithoutExtension(document)}_{index}", out var diagnostics);
        assembly.Should().NotBeNull($"{document} snippet at line {snippet.Line} must compile:{Environment.NewLine}{diagnostics}");

        var printed = Run(assembly!);
        if (snippet.ExpectedOutput is { } expected)
        {
            printed.TrimEnd().Should().Be(expected, $"{document} snippet at line {snippet.Line} prints its // Output: comment");
        }
    }

    private static readonly string[] Documents = ["README.md", "package.readme.md", "migrating-to-2.0.md"];

    private sealed record Snippet(int Line, string Code, string? ExpectedOutput);

    private static List<Snippet> Extract(string document)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "docs", document));
        var snippets = new List<Snippet>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "```csharp")
            {
                continue;
            }

            var skip = i > 0 && lines[i - 1].Contains("doc-test: skip", StringComparison.Ordinal);
            var start = i + 1;
            var end = start;
            while (end < lines.Length && lines[end].Trim() != "```")
            {
                end++;
            }

            if (!skip)
            {
                var code = lines[start..end];
                snippets.Add(new Snippet(start + 1, string.Join('\n', code), ExpectedOutput(code)));
            }

            i = end;
        }

        return snippets;
    }

    private static string? ExpectedOutput(string[] code)
    {
        var at = Array.FindIndex(code, l => l.Trim() == "// Output:");
        if (at < 0)
        {
            return null;
        }

        var output = code.Skip(at + 1).TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(l => l.TrimStart()[2..].TrimStart());
        return string.Join('\n', output);
    }

    private static Assembly? Compile(string code, string name, out string diagnostics)
    {
        var tree = CSharpSyntaxTree.ParseText(GlobalUsings + code, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            name,
            [tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
        return result.Success ? Assembly.Load(stream.ToArray()) : null;
    }

    private static string Run(Assembly assembly)
    {
        var entry = assembly.EntryPoint!;
        var original = Console.Out;
        var printed = new StringWriter();
        Console.SetOut(printed);
        try
        {
            var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        return NewLines().Replace(printed.ToString(), "\n");
    }

    private const string GlobalUsings = "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\nglobal using System.Threading.Tasks;\nglobal using DragoAnt.Observer;\n";

    private static MetadataReference[] LoadReferences()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var local = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        return platform.Concat(local)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First()))
            .ToArray();
    }

    [GeneratedRegex("\r\n?")]
    private static partial Regex NewLines();
}
