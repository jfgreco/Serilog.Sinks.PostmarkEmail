using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail.Tests.Support;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests
{
    /// <summary>
    /// Drives the sink over a real socket against <see cref="LoopbackPostmarkServer"/>.
    /// <para>
    /// The rest of the suite injects an <see cref="HttpMessageHandler"/>, which replaces the
    /// transport, so nothing there proves the request survives being serialized onto the wire.
    /// These tests do: real HttpClient, real HTTP framing, real headers.
    /// </para>
    /// <para>
    /// What they still cannot prove is anything only Postmark knows -- that these field names are
    /// the ones it accepts, or that a message is actually delivered. That needs a server token.
    /// </para>
    /// </summary>
    public class LoopbackTransportTests
    {
        static PostmarkEmailSinkOptions OptionsFor(LoopbackPostmarkServer server) => new()
        {
            ServerToken = "loopback-token",
            From = "Application Logs <logs@example.com>",
            To = "ops@example.com; oncall@example.com",
            Subject = "[{Level}] {Message}",
            OutputTemplate = "{Message}",
            Tag = "loopback",
            ServerUrl = server.BaseAddress
        };

        [Fact]
        public async Task SendsAWellFormedRequestOverARealSocket()
        {
            using var server = new LoopbackPostmarkServer();
            var options = OptionsFor(server);
            options.Validate("options");

            using (var sink = new PostmarkEmailSink(options))
                await sink.EmitBatchAsync(Some.Batch(Some.LogEvent(LogEventLevel.Error, "disk full")));

            await server.WaitForRequestAsync();

            Assert.Equal("POST", server.Method);
            Assert.Equal("/email", server.Path);

            // Headers as actually serialized by HttpClient, not as staged on a stub.
            Assert.Equal("loopback-token", server.Header("X-Postmark-Server-Token"));
            Assert.Contains("application/json", server.Header("Accept"));
            Assert.Contains("application/json", server.Header("Content-Type"));
            Assert.Contains("charset=utf-8", server.Header("Content-Type"));
            Assert.StartsWith("Serilog.Sinks.PostmarkEmail/", server.Header("User-Agent"));
            Assert.NotEqual("(absent)", server.Header("Content-Length"));

            var body = JsonDocument.Parse(server.Body!).RootElement;
            Assert.Equal("Application Logs <logs@example.com>", body.GetProperty("From").GetString());
            Assert.Equal("ops@example.com,oncall@example.com", body.GetProperty("To").GetString());
            Assert.Equal("[Error] disk full", body.GetProperty("Subject").GetString());
            Assert.Equal("disk full", body.GetProperty("TextBody").GetString());
            Assert.Equal("loopback", body.GetProperty("Tag").GetString());
        }

        [Fact]
        public async Task SurvivesNonAsciiOnTheWire()
        {
            // Content-Length is a byte count, not a character count. If the sink ever computed it
            // from string length this would truncate the JSON and the server would stall.
            using var server = new LoopbackPostmarkServer();
            var options = OptionsFor(server);
            options.Validate("options");

            using (var sink = new PostmarkEmailSink(options))
                await sink.EmitBatchAsync(Some.Batch(
                    Some.LogEvent(LogEventLevel.Error, "pago rechazado: saldo insuficiente — usuario ñandu ✅")));

            await server.WaitForRequestAsync();

            var body = JsonDocument.Parse(server.Body!).RootElement;
            Assert.Contains("saldo insuficiente", body.GetProperty("TextBody").GetString());
            Assert.Contains("ñandu", body.GetProperty("TextBody").GetString());
        }

        [Fact]
        public async Task ThrowsOnAServerErrorSoTheBatchIsRetried()
        {
            using var server = new LoopbackPostmarkServer(
                statusCode: 500, responseBody: "{\"ErrorCode\":0,\"Message\":\"server error\"}");
            var options = OptionsFor(server);
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);

            var ex = await Assert.ThrowsAsync<PostmarkApiException>(
                () => sink.EmitBatchAsync(Some.Batch(Some.LogEvent())));

            Assert.Equal(500, (int)ex.StatusCode);
        }

        [Fact]
        public async Task DropsTheBatchOnAValidationErrorWithoutThrowing()
        {
            using var server = new LoopbackPostmarkServer(
                statusCode: 422, responseBody: "{\"ErrorCode\":300,\"Message\":\"Invalid 'From' address\"}");
            var options = OptionsFor(server);
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));
        }

        [Fact]
        public async Task HttpTimeoutActuallyApplies()
        {
            // The only behavioural coverage of HttpTimeout: the server accepts the request but
            // never answers, so the sink must give up rather than hang.
            using var server = new LoopbackPostmarkServer(delayBeforeResponding: TimeSpan.FromSeconds(30));
            var options = OptionsFor(server);
            options.HttpTimeout = TimeSpan.FromMilliseconds(750);
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);

            var started = DateTime.UtcNow;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => sink.EmitBatchAsync(Some.Batch(Some.LogEvent())));

            // Generous upper bound: the point is that it gave up long before the server's 30s,
            // not that the timeout is precise.
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15),
                "the sink should have abandoned the request at its own timeout");
        }

        [Fact]
        public async Task LetsAConnectionFailurePropagate()
        {
            // Bind and immediately dispose, so the port is closed and nothing is listening.
            Uri dead;
            using (var server = new LoopbackPostmarkServer())
                dead = server.BaseAddress;

            var options = new PostmarkEmailSinkOptions
            {
                ServerToken = "t",
                From = "logs@example.com",
                To = "ops@example.com",
                ServerUrl = dead,
                HttpTimeout = TimeSpan.FromSeconds(5)
            };
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);

            // A refused connection is transient by nature, so it must not be swallowed.
            await Assert.ThrowsAnyAsync<Exception>(() => sink.EmitBatchAsync(Some.Batch(Some.LogEvent())));
        }

        [Fact]
        public async Task WorksEndToEndThroughARealLogger()
        {
            using var server = new LoopbackPostmarkServer();

            var logger = new LoggerConfiguration()
                .WriteTo.PostmarkEmail(
                    serverToken: "loopback-token",
                    from: "logs@example.com",
                    to: "ops@example.com",
                    subject: "[{Level}] {Message}",
                    outputTemplate: "{Message}",
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    serverUrl: server.BaseAddress.ToString())
                .CreateLogger();

            logger.Information("below the minimum level");
            logger.Error("through the whole stack");
            logger.Dispose();

            await server.WaitForRequestAsync();

            var body = JsonDocument.Parse(server.Body!).RootElement;
            Assert.Equal("[Error] through the whole stack", body.GetProperty("Subject").GetString());
            Assert.Equal("through the whole stack", body.GetProperty("TextBody").GetString());
        }
    }
}
