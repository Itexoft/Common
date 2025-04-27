namespace Itexoft.Common.ExecutionTools.Node;

public sealed class NpxRunner(string workingDirectory) : ToolRunner(npxPath, workingDirectory)
{
    private const string npxPath = "npx";
    
    public async Task<string> ExecAsync(string command, IReadOnlyCollection<string>? commandArgs = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException($"'{nameof(command)}' cannot be null or whitespace.", nameof(command));

        var args = new List<string>
        {
            "--yes",
            command
        };

        if (commandArgs is not null)
            args.AddRange(commandArgs);

        var (_, stdout, _) = await this.RunInternalAsync(args, null, token);

        return stdout;
    }
}