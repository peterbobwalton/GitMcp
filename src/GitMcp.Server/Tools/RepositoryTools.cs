using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GitMcp.Server.Tools;

/// <summary>Creating repositories and reading history.</summary>
[McpServerToolType]
public sealed class RepositoryTools(GitService git)
{
    internal const string RepoDescription = "Absolute path of the repository (its working tree, or any folder inside it).";

    [McpServerTool(Name = "git_init", Destructive = false, OpenWorld = false)]
    [Description("Create an empty git repository (git init). The folder is created if it does not exist.")]
    public Task<string> Init(
        [Description("Absolute path of the folder to initialize.")] string path,
        [Description("Create a bare repository.")] bool bare = false,
        [Description("Name of the initial branch, e.g. main.")] string? initialBranch = null,
        CancellationToken ct = default)
    {
        var dir = git.ResolvePath(path);
        var args = new GitArgs("init").Flag(bare, "--bare").Opt("--initial-branch", initialBranch is null ? null : GitArgs.CheckRef(initialBranch, "branch"));
        return git.CreateAsync(dir, dir, args, ct);
    }

    [McpServerTool(Name = "git_clone", Destructive = false, OpenWorld = true)]
    [Description("Clone a repository into a new folder (git clone).")]
    public Task<string> Clone(
        [Description("Repository URL or path to clone from.")] string url,
        [Description("Absolute path of the folder to clone into (must not exist or be empty).")] string destination,
        [Description("Branch or tag to check out instead of the remote HEAD.")] string? branch = null,
        [Description("Create a shallow clone with this many commits.")] int? depth = null,
        [Description("Make a bare clone.")] bool bare = false,
        [Description("Also clone submodules.")] bool recurseSubmodules = false,
        CancellationToken ct = default)
    {
        var dest = git.ResolvePath(destination);
        var parent = Path.GetDirectoryName(dest) ?? throw new McpException("The destination cannot be a drive root.");
        var args = new GitArgs("clone", "--progress")
            .Flag(bare, "--bare")
            .Flag(recurseSubmodules, "--recurse-submodules")
            .Opt("--depth", depth)
            .Opt("--branch", branch is null ? null : GitArgs.CheckRef(branch, "branch"));
        args.Add("--");
        args.Add(GitArgs.CheckRef(url, "url"));
        args.Add(dest);
        return git.CreateAsync(parent, dest, args, ct, network: true);
    }

    [McpServerTool(Name = "git_status", ReadOnly = true)]
    [Description("Show the working tree status: current branch, upstream, staged, unstaged and untracked files.")]
    public Task<string> Status(
        [Description(RepoDescription)] string repoPath,
        [Description("Compact one-line-per-file output (git status --short --branch).")] bool @short = false,
        [Description("Also list ignored files.")] bool ignored = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("status").Flag(@short, "--short").Flag(@short, "--branch").Flag(ignored, "--ignored");
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_log", ReadOnly = true)]
    [Description("Show commit history (git log).")]
    public Task<string> Log(
        [Description(RepoDescription)] string repoPath,
        [Description("Maximum number of commits.")] int maxCount = 20,
        [Description("Revision or range, e.g. main, HEAD~5..HEAD, origin/main..feature. Default: HEAD.")] string? revision = null,
        [Description("Only commits touching these paths.")] string[]? paths = null,
        [Description("One line per commit (hash and subject).")] bool oneline = false,
        [Description("Custom pretty format, e.g. '%h %an %ad %s'. Overrides oneline.")] string? format = null,
        [Description("Include changed-file statistics.")] bool stat = false,
        [Description("Commits from all branches.")] bool all = false,
        [Description("Filter by author (regex).")] string? author = null,
        [Description("Filter by commit message (regex).")] string? grep = null,
        [Description("Commits newer than this date, e.g. '2 weeks ago' or 2026-01-31.")] string? since = null,
        [Description("Commits older than this date.")] string? until = null,
        [Description("Draw the branch graph.")] bool graph = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("log").Opt("--max-count", maxCount)
            .Flag(oneline && format is null, "--oneline")
            .Opt("--format", format)
            .Flag(stat, "--stat")
            .Flag(all, "--all")
            .Flag(graph, "--graph")
            .Opt("--author", author)
            .Opt("--grep", grep)
            .Opt("--since", since)
            .Opt("--until", until)
            .Ref(revision, "revision");
        if (paths is { Length: > 0 })
        {
            args.Paths(paths);
        }
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_show", ReadOnly = true)]
    [Description("Show a commit (message and patch), or the content of a file at a revision when path is given.")]
    public Task<string> Show(
        [Description(RepoDescription)] string repoPath,
        [Description("Commit, tag or other revision. Default: HEAD.")] string revision = "HEAD",
        [Description("If set, print this file as it was at the revision (path relative to the repository root).")] string? path = null,
        [Description("Only the change summary (--stat), no patch.")] bool stat = false,
        [Description("Only changed file names with status (--name-status), no patch.")] bool nameOnly = false,
        CancellationToken ct = default)
    {
        var rev = GitArgs.CheckRef(revision, "revision");
        var args = new GitArgs("show");
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add($"{rev}:{path.Replace('\\', '/')}");
        }
        else
        {
            args.Flag(stat, "--stat").Flag(nameOnly, "--name-status").Add(rev);
        }
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_diff", ReadOnly = true)]
    [Description("Show changes. No revisions: unstaged changes. staged: staged changes. One revision: working tree vs that revision. Two: between them.")]
    public Task<string> Diff(
        [Description(RepoDescription)] string repoPath,
        [Description("Diff the index (staged changes) instead of the working tree.")] bool staged = false,
        [Description("First revision, e.g. HEAD~1, main, or a range like main...feature.")] string? @base = null,
        [Description("Second revision to compare base against.")] string? target = null,
        [Description("Limit to these paths.")] string[]? paths = null,
        [Description("Summary only (--stat).")] bool stat = false,
        [Description("Changed file names with status only (--name-status).")] bool nameOnly = false,
        [Description("Lines of context around each change.")] int? contextLines = null,
        [Description("Ignore whitespace changes (-w).")] bool ignoreWhitespace = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("diff")
            .Flag(staged, "--cached")
            .Flag(stat, "--stat")
            .Flag(nameOnly, "--name-status")
            .Flag(ignoreWhitespace, "-w");
        if (contextLines is not null)
        {
            args.Add($"-U{contextLines.Value}");
        }
        args.Ref(@base, "revision").Ref(target, "revision").Paths(paths);
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_blame", ReadOnly = true)]
    [Description("Show who last changed each line of a file (git blame).")]
    public Task<string> Blame(
        [Description(RepoDescription)] string repoPath,
        [Description("File path relative to the repository root.")] string path,
        [Description("Blame as of this revision. Default: working tree.")] string? revision = null,
        [Description("First line (1-based).")] int? startLine = null,
        [Description("Last line (inclusive).")] int? endLine = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("blame");
        if (startLine is not null || endLine is not null)
        {
            args.Add($"-L{startLine ?? 1},{(endLine is null ? "" : endLine.Value.ToString())}");
        }
        args.Ref(revision, "revision").Paths([path]);
        return git.ReadAsync(repoPath, args, ct);
    }
}
