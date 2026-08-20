# Serilog.Sinks.PostmarkEmail

A [Serilog](https://serilog.net) sink that batches log events and delivers them as email through the
[Postmark](https://postmarkapp.com) HTTP API.

No dependencies beyond Serilog itself. Targets `net10.0`, `net8.0` and `netstandard2.0`.

```
dotnet add package Serilog.Sinks.PostmarkEmail
```

## Quick start

```csharp
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.PostmarkEmail(
        serverToken: "your-postmark-server-token",
        from: "Application Logs <logs@yourdomain.com>",
        to: "ops@yourdomain.com",
        subject: "[{Level}] {Message}",
        restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();
```

Call `Log.CloseAndFlush()` (or dispose the logger) on shutdown so buffered events are sent rather
than dropped.

## Configuring from `appsettings.json`

Every parameter on the flat overload is a string or primitive, so
[Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration) can bind
it directly:

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.PostmarkEmail" ],
    "WriteTo": [
      {
        "Name": "PostmarkEmail",
        "Args": {
          "serverToken": "your-postmark-server-token",
          "from": "Application Logs <logs@yourdomain.com>",
          "to": "ops@yourdomain.com; oncall@yourdomain.com",
          "subject": "[{Level}] {Message}",
          "tag": "app-errors",
          "batchSizeLimit": 50,
          "bufferingTimeLimit": "00:01:00",
          "restrictedToMinimumLevel": "Error"
        }
      }
    ]
  }
}
```

Keep the token out of source control — supply it through user secrets, an environment variable, or
your key vault of choice and let configuration substitution fill it in.

## Configuring in code

For anything the flat overload doesn't cover — a shared `HttpClient`, a custom `IFormatProvider` —
use the options overload:

```csharp
using var httpClient = httpClientFactory.CreateClient("postmark");

Log.Logger = new LoggerConfiguration()
    .WriteTo.PostmarkEmail(new PostmarkEmailSinkOptions
    {
        ServerToken = configuration["Postmark:ServerToken"],
        From = "logs@yourdomain.com",
        To = "ops@yourdomain.com",
        Cc = "audit@yourdomain.com",
        Subject = "[{Level}] {Message}",
        Tag = "app-errors",
        MessageStream = "outbound",
        BatchSizeLimit = 50,
        BufferingTimeLimit = TimeSpan.FromMinutes(1),
        HttpClient = httpClient
    }, restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();
```

## Options

| Option | Default | Notes |
| --- | --- | --- |
| `ServerToken` | *required* | Postmark **server** token. See the gotcha below. |
| `From` | *required* | Must be a verified sender signature or an address on a confirmed domain. `Display Name <addr>` is accepted. |
| `To` | *required* | Comma- or semicolon-separated. Postmark accepts at most 50. |
| `Cc`, `Bcc`, `ReplyTo` | `null` | Omitted from the request when unset. |
| `Subject` | `"Log Email"` | An output template, rendered against the most significant event in the batch. |
| `OutputTemplate` | `"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}"` | Applied to every event to build the body. |
| `IsBodyHtml` | `false` | Sends the body as `HtmlBody` instead of `TextBody`. Nothing is escaped or wrapped, so pair it with an HTML-emitting template. |
| `Tag` | `null` | Groups these messages in Postmark's statistics. |
| `MessageStream` | `null` | Postmark defaults to `outbound`. |
| `TrackOpens` | `null` | Inherits the server setting when unset. |
| `BatchSizeLimit` | `100` | Events per email. |
| `BufferingTimeLimit` | `30s` | How long to wait for a batch to fill. |
| `QueueLimit` | `10000` | Events buffered before new ones are dropped. `null` for unbounded. |
| `EagerlyEmitFirstEvent` | `false` | Serilog's own default is `true`; sending one email for the very first event defeats the point of batching log mail. |
| `RetryTimeLimit` | `null` | Serilog's default of ten minutes. |
| `HttpClient` / `MessageHandler` | `null` | Mutually exclusive. See below. |
| `ServerUrl` | `https://api.postmarkapp.com/` | Override for proxies and tests. |
| `HttpTimeout` | `30s` | Applies only to a sink-owned client. |

## Behavior worth knowing

**The subject is a template.** It is rendered against the highest-level event in the batch (earliest
one, on a tie), so `"[{Level}] {Message}"` produces `[Error] Order 4021 failed to settle`. Because a
mail header cannot span lines, runs of whitespace in the rendered subject collapse to single spaces
and the result is truncated at 250 characters.

**Errors are split by whether a retry could help.** Serilog's batching infrastructure retries a batch
whose send threw, for up to `RetryTimeLimit`. That is right for a network blip or a 500, so those
throw. A rejected token or a malformed sender address would fail identically for the full ten
minutes, so those are written once to
[`SelfLog`](https://github.com/serilog/serilog/wiki/Debugging-and-Diagnostics) and the batch is
dropped. Postmark also returns a non-zero `ErrorCode` inside some 200-level responses; those are
reported to `SelfLog` too.

Turn `SelfLog` on while setting the sink up — it is where every delivery problem surfaces:

```csharp
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

**`HttpClient` ownership.** Supply `MessageHandler` and the sink builds a client over it, owns that
client, and leaves the handler for you to dispose. Supply `HttpClient` and the sink borrows it: it
never disposes it and never mutates its `BaseAddress`, `Timeout`, or default headers, so it is safe
to hand over a client from `IHttpClientFactory`. Requests always use an absolute URI derived from
`ServerUrl`, so a borrowed client's own `BaseAddress` is ignored. Supply neither and the sink creates
and disposes its own.

## Postmark gotchas

- **Server token, not account token.** The sink sends `X-Postmark-Server-Token`. An account token
  manages servers and domains and cannot send email; using one yields a 401.
- **The sender must be verified.** Postmark rejects a `From` that isn't a confirmed sender signature
  or on a verified domain, with `ErrorCode` 400. This is a configuration error, so the sink reports
  it to `SelfLog` and drops the batch rather than retrying.
- **Message streams.** Log email is transactional and belongs on the default `outbound` stream.
  Sending it on a broadcast stream adds unsubscribe handling you don't want on an alert.
- **Recipient cap.** Fifty addresses per field. Beyond that the sink drops the excess and warns
  through `SelfLog`, because Postmark would otherwise reject the whole message.

## Building

```
dotnet build
dotnet test
dotnet pack -c Release
```

`dotnet test` runs on Microsoft.Testing.Platform, which `global.json` opts into. Note that MTP mode
does not accept `--nologo`.

## License

MIT
