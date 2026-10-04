using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class DefaultPolicyTests
{
    private const string Json = """{"a":{"pin":"1","name":"x","nested":{"city":"y"}}}""";

    [Fact]
    public void SharedNestedRule_TwoParentsDifferentDefaults_EachUsesOwn()
    {
        var shared = JsonObserverItem<JsonObserveringEmptyContext>.Obj(b => b.Match("pin").MaskStr((_, _) => "***"), null).Delegate;
        var blockList = JsonObserver.Obj(b => b.Match("a").Obj(shared), BlockList);
        var nullList = JsonObserver.Obj(b => b.Match("a").Obj(shared), NullList);

        var first = blockList.Mask(Json);
        var second = nullList.Mask(Json);

        first.Should().Be("""{"a":{"pin":"***","name":"x","nested":{"city":"y"}}}""");
        second.Should().Be("""{"a":{"pin":"***","name":null,"nested":{"city":null}}}""");
    }

    [Fact]
    public void ConcurrentFirstCalls_SameOutput()
    {
        const string expected = """{"a":{"pin":"***","name":"x","nested":{"city":"y"}}}""";
        for (var round = 0; round < 20; round++)
        {
            var observer = JsonObserver.Obj(
                b => b.Match("a").Obj(a => a.Match("pin").MaskStr((_, _) => "***")),
                BlockList);
            using var start = new ManualResetEventSlim();
            var results = new string?[8];
            var threads = Enumerable.Range(0, results.Length)
                .Select(i => new Thread(() =>
                {
                    start.Wait();
                    results[i] = observer.Mask(Json);
                }))
                .ToArray();

            foreach (var thread in threads)
            {
                thread.Start();
            }

            start.Set();
            foreach (var thread in threads)
            {
                thread.Join();
            }

            results.Should().AllBe(expected);
        }
    }
}
