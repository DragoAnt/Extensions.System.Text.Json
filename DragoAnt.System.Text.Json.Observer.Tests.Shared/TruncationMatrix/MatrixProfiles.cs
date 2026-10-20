namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

internal static class MatrixProfiles
{
    internal static IReadOnlyList<(string Name, JsonObserverOptions Options)> Options { get; } =
    [
        ("default", new()),
        ("ignore-nulls", new() { IgnoreNulls = true }),
        ("indented", new() { Indented = true }),
        ("nulls+indented", new() { IgnoreNulls = true, Indented = true }),
        ("out-0", new() { MaxOutputBytes = 0 }),
        ("out-1", new() { MaxOutputBytes = 1 }),
        ("out-2", new() { MaxOutputBytes = 2 }),
        ("out-40+indented", new() { MaxOutputBytes = 40, Indented = true }),
        ("value-0", new() { MaxValueBytes = 0 }),
        ("value-4+nulls", new() { MaxValueBytes = 4, IgnoreNulls = true }),
        ("case-sensitive", new() { NameCaseInsensitive = false }),
        ("cs+out-40+value-4", new() { NameCaseInsensitive = false, MaxOutputBytes = 40, MaxValueBytes = 4 }),
    ];

    internal static IReadOnlyList<(string Name, CommentPolicy Policy)> CommentPolicies { get; } =
    [
        ("allowlist", CommentPolicy.AllowList),
        ("blocklist", CommentPolicy.BlockList),
        ("mask-all", CommentPolicy.MaskAll),
        ("drop-all", CommentPolicy.DropAll),
        ("last4", CommentPolicy.Mask(MaskTag.Last4)),
    ];
}
