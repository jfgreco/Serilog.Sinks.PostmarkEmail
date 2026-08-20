using System;
using System.Threading.Tasks;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail.Tests.Support;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests
{
    /// <summary>
    /// The only tests that talk to the real Postmark API. They skip themselves unless a token has
    /// been configured locally, so CI and a fresh clone never run them.
    /// <para>
    /// <strong>These send real email.</strong> Configuring a token is the opt-in; see
    /// <see cref="LiveTestSettings"/> for the two-line setup.
    /// </para>
    /// <para>
    /// This is the only thing that proves what a loopback server cannot: that the JSON field names
    /// are the ones Postmark accepts, that the sender signature is valid, and that a message is
    /// actually delivered.
    /// </para>
    /// </summary>
    [Collection(NonParallelCollection.Name)]
    public class LivePostmarkTests
    {
        static void SkipUnlessConfigured()
        {
            Assert.SkipUnless(
                LiveTestSettings.IsConfigured,
                $"Live Postmark test skipped: missing {LiveTestSettings.MissingDescription}. " +
                "See CONTRIBUTING.md; configuring a token opts in and sends real email.");
        }

        static PostmarkEmailSinkOptions LiveOptions() => new()
        {
            ServerToken = LiveTestSettings.ServerToken,
            From = LiveTestSettings.From,
            To = LiveTestSettings.To,
            Subject = "[{Level}] Serilog.Sinks.PostmarkEmail live test",
            OutputTemplate = "{Timestamp:O} [{Level}] {Message}{NewLine}",
            Tag = "serilog-sink-live-test"
        };

        [Fact]
        public async Task DeliversARealMessageThroughPostmark()
        {
            SkipUnlessConfigured();

            var options = LiveOptions();
            options.Validate("options");

            using var selfLog = new SelfLogCapture();
            using (var sink = new PostmarkEmailSink(options))
            {
                await sink.EmitBatchAsync(Some.Batch(
                    Some.LogEvent(LogEventLevel.Error, "live test: this message was sent by the test suite")));
            }

            // The sink reports every delivery failure through SelfLog and swallows permanent ones,
            // so silence here is the actual success signal.
            Assert.True(
                selfLog.Output.Length == 0,
                $"Postmark rejected the message. SelfLog said: {selfLog.Output}");
        }

        [Fact]
        public async Task RejectsABadTokenAsAPermanentFailure()
        {
            SkipUnlessConfigured();

            // Deliberately wrong token against the real API: proves the sink classifies a genuine
            // Postmark 401 as permanent -- reported once and dropped, not retried for ten minutes.
            var options = LiveOptions();
            options.ServerToken = "00000000-0000-0000-0000-000000000000";
            options.Validate("options");

            using var selfLog = new SelfLogCapture();
            using var sink = new PostmarkEmailSink(options);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent(LogEventLevel.Error, "should not arrive")));

            Assert.Contains("dropping batch", selfLog.Output);
            Assert.Contains("401", selfLog.Output);
        }
    }
}
