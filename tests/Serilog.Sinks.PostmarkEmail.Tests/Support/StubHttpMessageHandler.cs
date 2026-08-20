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

        /// <summary>Requests seen, in order. Their content is already consumed; use <see cref="Bodies"/>.</summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>Request bodies, index-aligned with <see cref="Requests"/>.</summary>
        public List<string> Bodies { get; } = new();

        /// <summary>Whether this handler was disposed. The sink must never dispose a caller-supplied handler.</summary>
        public bool Disposed { get; private set; }

        public HttpRequestMessage SingleRequest => Assert.Single(Requests);

        public JsonElement SingleBody => JsonDocument.Parse(Assert.Single(Bodies)).RootElement;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            // Content is disposed once the request completes, so read it now.
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());

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
