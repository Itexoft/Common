// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Itexoft.ExecutionTools;

public class ToolRunner(string executablePath, string? workingDirectory = null) : IDisposable, IAsyncDisposable
{
    private Process? currentProcess;
    public TextWriter? Out { get; set; }
    public TextWriter? Error { get; set; }

    public ValueTask DisposeAsync()
    {
        this.Dispose();

        return ValueTask.CompletedTask;
    }

    public void Dispose() => this.currentProcess?.Kill(true);

    public static ToolRunner CreateEntryRelative(string executablePath, string? workingDirectory = null)
    {
        var location = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;

        return CreateEntry(Path.Combine(location, executablePath), workingDirectory);
    }

    public static ToolRunner CreateEntry(string executablePath, string? workingDirectory = null)
    {
        workingDirectory = !string.IsNullOrEmpty(workingDirectory) && Path.IsPathRooted(workingDirectory)
            ? workingDirectory
            : Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, workingDirectory ?? string.Empty);

        return new(executablePath, workingDirectory);
    }

    public void SetConsoleOutError()
    {
        this.Out = Console.Out;
        this.Error = Console.Error;
    }

    public Task<int> RunAsync(IReadOnlyCollection<string> args, CancellationToken token = default) =>
        this.RunInternalAsync(args, null, null, null, token);

    public Task<int> RunAsync(IReadOnlyCollection<string> args, string? input, CancellationToken token = default) =>
        this.RunInternalAsync(args, input, null, null, token);

    public Task<int> RunAsync(
        IReadOnlyCollection<string> args,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancellationToken token = default) => this.RunInternalAsync(args, null, writeOutput, writeError, token);

    public Task<int> RunAsync(
        IReadOnlyCollection<string> args,
        string? input,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancellationToken token = default) => this.RunInternalAsync(args, input, writeOutput, writeError, token);

    protected async Task<RunResult> RunInternalAsync(IReadOnlyCollection<string> args, string? input, CancellationToken token)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var code = await this.RunInternalAsync(args, input, x => output.AppendLine(x), x => error.AppendLine(x), token);

        return new(code, output.ToString(), error.ToString());
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    protected async Task<int> RunInternalAsync(
        IReadOnlyCollection<string> args,
        string? input,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            this.currentProcess = Process.Start(psi) ?? throw new InvalidOperationException("failed to start tool");

            var writeInputTask = Task.CompletedTask;
            if (input != null)
                writeInputTask = this.currentProcess.StandardInput.WriteAsync(input.ToCharArray(), token)
                    .ContinueWith(_ => this.currentProcess!.StandardInput.Close(), token);

            var outputReadingTask = Task.Run(
                async () =>
                {
                    while (await this.currentProcess!.StandardOutput.ReadLineAsync() is { } line)
                    {
                        await this.Out?.WriteLineAsync(line)!;
                        writeOutput?.Invoke(line);
                    }
                },
                token);

            var errorReadingTask = Task.Run(
                async () =>
                {
                    while (await this.currentProcess!.StandardError.ReadLineAsync() is { } line)
                    {
                        await this.Error?.WriteLineAsync(line)!;
                        writeError?.Invoke(line);
                    }
                },
                token);

            try
            {
                await Task.WhenAll(this.currentProcess.WaitForExitAsync(token), writeInputTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!this.currentProcess.HasExited)
                    this.currentProcess.Kill(true);

                throw;
            }

            await Task.WhenAll(outputReadingTask, errorReadingTask).ConfigureAwait(false);

            return this.currentProcess.ExitCode;
        }
        finally
        {
            this.currentProcess?.Dispose();
            this.currentProcess = null;
        }
    }

    public sealed record RunResult(int ExitCode, string Output, string Error);
}