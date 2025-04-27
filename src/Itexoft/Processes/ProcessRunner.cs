// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Itexoft.Threading;

namespace Itexoft.Processes;

public class ProcessRunner(string executablePath, string? workingDirectory = null) : IDisposable, IAsyncDisposable
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

    public static ProcessRunner CreateEntryRelative(string executablePath, string? workingDirectory = null)
    {
        var location = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;

        return CreateEntry(Path.Combine(location, executablePath), workingDirectory);
    }

    public static ProcessRunner CreateEntry(string executablePath, string? workingDirectory = null)
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

    public Task<int> RunAsync(IReadOnlyCollection<string> args, CancelToken token = default) =>
        this.RunInternalAsync(args, null, null, null, token);

    public Task<int> RunAsync(IReadOnlyCollection<string> args, string? input, CancelToken token = default) =>
        this.RunInternalAsync(args, input, null, null, token);

    public Task<int> RunAsync(
        IReadOnlyCollection<string> args,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancelToken token = default) => this.RunInternalAsync(args, null, writeOutput, writeError, token);

    public Task<int> RunAsync(
        IReadOnlyCollection<string> args,
        string? input,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancelToken token = default) => this.RunInternalAsync(args, input, writeOutput, writeError, token);

    public Task<int> RunAsync(IReadOnlyCollection<string> args, ProcessRunOptions options, CancelToken token = default) =>
        this.RunInternalAsync(args, options, token);

    protected async Task<RunResult> RunInternalAsync(IReadOnlyCollection<string> args, string? input, CancelToken token)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var code = await this.RunInternalAsync(args, input, x => output.AppendLine(x), x => error.AppendLine(x), null, null, true, token);

        return new(code, output.ToString(), error.ToString());
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    protected async Task<int> RunInternalAsync(
        IReadOnlyCollection<string> args,
        string? input,
        Action<string>? writeOutput,
        Action<string>? writeError,
        CancelToken token) => await this.RunInternalAsync(args, input, writeOutput, writeError, null, null, true, token);

    protected async Task<int> RunInternalAsync(IReadOnlyCollection<string> args, ProcessRunOptions options, CancelToken token) =>
        await this.RunInternalAsync(
            args,
            options.Input,
            options.WriteOutput,
            options.WriteError,
            options.Environment,
            options.WorkingDirectory,
            options.CaptureOutput,
            token);

    private async Task<int> RunInternalAsync(
        IReadOnlyCollection<string> args,
        string? input,
        Action<string>? writeOutput,
        Action<string>? writeError,
        IReadOnlyDictionary<string, string?>? environment,
        string? workingDirectoryOverride,
        bool captureOutput,
        CancelToken token)
    {
        var writeInputTask = Task.CompletedTask;
        var outputReadingTask = Task.CompletedTask;
        var errorReadingTask = Task.CompletedTask;
        var exitCode = 0;
        var cancelled = false;
        Exception? outputFailure = null;
        DataReceivedEventHandler? outputHandler = null;
        DataReceivedEventHandler? errorHandler = null;

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardError = captureOutput,
            RedirectStandardOutput = captureOutput,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        var resolvedWorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectoryOverride) ? workingDirectoryOverride : workingDirectory;

        if (!string.IsNullOrWhiteSpace(resolvedWorkingDirectory))
            psi.WorkingDirectory = resolvedWorkingDirectory;

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (value is null)
                    psi.Environment.Remove(key);
                else
                    psi.Environment[key] = value;
            }
        }

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var bridge = token.Bridge(out var cancellationToken);

        try
        {
            this.currentProcess = Process.Start(psi) ?? throw new InvalidOperationException("failed to start tool");

            if (input != null)
                writeInputTask = WriteInputAsync(this.currentProcess, input, cancellationToken);

            if (captureOutput)
            {
                var outputClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var errorClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                outputHandler = (_, argsEvent) =>
                {
                    if (argsEvent.Data is null)
                    {
                        outputClosed.TrySetResult(true);

                        return;
                    }

                    this.Out?.WriteLine(argsEvent.Data);
                    writeOutput?.Invoke(argsEvent.Data);
                };

                errorHandler = (_, argsEvent) =>
                {
                    if (argsEvent.Data is null)
                    {
                        errorClosed.TrySetResult(true);

                        return;
                    }

                    this.Error?.WriteLine(argsEvent.Data);
                    writeError?.Invoke(argsEvent.Data);
                };

                this.currentProcess.OutputDataReceived += outputHandler;
                this.currentProcess.ErrorDataReceived += errorHandler;
                this.currentProcess.BeginOutputReadLine();
                this.currentProcess.BeginErrorReadLine();

                outputReadingTask = outputClosed.Task;
                errorReadingTask = errorClosed.Task;
            }

            await Task.WhenAll(this.currentProcess.WaitForExitAsync(cancellationToken), writeInputTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;

            if (this.currentProcess is { HasExited: false })
                this.currentProcess.Kill(true);
        }
        finally
        {
            if (this.currentProcess is not null)
            {
                try
                {
                    await this.currentProcess.WaitForExitAsync().ConfigureAwait(false);
                    exitCode = this.currentProcess.ExitCode;
                }
                catch
                {
                    // ignore
                }
            }

            try
            {
                if (captureOutput && this.currentProcess is not null)
                {
                    try
                    {
                        this.currentProcess.CancelOutputRead();
                    }
                    catch { }

                    try
                    {
                        this.currentProcess.CancelErrorRead();
                    }
                    catch { }
                }

                await Task.WhenAll(outputReadingTask, errorReadingTask).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!cancelled)
                    outputFailure = ex;
            }

            if (this.currentProcess is not null)
            {
                if (outputHandler is not null)
                    this.currentProcess.OutputDataReceived -= outputHandler;

                if (errorHandler is not null)
                    this.currentProcess.ErrorDataReceived -= errorHandler;
            }

            this.currentProcess?.Dispose();
            this.currentProcess = null;
        }

        if (outputFailure is not null)
            ExceptionDispatchInfo.Capture(outputFailure).Throw();

        if (cancelled || cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        return exitCode;
    }

    private static async Task WriteInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(input.ToCharArray(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    public sealed record ProcessRunOptions
    {
        public string? Input { get; init; }
        public IReadOnlyDictionary<string, string?>? Environment { get; init; }
        public string? WorkingDirectory { get; init; }
        public bool CaptureOutput { get; init; } = true;
        public Action<string>? WriteOutput { get; init; }
        public Action<string>? WriteError { get; init; }
    }

    public sealed record RunResult(int ExitCode, string Output, string Error);
}
