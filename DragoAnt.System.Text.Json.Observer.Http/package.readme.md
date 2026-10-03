# DragoAnt.System.Text.Json.Observer.Http

HTTP message handler and extensions for logging and masking JSON request and response bodies using `DragoAnt.System.Text.Json.Observer`.

## Getting started

```sh
dotnet add package DragoAnt.System.Text.Json.Observer.Http
```

Targets `net8.0`, `net9.0`, and `net10.0`.

## Features

- **Generic `DelegatingHandler`**: Stream-friendly JSON body logging for `HttpClient`.
- **Masking Integration**: Mask request and response bodies using `DragoAnt.System.Text.Json.Observer` before emitting logs.
- **Safety by Default**:
  - Truncation limit on logged bodies (`MaxBodyBytes`, default 4096).
  - Unseekable stream safety: never buffers unbounded streams unexpectedly.
  - Transparent response body preservation: preserves streaming and allows downstream callers to read the full body even after logging.
  - Never throws exceptions from logging: failures in masking or logging log a warning and let the HTTP pipeline proceed.
- **Dynamic Configuration**: Per-client options via `IOptionsMonitor` with dynamic reload support.

## Usage

```csharp
services.AddHttpClient("ApiClient")
    .AddJsonBodyLogging(options =>
    {
        options.When = JsonBodyLogWhen.OnFailure;
        options.MaxBodyBytes = 4096;
    });

// In request call sites:
var request = new HttpRequestMessage(HttpMethod.Post, "/api/data")
    .WithBodyLogging<MyRequestModel, MyResponseModel>("MyOperation");

var response = await httpClient.SendAsync(request, cancellationToken);
```
