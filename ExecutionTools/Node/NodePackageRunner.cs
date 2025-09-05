namespace Itexoft.Common.ExecutionTools.Node;

public abstract class NodePackageRunner(string executablePath, string workingDirectory, NodePackageRunner.Options? options = null)
    : ToolRunner(executablePath, workingDirectory)
{
    protected readonly Options options = options ?? new Options();

    public virtual async Task<string> InstallAsync(IReadOnlyCollection<string>? packages = null, CancellationToken token = default)
    {
        var a = new List<string> { "install" };
        if (this.options.Global)
            a.Add("-g");
        if (packages is not null && packages.Count != 0)
            a.AddRange(packages);
        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    public async Task<string> RunScriptAsync(string script, IReadOnlyCollection<string>? scriptArgs = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException($"'{nameof(script)}' cannot be null or whitespace.", nameof(script));
        var a = new List<string> { "run", script };
        if (scriptArgs is not null)
            a.AddRange(scriptArgs);
        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    public async Task<string> ExecAsync(IReadOnlyCollection<string> execArgs, CancellationToken token = default)
    {
        var (_, stdout, _) = await this.ExecuteAsync(execArgs.ToList(), token);

        return stdout;
    }

    protected virtual async Task<RunResult> ExecuteAsync(List<string> args, CancellationToken token)
    {
        if (this.options.Verbose)
            args.Add("--verbose");
        if (this.options.Force)
            args.Add("--force");

        return await this.RunInternalAsync(args, null, token);
    }
    
    public class Options
    {
        public bool Global { get; init; }

        public bool Force { get; init; }

        public bool Verbose { get; init; }
    }
}

public sealed class NpmRunner(string workingDirectory, NodePackageRunner.Options? options = null)
    : NodePackageRunner(npmPath, workingDirectory, options)
{
    private static readonly string npmPath = "npm";
}

public sealed class YarnRunner(string workingDirectory, NodePackageRunner.Options? options = null)
    : NodePackageRunner(yarnPath, workingDirectory, options)
{
    private static readonly string yarnPath = "yarn";

    public async override Task<string> InstallAsync(IReadOnlyCollection<string>? packages = null, CancellationToken token = default)
    {
        var a = new List<string>();
        if (packages is null || packages.Count == 0)
        {
            a.Add("install");
        }
        else
        {
            a.Add("add");
            a.AddRange(packages);
        }

        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    protected async override Task<RunResult> ExecuteAsync(List<string> args, CancellationToken token)
    {
        if (this.options.Verbose)
            args.Add("--verbose");
        if (this.options.Force)
            args.Add("--force");
        if (this.options.Global)
            args.Insert(0, "global");

        return await this.RunInternalAsync(args, null, token);
    }
}