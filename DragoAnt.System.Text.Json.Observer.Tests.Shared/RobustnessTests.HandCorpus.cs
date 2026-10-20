using System.Text;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract partial class RobustnessTests
{
    private static readonly NameMatch AnyCorpusItem = new(_ => true);
    private static readonly JsonObserver HandCorpusObserver = JsonObserver.Any(
        _ => { }, _ => { },
        AnyDepth(b => b
            .Match(Names.OneOf("pin", "driverLicense", "card")).Mask("***")
            .Path("nested", "secret").Mask("***")
            .Path("list", AnyCorpusItem, "ssn").Mask("***")
            .Match("blob").Mask("***"), BlockList));

    private static readonly IReadOnlyDictionary<string, HandCase> HandCases = CreateHandCases();

    [Theory]
    [InlineData("inside-key")]
    [InlineData("after-key")]
    [InlineData("after-colon")]
    [InlineData("inside-sensitive")]
    [InlineData("inside-escape")]
    [InlineData("inside-number")]
    [InlineData("inside-literal")]
    [InlineData("inside-nested")]
    [InlineData("inside-masked-container")]
    [InlineData("array-of-objects")]
    [InlineData("root-array-second")]
    [InlineData("plain-string-cut")]
    [InlineData("utf8-cut-mid-char")]
    [InlineData("trailing-data")]
    [InlineData("invalid-utf8-in-value")]
    public void TruncatedInput_HandCorpus_MatchesTable(string name)
    {
        var item = HandCases[name];
        var (result, output) = BytesApiTests.Mask(HandCorpusObserver, item.Input);
        output.Should().Be(item.Output, name);
        result.Status.Should().Be(item.Status, name);
        result.Flags.Should().Be(item.Flags, name);
        result.FailedAtByte.Should().Be(item.FailedAtByte, name);

        var outputBytes = Encoding.UTF8.GetBytes(item.Output);
        var invariantInput = CompletedHandInput(name);
        var (_, fullOutput) = BytesApiTests.Mask(HandCorpusObserver, invariantInput);
        var fullOutputBytes = name == "invalid-utf8-in-value" ? outputBytes : Encoding.UTF8.GetBytes(fullOutput);
        var invariantDocument = new MatrixDocument($"hand-{name}", invariantInput, false, invariantInput[0] == (byte)'[', invariantInput.Length);
        var failures = MatrixInvariants.Check(new MatrixRun(invariantDocument, "hand-corpus", "default", new(), "span",
            item.Input.Length, result, outputBytes, null, fullOutputBytes, false));
        failures.Should().BeEmpty($"the hand case {name} must satisfy the shared matrix invariants");
    }

    private static byte[] CompletedHandInput(string name) => Encoding.UTF8.GetBytes(name switch
    {
        "inside-key" or "after-key" or "after-colon" => "{\"driverLicense\":\"value\"}",
        "inside-sensitive" => "{\"driverLicense\":\"ffff\"}",
        "inside-escape" => "{\"driverLicense\":\"ff\\u0000\"}",
        "inside-number" => "{\"name\":\"Bob\",\"pin\":98}",
        "inside-literal" => "{\"name\":\"Bob\",\"ok\":true}",
        "inside-nested" => "{\"nested\":{\"secret\":\"NS\"}}",
        "inside-masked-container" => "{\"blob\":{\"x\":\"BLOB\"}}",
        "array-of-objects" => "{\"list\":[{\"ssn\":\"SS\"}]}",
        "root-array-second" => "[{\"driverLicense\":\"DL\"},{\"driverLicense\":\"D\"}]",
        "plain-string-cut" => "{\"name\":\"Bob\",\"note\":\"hello\"}",
        "utf8-cut-mid-char" => "{\"name\":\"Ив\"}",
        "trailing-data" => "{\"name\":\"Bob\"}",
        "invalid-utf8-in-value" => "{\"note\":\"a\uFFFDb\"}",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    });

    private static IReadOnlyDictionary<string, HandCase> CreateHandCases()
    {
        static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
        var invalid = Utf8("{\"note\":\"a").Concat(new byte[] { 0xC3 }).Concat(Utf8("b\"}")).ToArray();
        var midChar = Utf8("{\"name\":\"Ив")[..^1];
        return new Dictionary<string, HandCase>
        {
            ["inside-key"] = new(Utf8("{\"driverLic"), "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 1),
            ["after-key"] = new(Utf8("{\"driverLicense\""), "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 1),
            ["after-colon"] = new(Utf8("{\"driverLicense\":"), "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 17),
            ["inside-sensitive"] = new(Utf8("{\"driverLicense\":\"ffff"), "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 17),
            ["inside-escape"] = new(Utf8("{\"driverLicense\":\"ff\\u00"), "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 17),
            ["inside-number"] = new(Utf8("{\"name\":\"Bob\",\"pin\":98"), "{\"name\":\"Bob\"}", MaskStatus.Truncated, MaskFlags.InputTruncated, 20),
            ["inside-literal"] = new(Utf8("{\"name\":\"Bob\",\"ok\":tru"), "{\"name\":\"Bob\"}", MaskStatus.Truncated, MaskFlags.InputTruncated, 19),
            ["inside-nested"] = new(Utf8("{\"nested\":{\"secret\":\"NS"), "{\"nested\":{}}", MaskStatus.Truncated, MaskFlags.InputTruncated, 20),
            ["inside-masked-container"] = new(Utf8("{\"blob\":{\"x\":\"BLOB"), "{\"blob\":\"***\"}", MaskStatus.Truncated, MaskFlags.InputTruncated, 9),
            ["array-of-objects"] = new(Utf8("{\"list\":[{\"ssn\":\"SS"), "{\"list\":[{}]}", MaskStatus.Truncated, MaskFlags.InputTruncated, 16),
            ["root-array-second"] = new(Utf8("[{\"driverLicense\":\"DL\"},{\"driverLicense\":\"D"), "[{\"driverLicense\":\"***\"},{}]", MaskStatus.Truncated, MaskFlags.InputTruncated, 41),
            ["plain-string-cut"] = new(Utf8("{\"name\":\"Bob\",\"note\":\"hel"), "{\"name\":\"Bob\"}", MaskStatus.Truncated, MaskFlags.InputTruncated, 21),
            ["utf8-cut-mid-char"] = new(midChar, "{}", MaskStatus.Truncated, MaskFlags.InputTruncated, 8),
            ["trailing-data"] = new(Utf8("{\"name\":\"Bob\"}{\"pin\":1}"), "{\"name\":\"Bob\"}", MaskStatus.Truncated, MaskFlags.TrailingData, -1),
            ["invalid-utf8-in-value"] = new(invalid, "{\"note\":\"a\\uFFFDb\"}", MaskStatus.Truncated, MaskFlags.InvalidUtf8Replaced, -1),
        };
    }

    private sealed record HandCase(byte[] Input, string Output, MaskStatus Status, MaskFlags Flags, long FailedAtByte);
}
