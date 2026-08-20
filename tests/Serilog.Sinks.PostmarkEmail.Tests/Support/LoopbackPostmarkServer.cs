using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Serilog.Sinks.PostmarkEmail.Tests.Support
{
    /// <summary>
    /// A minimal HTTP server on the loopback interface that stands in for api.postmarkapp.com.
    /// <para>
    /// Unlike <see cref="StubHttpMessageHandler"/>, which replaces the transport, this exercises the
    /// real one: a real socket, real HTTP framing, and headers actually serialized by
    /// <see cref="System.Net.Http.HttpClient"/>. It is the closest the suite gets to Postmark
    /// without a server token.
    /// </para>
    /// <para>
    /// Raw <see cref="TcpListener"/> rather than <c>HttpListener</c>: binding to port 0 and keeping
    /// the socket means there is no window in which another process can take the port, and it avoids
    /// HttpListener's platform-specific URL reservation behaviour.
    /// </para>
    /// </summary>
    sealed class LoopbackPostmarkServer : IDisposable
    {
        readonly TcpListener _listener;
        readonly Task _serving;
        readonly CancellationTokenSource _cts = new();
        readonly int _statusCode;
        readonly string _responseBody;
        readonly TimeSpan _delayBeforeResponding;

        public LoopbackPostmarkServer(
            int statusCode = 200,
            string? responseBody = null,
            TimeSpan? delayBeforeResponding = null)
        {
            _statusCode = statusCode;
            _responseBody = responseBody ?? "{\"To\":\"ops@example.com\",\"MessageID\":\"loopback\",\"ErrorCode\":0,\"Message\":\"OK\"}";
            _delayBeforeResponding = delayBeforeResponding ?? TimeSpan.Zero;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

            _serving = Task.Run(ServeOneAsync);
        }

        /// <summary>The address to hand to the sink as its <c>ServerUrl</c>.</summary>
        public Uri BaseAddress { get; }

        /// <summary>Signals that a complete request has been read.</summary>
        public TaskCompletionSource<bool> RequestReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : "(absent)";

        /// <summary>Waits for the request to arrive, failing rather than hanging if it never does.</summary>
        public async Task WaitForRequestAsync(int timeoutSeconds = 30)
        {
            var completed = await Task.WhenAny(
                RequestReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))).ConfigureAwait(false);

            if (completed != RequestReceived.Task)
                throw new TimeoutException($"No request reached the loopback server within {timeoutSeconds}s.");
        }

        async Task ServeOneAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var stream = client.GetStream();

                var head = await ReadUntilHeadersEndAsync(stream).ConfigureAwait(false);
                ParseHead(head);

                if (Headers.TryGetValue("Content-Length", out var lengthText) &&
                    int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) &&
                    length > 0)
                {
                    var body = new byte[length];
                    var read = 0;
                    while (read < length)
                    {
                        var n = await stream.ReadAsync(body, read, length - read).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }
                    Body = Encoding.UTF8.GetString(body, 0, read);
                }

                RequestReceived.TrySetResult(true);

                if (_delayBeforeResponding > TimeSpan.Zero)
                    await Task.Delay(_delayBeforeResponding, _cts.Token).ConfigureAwait(false);

                var payload = Encoding.UTF8.GetBytes(_responseBody);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {_statusCode} {ReasonPhrase(_statusCode)}\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException
                                        or OperationCanceledException
                                        or InvalidOperationException
                                        or IOException
                                        or SocketException)
            {
                // Expected when the test disposes the server, or when the client hangs up first
                // because its own timeout fired. Not a test failure.
                RequestReceived.TrySetResult(false);
            }
        }

        static async Task<string> ReadUntilHeadersEndAsync(NetworkStream stream)
        {
            var buffer = new List<byte>(512);
            var one = new byte[1];

            while (true)
            {
                var n = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n == 0) break;

                buffer.Add(one[0]);

                var c = buffer.Count;
                if (c >= 4 &&
                    buffer[c - 4] == (byte)'\r' && buffer[c - 3] == (byte)'\n' &&
                    buffer[c - 2] == (byte)'\r' && buffer[c - 1] == (byte)'\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        void ParseHead(string head)
        {
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return;

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length >= 2)
            {
                Method = requestLine[0];
                Path = requestLine[1];
            }

            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0)
                    continue;
                Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
        }

        static string ReasonPhrase(int statusCode) => statusCode switch
        {
            200 => "OK",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "Status"
        };

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (SocketException) { }

            // Give the serving task a moment to unwind; never let cleanup fail a test.
            try { _serving.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }

            _cts.Dispose();
        }
    }
}
