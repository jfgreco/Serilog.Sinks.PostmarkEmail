# Contributing

Thanks for taking an interest. Issues and pull requests are welcome.

## Getting set up

You need the **.NET 10 SDK**. The test project also targets `net8.0`, so you need the **.NET 8
runtime** installed to run the full suite locally — otherwise the net8.0 half fails to start.

```
dotnet build
dotnet test
```

`dotnet test` runs on Microsoft.Testing.Platform, which `global.json` opts into. Two consequences
that trip people up:

- **`dotnet test --nologo` fails** with exit code 5. `--nologo` is a VSTest-mode flag and MTP
  rejects it as an invalid argument. Leave it off.
- Test projects are executables (`<OutputType>Exe</OutputType>`), so you can also run
  `./tests/Serilog.Sinks.PostmarkEmail.Tests/bin/Debug/net10.0/Serilog.Sinks.PostmarkEmail.Tests.exe`
  directly to iterate on one framework.

## Project shape

`src/Serilog.Sinks.PostmarkEmail` is the library. It has **no dependency beyond Serilog** on the
modern targets, and adds `System.Text.Json` only for `netstandard2.0`. Please keep it that way —
a logging sink that drags dependencies into every consumer is a liability. Serilog 4 provides
batching in the core package (`IBatchedLogEventSink`, `BatchingOptions`), so there is no need for
`Serilog.Sinks.PeriodicBatching`.

The library builds with `TreatWarningsAsErrors` and nullable reference types enabled across all
three target frameworks. A change that only compiles on `net10.0` will fail the build.

## Testing conventions

- **No test may touch the network.** Inject a handler through
  `PostmarkEmailSinkOptions.MessageHandler` and use `StubHttpMessageHandler`.
- Tests that capture `SelfLog` must be in `NonParallelCollection`. `SelfLog` is process-global, so
  running those in parallel with anything else makes them unreliable.
- Prefer asserting on the parsed JSON body over string matching, so a formatting change does not
  produce a false failure.

### Do not assert on Serilog's batch grouping

This one has already cost a broken release. Serilog's batching worker can drain the queue *between*
two `logger.Write` calls, so two events logged back to back may arrive as one email or as two. It is
a scheduling decision, not a contract of this sink.

A test that logged two events and asserted `Assert.Single(handler.Bodies)` passed on Windows every
time, passed CI, and then failed the release run — roughly a third of the time on a slower machine.
If you need multi-event coverage through a real `Logger`, assert on the concatenation of all bodies
rather than on how many requests happened. See
`DeliversEveryBufferedEventOnDisposeHoweverTheSchedulerGroupsThem`.

### Reproducing a Linux-only failure

CI runs on Linux. If something passes locally on Windows but fails in CI, reproduce it in a
container rather than guessing:

```bash
docker run --rm --cpus=2 -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c "
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/di.sh &&
  bash /tmp/di.sh --channel 8.0 --runtime dotnet --install-dir /usr/share/dotnet --no-path &&
  git clone -q /src /work && cd /work && dotnet test -c Release"
```

`--cpus=2` matters: it approximates a GitHub runner and surfaces races that a fast machine hides.
For a suspected flake, loop it — a single green run proves nothing:

```bash
for i in $(seq 1 25); do dotnet test -c Release --no-build || echo "FAILED on $i"; done
```

## Pull requests

- Branch from `master`.
- Keep the public API surface small, and document any new public member — the library builds with
  `GenerateDocumentationFile`, so a missing XML comment is a build error.
- Add a test that fails without your change.
- Run `dotnet test` before pushing. CI runs the same command on `net8.0` and `net10.0`.

## Releasing

Maintainers only. See the [Releasing section of the README](../README.md#releasing). In short: the
git tag is the source of truth for the version, and pushing a `v*` tag publishes to NuGet through
trusted publishing. **A published version can never be reused** — only unlisted.
