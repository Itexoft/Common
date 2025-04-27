// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.Processes.Node;

public abstract class NodePackageRunner(string executablePath, string workingDirectory, NodePackageRunner.Options? options = null)
    : ProcessRunner(executablePath, workingDirectory)
{
    protected readonly Options OptionsInternal = options ??= new();

    public virtual async Task<string> InstallAsync(IReadOnlyCollection<string>? packages = null, CancelToken token = default)
    {
        var a = new List<string> { "install" };

        if (this.OptionsInternal.Global)
            a.Add("-g");

        if (packages is not null && packages.Count != 0)
            a.AddRange(packages);

        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    public async Task<string> RunScriptAsync(string script, IReadOnlyCollection<string>? scriptArgs = null, CancelToken token = default)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException($"'{nameof(script)}' cannot be null or whitespace.", nameof(script));

        var a = new List<string> { "run", script };

        if (scriptArgs is not null)
            a.AddRange(scriptArgs);

        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    public async Task<string> ExecAsync(IReadOnlyCollection<string> execArgs, CancelToken token = default)
    {
        var (_, stdout, _) = await this.ExecuteAsync(execArgs.ToList(), token);

        return stdout;
    }

    protected virtual async Task<RunResult> ExecuteAsync(List<string> args, CancelToken token)
    {
        if (this.OptionsInternal.Verbose)
            args.Add("--verbose");

        if (this.OptionsInternal.Force)
            args.Add("--force");

        return await this.RunInternalAsync(args, "", token);
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
    private const string npmPath = "npm";
}

public sealed class YarnRunner(string workingDirectory, NodePackageRunner.Options? options = null)
    : NodePackageRunner(yarnPath, workingDirectory, options)
{
    private const string yarnPath = "yarn";

    public async override Task<string> InstallAsync(IReadOnlyCollection<string>? packages = null, CancelToken token = default)
    {
        var a = new List<string>();

        if (packages is null || packages.Count == 0)
            a.Add("install");
        else
        {
            a.Add("add");
            a.AddRange(packages);
        }

        var (_, stdout, _) = await this.ExecuteAsync(a, token);

        return stdout;
    }

    protected async override Task<RunResult> ExecuteAsync(List<string> args, CancelToken token)
    {
        if (this.OptionsInternal.Verbose)
            args.Add("--verbose");

        if (this.OptionsInternal.Force)
            args.Add("--force");

        if (this.OptionsInternal.Global)
            args.Insert(0, "global");

        return await this.RunInternalAsync(args, "", token);
    }
}
