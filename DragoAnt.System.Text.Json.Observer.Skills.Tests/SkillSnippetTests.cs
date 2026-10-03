using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DragoAnt.System.Text.Json.Observer.Skills.Tests;

/// <summary>
/// Every <c>```csharp</c> block under <c>skills/</c> is compiled against the current libraries and run.
/// A block with <c>[Fact]</c> or <c>[Theory]</c> is an xUnit test class: each test method runs and must pass.
/// Any other block is a program: it must run without throwing and print what its <c>// Output:</c> comment says.
/// A block preceded by <c>&lt;!-- doc-test: skip --&gt;</c> is a fragment and is not compiled.
/// </summary>
public sealed partial class SkillSnippetTests
{
    private static readonly Lazy<MetadataReference[]> References = new(LoadReferences);

    public static TheoryData<string, int> Snippets()
    {
        var data = new TheoryData<string, int>();
        foreach (var document in SkillDocuments.All)
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
    public void EverySkill_HasRunnableSnippets()
    {
        foreach (var skill in SkillDocuments.Skills)
        {
            SkillDocuments.All.Where(d => d.StartsWith(skill + "/", StringComparison.Ordinal))
                .Sum(d => Extract(d).Count)
                .Should().BeGreaterThan(0, skill);
        }
    }

    [Theory]
    [MemberData(nameof(Snippets))]
    public async Task Snippet_CompilesAndRuns(string document, int index)
    {
        var snippet = Extract(document)[index];
        var where = $"{document} snippet at line {snippet.Line}";

        var assembly = Compile(snippet, $"{document.Replace('/', '_').Replace('.', '_').Replace('-', '_')}_{index}", out var diagnostics);
        assembly.Should().NotBeNull($"{where} must compile:{Environment.NewLine}{diagnostics}");

        if (snippet.IsTestClass)
        {
            var ran = await RunTests(assembly!, where);
            ran.Should().BeGreaterThan(0, $"{where} declares at least one test");
            return;
        }

        var printed = await RunProgram(assembly!);
        if (snippet.ExpectedOutput is { } expected)
        {
            printed.TrimEnd().Should().Be(expected, $"{where} prints its // Output: comment");
        }
    }

    private sealed record Snippet(int Line, string Code, string? ExpectedOutput, bool IsTestClass);

    private static List<Snippet> Extract(string document)
    {
        var lines = SkillDocuments.ReadLines(document);
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
                var isTestClass = code.Any(l => TestAttribute().IsMatch(l));
                snippets.Add(new Snippet(start + 1, string.Join('\n', code), isTestClass ? null : ExpectedOutput(code), isTestClass));
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

    private static Assembly? Compile(Snippet snippet, string name, out string diagnostics)
    {
        var tree = CSharpSyntaxTree.ParseText(ImplicitUsings + snippet.Code, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            name,
            [tree],
            References.Value,
            new CSharpCompilationOptions(
                snippet.IsTestClass ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
        return result.Success ? Assembly.Load(stream.ToArray()) : null;
    }

    private static async Task<string> RunProgram(Assembly assembly)
    {
        var entry = assembly.EntryPoint!;
        var original = Console.Out;
        var printed = new StringWriter();
        Console.SetOut(printed);
        try
        {
            await Await(Invoke(entry, null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]));
        }
        finally
        {
            Console.SetOut(original);
        }

        return NewLines().Replace(printed.ToString(), "\n");
    }

    private static async Task<int> RunTests(Assembly assembly, string where)
    {
        var ran = 0;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attributes = method.GetCustomAttributes().ToList();
                var test = attributes.FirstOrDefault(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute");
                if (test is null || test.GetType().GetProperty("Skip")?.GetValue(test) is string)
                {
                    continue;
                }

                List<object?[]?> rows = test.GetType().Name == "TheoryAttribute"
                    ? attributes.Where(a => a.GetType().Name == "InlineDataAttribute")
                        .Select(a => (object?[]?)a.GetType().GetProperty("Data")!.GetValue(a))
                        .ToList()
                    : [null];
                rows.Should().NotBeEmpty($"{where}: {type.Name}.{method.Name} is a theory, so it needs [InlineData] rows");

                foreach (var row in rows)
                {
                    var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                    try
                    {
                        await Await(Invoke(method, instance, row));
                        ran++;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"{where}: {type.Name}.{method.Name}({string.Join(", ", row ?? [])}) failed: {ex.Message}", ex);
                    }
                    finally
                    {
                        switch (instance)
                        {
                            case IAsyncDisposable asyncDisposable:
                                await asyncDisposable.DisposeAsync();
                                break;
                            case IDisposable disposable:
                                disposable.Dispose();
                                break;
                        }
                    }
                }
            }
        }

        return ran;
    }

    private static object? Invoke(MethodInfo method, object? instance, object?[]? arguments)
    {
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static async Task Await(object? result)
    {
        switch (result)
        {
            case Task task:
                await task;
                break;
            case ValueTask valueTask:
                await valueTask;
                break;
        }
    }

    private const string ImplicitUsings =
        "global using System;\nglobal using System.Collections.Generic;\nglobal using System.IO;\nglobal using System.Linq;\n" +
        "global using System.Net.Http;\nglobal using System.Threading;\nglobal using System.Threading.Tasks;\n";

    private static MetadataReference[] LoadReferences()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var local = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        return platform.Concat(local)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First()))
            .ToArray();
    }

    [GeneratedRegex(@"^\s*\[(Fact|Theory)\b")]
    private static partial Regex TestAttribute();

    [GeneratedRegex("\r\n?")]
    private static partial Regex NewLines();
}
