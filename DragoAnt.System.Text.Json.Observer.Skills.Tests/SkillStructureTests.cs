using System.Text.RegularExpressions;

namespace DragoAnt.System.Text.Json.Observer.Skills.Tests;

/// <summary>
/// Keeps the skills installable by any agent: open <c>SKILL.md</c> frontmatter only, and every relative link resolves.
/// </summary>
public sealed partial class SkillStructureTests
{
    private static readonly string[] ExpectedSkills = ["json-observer-http-logging", "json-observer-masking", "json-observer-testing"];

    [Fact]
    public void Skills_AreTheExpectedSet() =>
        SkillDocuments.Skills.Order(StringComparer.Ordinal).Should().Equal(ExpectedSkills);

    public static TheoryData<string> SkillNames() => new(ExpectedSkills);

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void SkillMd_HasOnlyNameAndDescriptionFrontmatter(string skill)
    {
        var lines = SkillDocuments.ReadLines($"{skill}/SKILL.md");
        lines[0].Should().Be("---");
        var end = Array.IndexOf(lines, "---", 1);
        end.Should().BeGreaterThan(0, "the frontmatter is closed");

        var fields = lines[1..end].ToDictionary(l => l[..l.IndexOf(':')], l => l[(l.IndexOf(':') + 1)..].Trim());
        fields.Keys.Should().BeEquivalentTo(["name", "description"]);
        fields["name"].Should().Be(skill);
        fields["name"].Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        fields["name"].Length.Should().BeLessThanOrEqualTo(64);
        fields["description"].Length.Should().BeInRange(1, 1024);
        lines.Length.Should().BeLessThan(500, "SKILL.md stays short; detail goes to companions");
    }

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var document in SkillDocuments.All)
        {
            data.Add($"skills/{document}");
        }

        data.Add("docs/skills.md");
        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void RelativeLinks_Resolve(string document)
    {
        var path = Path.Combine(AppContext.BaseDirectory, document);
        var text = File.ReadAllText(path);
        foreach (Match link in RelativeLink().Matches(text))
        {
            var target = link.Groups["target"].Value;
            var file = target.Split('#')[0];
            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, file));
            var inRepo = resolved.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "skills")), StringComparison.OrdinalIgnoreCase)
                || resolved.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "docs")), StringComparison.OrdinalIgnoreCase);
            if (inRepo)
            {
                File.Exists(resolved).Should().BeTrue($"{document} links to {target}");
            }
        }
    }

    [GeneratedRegex(@"\]\((?<target>\.{1,2}/[^)\s]+\.md(#[^)\s]*)?)\)")]
    private static partial Regex RelativeLink();
}
