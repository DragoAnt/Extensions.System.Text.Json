using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Gate>();
var app = builder.Build();

app.MapPost("/echo", async context =>
{
    context.Response.ContentType = "application/json";
    await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.MapGet("/item/{id}", async (string id, HttpContext context) =>
{
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync($$"""{"id":"{{id}}","secret":"s-{{id}}","data":"{{new string('x', 2_048)}}"}""", context.RequestAborted);
});

app.MapGet("/slow", async (int ms, HttpContext context) =>
{
    await Task.Delay(ms, context.RequestAborted);
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("""{"ok":true}""", context.RequestAborted);
});

app.MapGet("/stream-json", async (HttpContext context, Gate gate) =>
{
    context.Response.ContentType = "application/json";
    using var registration = context.RequestAborted.Register(() => gate.Aborted.TrySetResult());
    await context.Response.WriteAsync("""{"event":1""", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
    await gate.Release.Task.WaitAsync(context.RequestAborted);
    await context.Response.WriteAsync("}", context.RequestAborted);
});

app.MapGet("/sse", async (HttpContext context, Gate gate) =>
{
    context.Response.ContentType = "text/event-stream";
    using var registration = context.RequestAborted.Register(() => gate.Aborted.TrySetResult());
    await context.Response.WriteAsync("data: {\"a\":1}\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
    await gate.Release.Task.WaitAsync(context.RequestAborted);
});

app.MapGet("/fail", async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsync("""{"title":"boom","secret":"pw-500"}""", context.RequestAborted);
});

app.MapGet("/huge", async (int mb, HttpContext context) =>
{
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"data\":\"", context.RequestAborted);
    var chunk = Encoding.UTF8.GetBytes(new string('x', 64 * 1024));
    for (var remaining = mb * 1024 * 1024; remaining > 0; remaining -= chunk.Length)
    {
        await context.Response.Body.WriteAsync(chunk.AsMemory(0, Math.Min(remaining, chunk.Length)), context.RequestAborted);
    }

    await context.Response.WriteAsync("\"}", context.RequestAborted);
});

app.MapGet("/chunked", async context =>
{
    context.Response.ContentType = "application/json";
    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
    foreach (var part in new[] { "{\"parts\":[1,", "2,3],", "\"secret\":\"pw-c\"}" })
    {
        await context.Response.WriteAsync(part, context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
});

app.MapPost("/hash", async context =>
{
    context.Response.ContentType = "application/json";
    await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.Run();

public sealed class Gate
{
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public partial class Program;
