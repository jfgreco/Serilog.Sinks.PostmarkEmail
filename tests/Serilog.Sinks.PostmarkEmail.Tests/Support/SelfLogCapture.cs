using System;
using System.IO;
using System.Text;
using Serilog.Debugging;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests.Support
{
    /// <summary>
    /// Redirects <see cref="SelfLog"/> for the lifetime of the instance. SelfLog is process-global,
    /// so every test that uses this must belong to <see cref="NonParallelCollection"/>.
    /// </summary>
    sealed class SelfLogCapture : IDisposable
    {
        readonly StringWriter _writer = new(new StringBuilder());

        public SelfLogCapture() => SelfLog.Enable(_writer);

        public string Output => _writer.ToString();

        public void Dispose() => SelfLog.Disable();
    }

    [CollectionDefinition(NonParallelCollection.Name, DisableParallelization = true)]
    public sealed class NonParallelCollection
    {
        public const string Name = "SelfLog (non-parallel)";
    }
}
