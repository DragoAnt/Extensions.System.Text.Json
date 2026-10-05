using System.Buffers;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class CommentTests
{
    private const string Service = """
        { // service account
          "user": "svc-orders",
          "password": "hunter2", // rotated 2026-09
          /* timeouts in seconds */
          "timeout": 30 // TODO: lower
        }
        """;

    private static readonly JsonObserver Plain = JsonObserver.Obj(AnyDepth(b => b.Match("password").Mask(MaskTag.Full), BlockList));

    private static readonly JsonObserver WithRules = JsonObserver.Obj(AnyDepth(b => b
            .Match("password").Mask(MaskTag.Full).Comment(CommentKind.Any, CommentRules.Drop)
            .Match("timeout").Unmasked().Comment(CommentKind.Inline, (ref CommentContext c) =>
            {
                if (c.Text.TrimStart(" "u8).StartsWith("TODO"u8))
                {
                    c.Drop();
                }
                else
                {
                    c.Keep();
                }
            }),
        BlockList));

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    private static JsonObserverOptions Policy(CommentPolicy policy) => new() { Comments = policy };

    private static string Parse(string output)
    {
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return JsonSerializer.Serialize(document.RootElement);
    }

    [Fact]
    public void Default_DropsEveryComment()
        => Plain.Mask(Service).Should().Be("""{"user":"svc-orders","password":"***","timeout":30}""");

    [Fact]
    public void BlockList_KeepsComments_AndMasksTheMaskedOwners()
        => Plain.Mask(Service, Policy(CommentPolicy.BlockList))
            .Should().Be("""{/* service account*/"user":"svc-orders","password":"***"/*****//* timeouts in seconds */,"timeout":30/* TODO: lower*/}""");

    [Fact]
    public void RulesOverrideThePolicy()
        => WithRules.Mask(Service, Policy(CommentPolicy.BlockList))
            .Should().Be("""{/* service account*/"user":"svc-orders","password":"***"/* timeouts in seconds */,"timeout":30}""");

    [Fact]
    public void RulesKeepUnderTheDefaultPolicy()
    {
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("timeout").Unmasked().Comment(CommentKind.Any, CommentRules.Keep), BlockList));

        observer.Mask(Service).Should().Be("""{"user":"svc-orders","password":"hunter2"/* timeouts in seconds */,"timeout":30/* TODO: lower*/}""");
    }

    [Fact]
    public void MaskedOwner_KeepMasks_RawKeepsClear()
    {
        const string json = """{"password":"hunter2" /* was hunter1 */}""";
        var keep = JsonObserver.Obj(b => b.Match("password").Mask(MaskTag.Full).Comment(CommentKind.Inline, CommentRules.Keep), BlockList);
        var raw = JsonObserver.Obj(b => b.Match("password").Mask(MaskTag.Full).Comment(CommentKind.Inline, CommentRules.Raw), BlockList);

        keep.Mask(json).Should().Be("""{"password":"***"/*****/}""");
        raw.Mask(json).Should().Be("""{"password":"***"/* was hunter1 */}""");
    }

    [Fact]
    public void MaskAll_And_MaskWithATag()
    {
        var json = Lines("""{"a":1 // note""", "}");

        Plain.Mask(json, Policy(CommentPolicy.MaskAll)).Should().Be("""{"a":1/*****/}""");
        Plain.Mask(json, Policy(CommentPolicy.Mask(MaskTag.Last4))).Should().Be("""{"a":1/*****/}""");
        Plain.Mask(Lines("""{"a":1 // a longer note""", "}"), Policy(CommentPolicy.Mask(MaskTag.Last4))).Should().Be("""{"a":1/****note*/}""");
    }

    [Fact]
    public void DropAll_IgnoresRules()
        => WithRules.Mask(Service, Policy(CommentPolicy.DropAll)).Should().Be("""{"user":"svc-orders","password":"***","timeout":30}""");

    [Fact]
    public void Replace_WritesTheNewText()
    {
        var observer = JsonObserver.Obj(b => b.Match("a").Unmasked().Comment(CommentKind.Any, (ref CommentContext c) => c.Replace("checked")), BlockList);

        observer.Mask("""{/* x */"a":1}""").Should().Be("""{/*checked*/"a":1}""");
    }

    [Fact]
    public void Ownership_FollowsTheLines()
    {
        var kinds = new List<string>();
        CommentRule record = (ref CommentContext c) =>
        {
            kinds.Add($"{c.Kind} {c.Owner.ToString()} {Encoding.UTF8.GetString(c.Text).Trim()}");
            c.Drop();
        };
        var observer = JsonObserver.Any(
            o => o.Match("a").Unmasked().Comment(CommentKind.Any, record).Match("b").Unmasked().Comment(CommentKind.Any, record),
            l => l.Unmasked().Comment(CommentKind.Any, record),
            BlockList);

        observer.Mask("""
            {
              // before a
              "a": /* between */ 1, // inline a
              // before b
              "b": 2
              // after the last member
            }
            """, Policy(CommentPolicy.BlockList));
        observer.Mask(Lines("""[1, /* inline 0 */ 2 // inline 1""", "]"), Policy(CommentPolicy.BlockList));

        kinds.Should().Equal(
            "Before a before a",
            "Before a between",
            "Inline a inline a",
            "Before b before b",
            "Inline [0] inline 0",
            "Inline [1] inline 1");
    }

    [Fact]
    public void ContainerAndRootComments_AreAfterAndBefore()
    {
        var output = Plain.Mask("""
            // head
            {"a":{"b":1
            /* end of a */}} // tail
            // after

            """, Policy(CommentPolicy.BlockList));

        output.Should().Be("""/* head*/{"a":{"b":1/* end of a */}}/* tail*//* after*/""");
        Parse(output!).Should().Be("""{"a":{"b":1}}""");
    }

    [Fact]
    public void CommentsInArraysAndNestedRules()
    {
        var observer = JsonObserver.Obj(b => b.Match("lines").Array(l => l.Obj(x => x.Match("qty").Unmasked())), BlockList);

        observer.Mask("""{"lines":[/* first */{"qty":1 /* one */},{"qty":2}]}""", Policy(CommentPolicy.BlockList))
            .Should().Be("""{"lines":[/* first */{"qty":1/* one */},{"qty":2}]}""");
    }

    [Fact]
    public void ShapeObserver_FollowsThePolicy()
    {
        var shape = JsonShape.Object(("id", JsonShape.Scalar), ("secret", JsonShape.Masked(MaskTag.Full)));
        var observer = JsonObserver.FromShape(shape);
        var json = Lines("""{"id":1, /* the id */ "secret":"x" // was y""", "}");

        observer.Mask(json).Should().Be("""{"id":1,"secret":"***"}""");
        observer.Mask(json, Policy(CommentPolicy.BlockList)).Should().Be("""{"id":1/* the id */,"secret":"***"/*****/}""");
    }

    [Fact]
    public void CommentTerminatorInside_IsDefused()
    {
        var output = Plain.Mask(Lines("""{"a":1 // x */ y""", "}"), Policy(CommentPolicy.BlockList));

        output.Should().Be("""{"a":1/* x * / y*/}""");
        Parse(output!).Should().Be("""{"a":1}""");
    }

    [Fact]
    public void SequenceInput_SplitAnywhere_SameOutput()
    {
        var utf8 = Encoding.UTF8.GetBytes(Service);
        var options = Policy(CommentPolicy.BlockList);
        var expected = Plain.Mask(Service, options);
        for (var split = 1; split < utf8.Length; split++)
        {
            var first = new Segment(utf8.AsMemory(0, split));
            var last = first.Append(utf8.AsMemory(split));
            var output = new ArrayBufferWriter<byte>();
            Plain.Mask(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), output, options);
            Encoding.UTF8.GetString(output.WrittenSpan).Should().Be(expected, $"split at {split}");
        }
    }

    [Fact]
    public void OutputCap_CountsComments_AndStaysParseable()
    {
        for (var cap = 0; cap < 80; cap++)
        {
            var output = Plain.Mask(Service, out var result, new JsonObserverOptions { Comments = CommentPolicy.BlockList, MaxOutputBytes = cap })!;
            Encoding.UTF8.GetByteCount(output).Should().BeLessThanOrEqualTo(Math.Max(cap, 0) == 0 ? 0 : cap);
            if (output.Length > 0)
            {
                Parse(output).Should().NotBeNull();
            }

            result.Status.Should().BeOneOf(MaskStatus.Masked, MaskStatus.Truncated);
        }
    }

    [Theory]
    [InlineData("""{"password":"hunter2" /* hunter2 */}""")]
    [InlineData("""{/* hunter2 */"password":"hunter2"}""")]
    [InlineData("""{"password": /* hunter2 */ "hunter2"}""")]
    [InlineData("""{"x":{"password":"hunter2"} // hunter2|}""")]
    [InlineData("""// hunter2|{"password":"hunter2"}""")]
    public void SecretInAComment_NeverLeaksUnderTheDefault(string lines)
    {
        var json = lines.Replace('|', '\n');
        foreach (var observer in new[] { Plain, WithRules, JsonObserver.Obj(_ => { }) })
        {
            observer.Mask(json).Should().NotContain("hunter2");
            observer.Mask(json, Policy(CommentPolicy.MaskAll)).Should().NotContain("hunter2");
        }
    }

    [Fact]
    public void Read_IgnoresComments()
        => JsonObserver.Obj<NoContext>(b => b.Match("a").Unmasked().Comment(CommentKind.Any, CommentRules.Keep))
            .Read("""{/*x*/"a":1}""", NoContext.Instance).Status.Should().Be(MaskStatus.Masked);

    [Fact]
    public void Comment_WithoutARule_Throws()
        => FluentActions.Invoking(() => JsonObserver.Obj(b => b.Comment(CommentKind.Any, CommentRules.Drop))).Should().Throw<InvalidOperationException>();

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
