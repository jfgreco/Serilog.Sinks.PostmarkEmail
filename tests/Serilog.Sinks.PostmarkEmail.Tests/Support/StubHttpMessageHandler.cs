using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests.Support
{
    /// <summary>
    /// Captures outgoing requests and replies with a canned response. No network traffic.
    /// </summary>
    sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
        {
            _responder = responder ?? (_ => Ok());
        }

        // A sink driven through a real Logger is called from the batching worker thread while the
        // test thread reads these, so both sides go through the lock and readers get a snapshot.
        readonly object _sync = new();
        readonly List<HttpRequestMessage> _requests = new();
        readonly List<string> _bodies = new();

        /// <summary>Requests seen, in order. Their content is already consumed; use <see cref="Bodies"/>.</summary>
        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get { lock (_sync) return _requests.ToArray(); }
        }

        /// <summary>Request bodies, index-aligned with <see cref="Requests"/>.</summary>
        public IReadOnlyList<string> Bodies
        {
            get { lock (_sync) return _bodies.ToArray(); }
        }

        /// <summary>Whether this handler was disposed. The sink must never dispose a caller-supplied handler.</summary>
        public bool Disposed { get; private set; }

        public HttpRequestMessage SingleRequest => Assert.Single(Requests);

        public JsonElement SingleBody => JsonDocument.Parse(Assert.Single(Bodies)).RootElement;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Content is disposed once the request completes, so read it now. This has to happen
            // outside the lock, since await inside a lock is not allowed.
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();

            lock (_sync)
            {
                _requests.Add(request);
                _bodies.Add(body);
            }

            return _responder(request);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public static HttpResponseMessage Ok(int errorCode = 0, string message = "OK") =>
            Json(HttpStatusCode.OK, $$"""{"To":"ops@example.com","SubmittedAt":"2026-08-20T12:00:00Z","MessageID":"abc","ErrorCode":{{errorCode}},"Message":"{{message}}"}""");

        public static HttpResponseMessage Error(HttpStatusCode statusCode, int errorCode, string message) =>
            Json(statusCode, $$"""{"ErrorCode":{{errorCode}},"Message":"{{message}}"}""");

        public static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
    }
}
