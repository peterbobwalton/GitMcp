using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static GitMcp.Server.Tools.RepositoryTools;

namespace GitMcp.Server.Tools;

/// <summary>Staging, committing and undoing changes in the working tree.</summary>
[McpServerToolType]
public sealed class WorkTreeTools(GitService git)
{
    [McpServerTool(Name = "git_add", Destructive = false)]
    [Description("Stage changes (git add).")]
    public Task<string> Add(
        [Description(RepoDescription)] string repoPath,
        [Description("Files or pathspecs to stage, relative to the repository root. Use [\".\"] for everything under the root.")] string[]? paths = null,
        [Description("Stage all changes including deletions and untracked files (-A).")] bool all = false,
        [Description("Stage modifications and deletions of tracked files only (-u).")] bool update = false,
        CancellationToken ct = default)
    {
        if (!all && !update && paths is not { Length: > 0 })
        {
            throw new McpException("Pass paths, or set all or update.");
        }
        var args = new GitArgs("add").Flag(all, "--all").Flag(update, "--update").Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_restore", Destructive = true)]
    [Description("Restore files (git restore). Default: discard unstaged changes in the working tree. staged=true unstages instead.")]
    public Task<string> Restore(
        [Description(RepoDescription)] string repoPath,
        [Description("Files or pathspecs to restore.")] string[] paths,
        [Description("Restore the index (unstage).")] bool staged = false,
        [Description("Also restore the working tree when staged is true (discards the changes).")] bool worktree = false,
        [Description("Restore content from this revision instead of the index/HEAD.")] string? source = null,
        CancellationToken ct = default)
    {
        if (paths is not { Length: > 0 })
        {
            throw new McpException("Pass the paths to restore.");
        }
        var args = new GitArgs("restore").Flag(staged, "--staged").Flag(staged && worktree, "--worktree")
            .Opt("--source", source is null ? null : GitArgs.CheckRef(source, "revision"))
            .Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_rm", Destructive = true)]
    [Description("Remove files from the index and the working tree (git rm).")]
    public Task<string> Remove(
        [Description(RepoDescription)] string repoPath,
        [Description("Files or pathspecs to remove.")] string[] paths,
        [Description("Only untrack: keep the files on disk (--cached).")] bool cached = false,
        [Description("Allow removing directories (-r).")] bool recursive = false,
        [Description("Remove even with local modifications (-f).")] bool force = false,
        CancellationToken ct = default)
    {
        if (paths is not { Length: > 0 })
        {
            throw new McpException("Pass the paths to remove.");
        }
        var args = new GitArgs("rm").Flag(cached, "--cached").Flag(recursive, "-r").Flag(force, "--force").Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_mv", Destructive = false)]
    [Description("Move or rename a tracked file or folder (git mv).")]
    public Task<string> Move(
        [Description(RepoDescription)] string repoPath,
        [Description("Current path, relative to the repository root.")] string source,
        [Description("New path.")] string destination,
        [Description("Overwrite an existing destination (-f).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("mv").Flag(force, "--force").Paths([source, destination]);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_commit", Destructive = false)]
    [Description("Record staged changes as a commit (git commit).")]
    public Task<string> Commit(
        [Description(RepoDescription)] string repoPath,
        [Description("Commit message. Optional only with amend (keeps the old message).")] string? message = null,
        [Description("Stage modified and deleted tracked files first (-a).")] bool all = false,
        [Description("Replace the last commit (--amend).")] bool amend = false,
        [Description("Allow a commit with no changes.")] bool allowEmpty = false,
        [Description("Override the author, e.g. 'Jane Doe <jane@example.com>'.")] string? author = null,
        [Description("Skip pre-commit and commit-msg hooks.")] bool noVerify = false,
        [Description("Add a Signed-off-by trailer.")] bool signoff = false,
        [Description("Commit only these paths (their current content), ignoring other staged changes.")] string[]? paths = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(message) && !amend)
        {
            throw new McpException("A commit message is required.");
        }
        var args = new GitArgs("commit")
            .Flag(all, "--all")
            .Flag(amend, "--amend")
            .Flag(allowEmpty, "--allow-empty")
            .Flag(noVerify, "--no-verify")
            .Flag(signoff, "--signoff")
            .Opt("--author", author);
        if (string.IsNullOrEmpty(message))
        {
            args.Add("--no-edit");
        }
        else
        {
            args.Add($"--message={message}");
        }
        args.Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_reset", Destructive = true)]
    [Description("Move the current branch to a revision (git reset). With paths: unstage those paths (mode is ignored).")]
    public Task<string> Reset(
        [Description(RepoDescription)] string repoPath,
        [Description("soft (keep index and files), mixed (keep files, reset index), hard (discard everything), keep or merge.")] string mode = "mixed",
        [Description("Revision to reset to. Default: HEAD.")] string? target = null,
        [Description("Only reset these paths in the index.")] string[]? paths = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("reset");
        if (paths is { Length: > 0 })
        {
            args.Ref(target, "revision").Paths(paths);
        }
        else
        {
            var m = mode.Trim().ToLowerInvariant();
            if (m is not ("soft" or "mixed" or "hard" or "keep" or "merge"))
            {
                throw new McpException($"Unknown reset mode '{mode}'. Use soft, mixed, hard, keep or merge.");
            }
            args.Add("--" + m);
            args.Ref(target, "revision");
        }
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_clean", Destructive = true)]
    [Description("Delete untracked files (git clean). Runs as a dry run unless dryRun is false.")]
    public Task<string> Clean(
        [Description(RepoDescription)] string repoPath,
        [Description("Only list what would be deleted (default true). Set false to actually delete.")] bool dryRun = true,
        [Description("Also remove untracked directories (-d).")] bool directories = false,
        [Description("Also remove ignored files (-x).")] bool ignored = false,
        [Description("Remove only ignored files (-X).")] bool onlyIgnored = false,
        [Description("Limit to these paths.")] string[]? paths = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("clean", dryRun ? "--dry-run" : "--force")
            .Flag(directories, "-d")
            .Flag(ignored && !onlyIgnored, "-x")
            .Flag(onlyIgnored, "-X")
            .Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }
}
