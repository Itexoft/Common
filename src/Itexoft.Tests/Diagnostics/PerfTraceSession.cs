// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.NETCore.Client;

namespace Itexoft.Tests.Diagnostics;

internal sealed class PerfTraceSession : IDisposable
{
    private readonly EventPipeSession? session;
    private readonly Thread? pumpThread;
    private readonly FileStream? stream;

    private PerfTraceSession(EventPipeSession session, Thread pumpThread, FileStream stream)
    {
        this.session = session;
        this.pumpThread = pumpThread;
        this.stream = stream;
    }

    public static PerfTraceSession? Start(string name)
    {
        if (!IsEnabled("ITEXOFT_PERF_TRACE"))
            return null;

        var traceDir = Environment.GetEnvironmentVariable("ITEXOFT_PERF_TRACE_DIR");

        if (string.IsNullOrWhiteSpace(traceDir))
            traceDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "profiles");

        Directory.CreateDirectory(traceDir);

        var fileName = $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.nettrace";
        var path = Path.Combine(traceDir, fileName);

        var providers = new List<EventPipeProvider>
        {
            new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
        };

        var client = new DiagnosticsClient(Process.GetCurrentProcess().Id);
        var session = client.StartEventPipeSession(providers, requestRundown: true);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        var pumpThread = new Thread(
            () =>
            {
                try
                {
                    session.EventStream.CopyTo(stream);
                }
                catch
                {
                }
            })
        {
            IsBackground = true,
            Name = "PerfTraceSessionPump",
        };

        pumpThread.Start();

        return new PerfTraceSession(session, pumpThread, stream);
    }

    public void Dispose()
    {
        if (this.session == null)
            return;

        try
        {
            this.session.Stop();
        }
        catch
        {
        }

        if (this.pumpThread != null)
            this.pumpThread.Join(TimeSpan.FromSeconds(2));

        this.stream?.Dispose();
        this.session.Dispose();
    }

    private static bool IsEnabled(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
