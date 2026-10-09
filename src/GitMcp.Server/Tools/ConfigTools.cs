using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GitMcp.Server.Tools;

/// <summary>git config, plus a raw escape hatch for anything the typed tools don't cover.</summary>
[McpServerToolType]
public sealed class ConfigTools(GitService git)
{
    private const string ScopeDescription = "local (default, needs repoPath), global (user), system, or worktree.";
    private const string ConfigRepoDescription = "Absolute repository path. Required for local scope; optional for global/system.";

    [McpServerTool(Name = "git_config_get", ReadOnly = true)]
    [Description("Read a git config value. Without scope, returns the effective value (local overrides global overrides system).")]
    public async Task<string> Get(
        [Description("Config key, e.g. user.name or remote.origin.url.")] string key,
        [Description(ConfigRepoDescription)] string? repoPath = null,
        [Description(ScopeDescription + " Omit for the effective value.")] string? scope = null,
        [Description("Return every value of a multi-valued key (--get-all).")] bool all = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("config").Flag(!string.IsNullOrWhiteSpace(scope), ScopeFlag(scope)).Flag(true, all ? "--get-all" : "--get");
        var result = await RunAsync(repoPath, scope, write: false, [.. args, GitArgs.CheckRef(key, "key")], ct, okExitCodes: [1]);
        return result == "(done, no output)" ? $"{key} is not set." : result;
    }

    [McpServerTool(Name = "git_config_set", Destructive = false)]
    [Description("Set (or with unset, remove) a git config value.")]
    public Task<string> Set(
        [Description("Config key, e.g. user.email.")] string key,
        [Description("Value to set. Ignored with unset.")] string? value = null,
        [Description(ConfigRepoDescription)] string? repoPath = null,
        [Description(ScopeDescription)] string scope = "local",
        [Description("Remove the key instead (--unset).")] bool unset = false,
        CancellationToken ct = default)
    {
        if (!unset && value is null)
        {
            throw new McpException("Pass value, or set unset.");
        }
        var args = new GitArgs("config").Flag(!string.IsNullOrWhiteSpace(scope), ScopeFlag(scope));
        if (unset)
        {
            args.Add("--unset");
            args.Add(GitArgs.CheckRef(key, "key"));
        }
        else
        {
            args.Add(GitArgs.CheckRef(key, "key"));
            args.Add(value!);
        }
        return RunAsync(repoPath, scope, write: true, args, ct);
    }

    [McpServerTool(Name = "git_config_list", ReadOnly = true)]
    [Description("List config values with the file each comes from.")]
    public Task<string> List(
        [Description(ConfigRepoDescription)] string? repoPath = null,
        [Description(ScopeDescription + " Omit for all scopes.")] string? scope = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("config", "--list", "--show-origin").Flag(!string.IsNullOrWhiteSpace(scope), ScopeFlag(scope));
        return RunAsync(repoPath, scope, write: false, args, ct);
    }

    [McpServerTool(Name = "git_run", Destructive = true, OpenWorld = true)]
    [Description("Run any other git command, e.g. [\"rev-parse\", \"HEAD\"] or [\"worktree\", \"list\"]. " +
                 "Do not include 'git' or '-C'. Interactive commands (rebase -i, add -p, editors) are not supported.")]
    public Task<string> Run(
        [Description(RepositoryTools.RepoDescription)] string repoPath,
        [Description("Arguments after 'git'.")] string[] args,
        [Description("True if the command only reads, so it can run in parallel with other reads.")] bool readOnly = false,
        CancellationToken ct = default)
    {
        if (args is not { Length: > 0 })
        {
            throw new McpException("Pass the git arguments.");
        }
        // Global options (-C, --git-dir, -c ...) would let a call escape repoPath and the allowed roots.
        if (args[0].StartsWith('-'))
        {
            throw new McpException("Start args with the git subcommand (e.g. \"rev-parse\"). Global options such as -C, -c " +
                                   "or --git-dir are not allowed; use repoPath to choose the repository.");
        }
        return readOnly ? git.ReadAsync(repoPath, args, ct) : git.WriteAsync(repoPath, args, ct, network: true);
    }

    private Task<string> RunAsync(string? repoPath, string? scope, bool write, IEnumerable<string> args, CancellationToken ct, int[]? okExitCodes = null)
    {
        var s = scope?.Trim().ToLowerInvariant();
        // Global and system config are shared by every repository, so they get one lock of their own whatever
        // repoPath says, and run where no repository can interfere.
        if (s is "global" or "system")
        {
            return git.RunInAsync(Path.GetTempPath(), $"<{s}-config>", write, args, ct, okExitCodes);
        }
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            if (write || !string.IsNullOrEmpty(s))
            {
                throw new McpException("repoPath is required unless scope is global or system.");
            }
            // Effective value outside any repository: global + system.
            return git.RunInAsync(Path.GetTempPath(), "<global-config>", write: false, args, ct, okExitCodes);
        }
        return write ? git.WriteAsync(repoPath, args, ct) : git.ReadAsync(repoPath, args, ct, okExitCodes);
    }

    private static string ScopeFlag(string? scope) => (scope?.Trim().ToLowerInvariant()) switch
    {
        null or "" => "",
        "local" => "--local",
        "global" => "--global",
        "system" => "--system",
        "worktree" => "--worktree",
        _ => throw new McpException($"Unknown scope '{scope}'. Use local, global, system or worktree."),
    };
}
