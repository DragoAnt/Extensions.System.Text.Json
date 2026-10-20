using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using DragoAnt.Observer;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

internal static class MatrixInvariants
{
    private static readonly ConditionalWeakTable<MatrixDocument, Lazy<InputFacts>> InputFactsCache = new();

    internal static IEnumerable<string> Check(MatrixRun run)
    {
        string Failure(string invariant, string detail) =>
            $"{run.Document.Name}/{run.ObserverKey}/{run.ProfileName}/{run.Entry} cut={run.Cut}: {invariant}: {detail}";

        if (run.Thrown is not null)
        {
            yield return Failure("I1", run.Thrown.GetType().Name);
            yield break;
        }

        JsonDocument? outputDocument = null;
        Exception? parseException = null;
        if (run.Output.Length > 0)
        {
            try
            {
                outputDocument = JsonDocument.Parse(run.Output, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 128 });
            }
            catch (Exception exception)
            {
                parseException = exception;
            }
        }
        if (parseException is not null)
        {
            yield return Failure("I2", parseException.Message);
        }

        var text = Encoding.UTF8.GetString(run.Output);
        var leaked = text.Contains(MatrixCorpus.SecretMarker, StringComparison.Ordinal)
            || text.Contains(MatrixCorpus.CommentSecretMarker, StringComparison.Ordinal)
            || text.Contains(MatrixCorpus.NumberSecretMarker, StringComparison.Ordinal)
            || (!run.VariantLeakAllowed && text.Contains(MatrixCorpus.VariantSecretMarker, StringComparison.Ordinal));
        var cutWithoutFlag = false;
        if (outputDocument is not null)
        {
            var facts = Facts(run.Document);
            foreach (var value in Scalars(outputDocument.RootElement))
            {
                leaked |= value.Contains(MatrixCorpus.SecretMarker, StringComparison.Ordinal)
                    || value.Contains(MatrixCorpus.CommentSecretMarker, StringComparison.Ordinal)
                    || (!run.VariantLeakAllowed && value.Contains(MatrixCorpus.VariantSecretMarker, StringComparison.Ordinal));
                leaked |= value != "***" && !facts.Scalars.Contains(value) && value.StartsWith("***", StringComparison.Ordinal) && !facts.Last4Values.Contains(value);
                cutWithoutFlag |= !facts.Scalars.Contains(value) && value.EndsWith('…') && !run.Result.Flags.HasFlag(MaskFlags.ValueCut);
            }
            var requireWholeContainerMask = run.Cut >= run.Document.RootEnd && run.ObserverKey != "allowlist-unmasked";
            leaked |= HasUnmaskedCanonicalSensitiveValue(outputDocument.RootElement, facts.Last4Values, requireWholeContainerMask);
        }
        if (leaked)
        {
            yield return Failure("I3", $"secret marker in output {text}");
        }

        if (outputDocument is not null)
        {
            using (outputDocument)
            {
                var allowed = new HashSet<string>(Facts(run.Document).Scalars, StringComparer.Ordinal);
                allowed.Add("***");
                foreach (var scalar in Scalars(outputDocument.RootElement))
                {
                    if (allowed.Contains(scalar)) continue;
                    if (IsValueCut(scalar, allowed, run)) continue;
                    if (scalar.StartsWith("***", StringComparison.Ordinal)) continue;
                    yield return Failure("I4", $"partial or invented scalar {scalar}");
                }
            }
        }

        var completeAndClean = run.Cut >= run.Document.RootEnd && run.Result.Flags == MaskFlags.None;
        if (cutWithoutFlag
            || (run.Result.Status == MaskStatus.Masked) != completeAndClean
            || (run.Cut < run.Document.RootEnd && run.Result.Status is not (MaskStatus.Truncated or MaskStatus.Unrecognized)))
        {
            yield return Failure("I5", $"status {run.Result.Status}, flags {run.Result.Flags}, root end {run.Document.RootEnd}");
        }

        var strippedOutput = Strip(run.Output);
        if (run.Output.Length > 0 && !Strip(run.FullOutput).AsSpan().StartsWith(strippedOutput))
        {
            yield return Failure("I7", "output is not a prefix of full output after synthesized closers are removed");
        }
        if (run.PreviousOutput is not null && !strippedOutput.AsSpan().StartsWith(Strip(run.PreviousOutput)))
        {
            yield return Failure("I7", "output is not monotone with the preceding cut");
        }

        if (run.Result.FailedAtByte != -1 && (run.Result.FailedAtByte < 0 || run.Result.FailedAtByte > run.Cut))
        {
            yield return Failure("I8", $"FailedAtByte {run.Result.FailedAtByte}");
        }
    }

    private static InputFacts Facts(MatrixDocument document) =>
        InputFactsCache.GetValue(document, static value => new(() => CreateInputFacts(value))).Value;

    private static InputFacts CreateInputFacts(MatrixDocument document)
    {
        using var parsed = JsonDocument.Parse(document.Utf8, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 128,
        });
        var scalars = Scalars(parsed.RootElement).ToHashSet(StringComparer.Ordinal);
        var last4Values = scalars.Select(value => value.EnumerateRunes().ToArray())
            .Where(runes => runes.Length >= 8)
            .Select(runes => "***" + string.Concat(runes[^4..]))
            .ToHashSet(StringComparer.Ordinal);
        return new(scalars, last4Values);
    }

    private static IEnumerable<string> Scalars(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                foreach (var value in Scalars(property.Value)) yield return value;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                foreach (var value in Scalars(item)) yield return value;
                break;
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            default:
                yield return element.GetRawText();
                break;
        }
    }

    private static bool IsValueCut(string value, IReadOnlySet<string> inputScalars, MatrixRun run)
    {
        if (!value.EndsWith('…') || !run.Result.Flags.HasFlag(MaskFlags.ValueCut)) return false;
        var kept = value[..^1];
        return Encoding.UTF8.GetByteCount(kept) <= run.Options.MaxValueBytes
            && inputScalars.Any(input => input.Length > kept.Length && input.StartsWith(kept, StringComparison.Ordinal));
    }

    private static bool HasUnmaskedCanonicalSensitiveValue(JsonElement element, IReadOnlySet<string> last4Values, bool requireWholeContainerMask)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => HasUnmaskedCanonicalSensitiveValue(item, last4Values, requireWholeContainerMask));
        }
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (MatrixCorpus.SensitiveNames.Contains(property.Name))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString()!;
                    if (value != "***" && (property.Name != "card" || !last4Values.Contains(value))) return true;
                }
                else if (property.Value.ValueKind != JsonValueKind.Null
                         && (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) || requireWholeContainerMask))
                {
                    return true;
                }
            }
            if (HasUnmaskedCanonicalSensitiveValue(property.Value, last4Values, requireWholeContainerMask)) return true;
        }
        return false;
    }

    private static byte[] Strip(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] is (byte)'}' or (byte)']' or (byte)' ' or (byte)'\r' or (byte)'\n') length--;
        return bytes.AsSpan(0, length).ToArray();
    }
}

internal sealed record InputFacts(IReadOnlySet<string> Scalars, IReadOnlySet<string> Last4Values);

internal sealed record MatrixRun(
    MatrixDocument Document, string ObserverKey, string ProfileName, JsonObserverOptions Options,
    string Entry, int Cut, MaskResult Result, byte[] Output, Exception? Thrown,
    byte[] FullOutput, bool VariantLeakAllowed, byte[]? PreviousOutput = null);
