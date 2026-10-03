namespace DragoAnt.System.Text.Json.Observer.Skills.Tests;

internal static class SkillDocuments
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "skills");

    /// <summary>Every markdown file under <c>skills/</c>, as a path relative to it with forward slashes.</summary>
    public static IReadOnlyList<string> All { get; } = Directory.Exists(Root)
        ? Directory.GetFiles(Root, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray()
        : [];

    /// <summary>Skill folder names: the directories holding a <c>SKILL.md</c>.</summary>
    public static IReadOnlyList<string> Skills { get; } = All
        .Where(d => d.EndsWith("/SKILL.md", StringComparison.Ordinal))
        .Select(d => d[..d.IndexOf('/')])
        .ToArray();

    public static string[] ReadLines(string document) => File.ReadAllLines(Path.Combine(Root, document));
}
