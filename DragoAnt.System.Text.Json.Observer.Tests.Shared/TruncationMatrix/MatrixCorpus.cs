using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

internal static class MatrixCorpus
{
    internal const string SecretMarker = "ZQX";
    internal const string VariantSecretMarker = "ZQV";
    internal const string CommentSecretMarker = "ZQC";
    internal const string NumberSecretMarker = "731904731904";
    internal const string ExtraSecretMarker = "ZQE";
    internal const int GeneratorSeed = 20261020;
    internal const int GeneratedCount = 40;
    internal static IReadOnlySet<string> SensitiveNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "password", "pin", "driverLicense", "ssn", "token", "card", "blob",
    };

    internal static IReadOnlyDictionary<string, MatrixDocument> Hand { get; } = CreateHand();
    internal static IReadOnlyList<MatrixDocument> Generated { get; } = Generate(GeneratorSeed, GeneratedCount);

    internal static IReadOnlyList<MatrixDocument> Generate(int seed, int count)
    {
        var random = new Random(seed);
        var documents = new List<MatrixDocument>(count);
        for (var i = 0; i < count; i++)
        {
            var rootArray = i % 5 == 0;
            var comments = i % 4 == 0;
            var canonical = new[] { "password", "pin", "driverLicense", "ssn", "token", "card" }[random.Next(6)];
            var variantName = VaryCase(canonical, random);
            var depth = 1 + i % 4;
            var nested = $"{{\"password\":{RawEmoji(SecretMarker + "-nested-é")},\"note\":{Quote("nested-clear")}}}";
            for (var level = 1; level < depth; level++)
            {
                nested = level % 2 == 0 ? $"{{\"nested\":{nested}}}" : $"{{\"items\":[{nested},{{\"name\":{Quote("array-clear")}}}]}}";
            }

            var values = new List<string>
            {
                $"{Quote(canonical)}:{RawEmoji(SecretMarker + $"-{i:D2}-é")}",
                $"{Quote(variantName)}:{Quote(VariantSecretMarker + $"-{i:D2}")}",
                $"\"pin\":{NumberSecretMarker}",
                $"\"id\":{i}",
                $"\"blob\":{{\"note\":{Quote(SecretMarker + "-blob")},\"ok\":true,\"nil\":null}}",
                $"\"name\":{RawEmoji("clear-é")}",
                $"\"naïve\":{Quote("raw-Ж中")}",
                $"\"n\":{random.Next(-1000, 1000).ToString(global::System.Globalization.CultureInfo.InvariantCulture)}",
                $"\"ok\":{(i % 2 == 0 ? "true" : "false")}",
                $"\"extra\":{{\"note\":{Quote(ExtraSecretMarker + "-extra")}}}",
                $"\"nested\":{nested}",
            };

            var body = string.Join(comments ? ",/* generated */" : ",", values);
            var json = rootArray ? $"[{{{body}}},0]" : $"{{{body}}}";
            if (comments)
            {
                json = json.Insert(json.Length - 1, ",");
            }
            documents.Add(Create($"generated-{i:D2}", json, comments, rootArray));
        }
        return documents;
    }

    private static readonly JsonSerializerOptions CorpusJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Quote(string value) => JsonSerializer.Serialize(value, CorpusJson);

    private static string RawEmoji(string value) => $"\"{value}😀\"";

    private static IReadOnlyDictionary<string, MatrixDocument> CreateHand()
    {
        var deep = new StringBuilder();
        for (var i = 0; i < 30; i++) deep.Append("{\"nested\":[");
        deep.Append("{\"pin\":\"ZQX-deep\"}");
        for (var i = 0; i < 30; i++) deep.Append("]}");

        return new Dictionary<string, MatrixDocument>
        {
            ["escapes"] = Create("escapes", """{"note":"\"\\\/\b\f\n\r\t\u00e9","driver\u004Cicense":"ZQX-escape"}"""),
            ["surrogates"] = Create("surrogates", """{"name":"😀 \ud83d\ude00 Кириллица 中文","password":"ZQX-surrogate"}"""),
            ["comments"] = Create("comments", "{/* before */\"password\":\"ZQX-comment\"/*ZQC*/,\"note\":\"ok\"// inline\n}/* tail */", true),
            ["trailing-commas"] = Create("trailing-commas", """{"nested":{"pin":"ZQX-pin",},"list":[1,2,],}"""),
            ["root-array"] = Create("root-array", """[{"driverLicense":"ZQX1"},{"driverLicense":"ZQX2"},3,"s",null]""", rootArray: true),
            ["deep"] = Create("deep", deep.ToString()),
            ["numbers"] = Create("numbers", """{"n":1e400,"pin":-0,"nested":{"n":-0.0},"list":[12345678901234567890,1.5e-3,0]}"""),
            ["literals"] = Create("literals", """{"id":42,"ok":true,"pin":false,"token":null,"list":[true,false,null]}"""),
            ["duplicates"] = Create("duplicates", """{"name":"first","name":"second","password":"ZQX1","password":"ZQX2"}"""),
            ["upper-cased"] = Create("upper-cased", """{"PASSWORD":"ZQV1","Pin":"ZQV2","DRiverLicensE":"ZQV3","SSN":"ZQV4","password":"ZQX1","pin":"ZQX2","driverLicense":"ZQXff\u00e9","ssn":"ZQX4","card":"ZQX12345678"}"""),
        };
    }

    private static MatrixDocument Create(string name, string json, bool comments = false, bool rootArray = false)
    {
        var utf8 = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 128 });
        long rootEnd = 0;
        while (reader.Read())
        {
            if (reader.CurrentDepth == 0 && reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                rootEnd = reader.BytesConsumed;
                break;
            }
        }
        return new MatrixDocument(name, utf8, comments, rootArray, checked((int)rootEnd));
    }

    private static string VaryCase(string value, Random random)
    {
        string variant;
        do
        {
            variant = string.Concat(value.Select(c => char.IsLetter(c) && random.Next(2) == 0 ? char.ToUpperInvariant(c) : c));
        } while (variant == value);
        return variant;
    }
}

internal sealed record MatrixDocument(string Name, byte[] Utf8, bool HasComments, bool RootIsArray, int RootEnd);
