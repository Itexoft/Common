using System.Diagnostics;
using System.Text;

namespace Itexoft.Common.ExecutionTools;

public abstract class ToolRunner(string executablePath, string? workingDirectory = null) : IDisposable, IAsyncDisposable
{

    private Process? currentProcess;

    public ValueTask DisposeAsync()
    {
        this.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() { this.currentProcess?.Kill(true); }

    protected abstract List<string> BuildArguments(string sourceFile);

    public bool EchoOutput { get; set; }

    public Task<RunResult> RunAsync(IReadOnlyCollection<string> args, CancellationToken token = default)
    {
        return this.RunAsync(args, null, token);
    }

    public async Task<RunResult> RunAsync(IReadOnlyCollection<string> args, string? input, CancellationToken token = default)
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
                    .ContinueWith(_ => this.currentProcess.StandardInput.Close(), token);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            var outputReadingTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await this.currentProcess!.StandardOutput.ReadLineAsync(token)) is not null)
                {
                    outputBuilder.AppendLine(line);
                    if (this.EchoOutput)
                        Console.WriteLine(line);
                }
            }, token);

            var errorReadingTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await this.currentProcess!.StandardError.ReadLineAsync(token)) is not null)
                {
                    errorBuilder.AppendLine(line);
                    if (this.EchoOutput)
                        Console.Error.WriteLine(line);
                }
            }, token);

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
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            var code = this.currentProcess.ExitCode;

            if (code != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executablePath)} exited with {code}: {error}");

            return new(this.currentProcess.ExitCode, output, error);
        }
        finally
        {
            this.currentProcess!.Dispose();
            this.currentProcess = null;
        }
    }

    public sealed record RunResult(int ExitCode, string Output, string Error);
}