using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static GitMcp.Server.Tools.RepositoryTools;

namespace GitMcp.Server.Tools;

/// <summary>Branches, switching, and combining history (merge, rebase, cherry-pick, revert).</summary>
[McpServerToolType]
public sealed class BranchTools(GitService git)
{
    private const string ActionDescription =
        "start (default) begins the operation; continue, abort, skip or quit act on one that stopped on conflicts.";

    [McpServerTool(Name = "git_branch_list", ReadOnly = true)]
    [Description("List branches. The current branch is marked with '*'.")]
    public Task<string> List(
        [Description(RepoDescription)] string repoPath,
        [Description("Include remote-tracking branches (-a).")] bool all = false,
        [Description("Only remote-tracking branches (-r).")] bool remotes = false,
        [Description("Show last commit and upstream/ahead/behind (-vv).")] bool verbose = false,
        [Description("Only branches matching this glob, e.g. 'feature/*'.")] string? pattern = null,
        [Description("Only branches merged into this revision.")] string? merged = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("branch")
            .Flag(all, "--all")
            .Flag(remotes && !all, "--remotes")
            .Flag(verbose, "-vv")
            .Opt("--merged", merged is null ? null : GitArgs.CheckRef(merged, "revision"));
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            args.Add("--list");
            args.Ref(pattern, "pattern");
        }
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_branch_create", Destructive = false)]
    [Description("Create a branch without switching to it (use git_switch with create to do both).")]
    public Task<string> Create(
        [Description(RepoDescription)] string repoPath,
        [Description("New branch name.")] string name,
        [Description("Commit or branch to start from. Default: HEAD.")] string? startPoint = null,
        [Description("Reset the branch if it already exists (-f).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("branch").Flag(force, "--force").Ref(name, "branch name").Ref(startPoint, "start point");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_branch_delete", Destructive = true)]
    [Description("Delete a local branch (-d, or -D with force). To delete a remote branch use git_push with delete.")]
    public Task<string> Delete(
        [Description(RepoDescription)] string repoPath,
        [Description("Branch to delete.")] string name,
        [Description("Delete even if not fully merged (-D).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("branch", force ? "-D" : "-d").Ref(name, "branch name");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_branch_rename", Destructive = false)]
    [Description("Rename a branch (git branch -m). Renames the current branch when oldName is omitted.")]
    public Task<string> Rename(
        [Description(RepoDescription)] string repoPath,
        [Description("New name.")] string newName,
        [Description("Branch to rename. Default: current branch.")] string? oldName = null,
        [Description("Overwrite an existing branch with the new name (-M).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("branch", force ? "-M" : "-m").Ref(oldName, "branch name").Ref(newName, "branch name");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_checkout", Destructive = true)]
    [Description("git checkout: switch to a branch/commit, or with paths restore those files from a revision (default: index).")]
    public Task<string> Checkout(
        [Description(RepoDescription)] string repoPath,
        [Description("Branch, tag or commit. Optional when paths are given.")] string? target = null,
        [Description("Create this new branch at target and switch to it (-b).")] string? newBranch = null,
        [Description("Restore these paths instead of switching branches.")] string[]? paths = null,
        [Description("Throw away local changes when switching (-f).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("checkout").Flag(force, "--force");
        if (paths is { Length: > 0 })
        {
            args.Ref(target, "revision").Paths(paths);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(newBranch))
            {
                throw new McpException("Pass target, newBranch or paths.");
            }
            if (!string.IsNullOrWhiteSpace(newBranch))
            {
                args.Add("-b");
                args.Ref(newBranch, "branch name");
            }
            args.Ref(target, "revision");
        }
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_switch", Destructive = false)]
    [Description("Switch branches (git switch), optionally creating the branch.")]
    public Task<string> Switch(
        [Description(RepoDescription)] string repoPath,
        [Description("Branch to switch to (or to create). With detach: any commit.")] string branch,
        [Description("Create the branch (-c).")] bool create = false,
        [Description("Create or reset the branch (-C).")] bool forceCreate = false,
        [Description("Where a created branch starts. Default: HEAD.")] string? startPoint = null,
        [Description("Detach HEAD at the commit instead of switching to a branch.")] bool detach = false,
        [Description("Discard local changes that would block the switch.")] bool discardChanges = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("switch")
            .Flag(discardChanges, "--discard-changes")
            .Flag(detach, "--detach")
            .Flag(forceCreate, "-C")
            .Flag(create && !forceCreate, "-c")
            .Ref(branch, "branch")
            .Ref(startPoint, "start point");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_merge", Destructive = false)]
    [Description("Merge branches into the current branch, or continue/abort a merge that stopped on conflicts.")]
    public Task<string> Merge(
        [Description(RepoDescription)] string repoPath,
        [Description("Branches or commits to merge (for action start).")] string[]? branches = null,
        [Description(ActionDescription)] string action = "start",
        [Description("Always create a merge commit (--no-ff).")] bool noFastForward = false,
        [Description("Refuse unless it can fast-forward (--ff-only).")] bool fastForwardOnly = false,
        [Description("Squash the changes into the index without committing (--squash).")] bool squash = false,
        [Description("Stop before committing (--no-commit).")] bool noCommit = false,
        [Description("Merge commit message.")] string? message = null,
        [Description("Strategy option, e.g. ours or theirs (-X).")] string? strategyOption = null,
        CancellationToken ct = default)
    {
        if (Sequencer("merge", action, ["continue", "abort", "quit"]) is { } control)
        {
            return git.WriteAsync(repoPath, control, ct);
        }
        if (branches is not { Length: > 0 })
        {
            throw new McpException("Pass the branches to merge.");
        }
        var args = new GitArgs("merge")
            .Flag(noFastForward, "--no-ff")
            .Flag(fastForwardOnly, "--ff-only")
            .Flag(squash, "--squash")
            .Flag(noCommit, "--no-commit")
            .Opt("--strategy-option", strategyOption)
            .Opt("--message", message)
            .Refs(branches, "branch");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_rebase", Destructive = true)]
    [Description("Rebase the current branch onto another (non-interactive), or continue/skip/abort a rebase in progress.")]
    public Task<string> Rebase(
        [Description(RepoDescription)] string repoPath,
        [Description("Upstream to rebase onto, e.g. main or origin/main (for action start).")] string? upstream = null,
        [Description(ActionDescription)] string action = "start",
        [Description("Transplant onto this commit instead of upstream (--onto).")] string? onto = null,
        [Description("Branch to rebase. Default: current branch.")] string? branch = null,
        [Description("Stash local changes before and restore them after (--autostash).")] bool autostash = false,
        [Description("Keep merge commits (--rebase-merges).")] bool rebaseMerges = false,
        CancellationToken ct = default)
    {
        if (Sequencer("rebase", action, ["continue", "abort", "skip", "quit"]) is { } control)
        {
            return git.WriteAsync(repoPath, control, ct);
        }
        if (string.IsNullOrWhiteSpace(upstream) && string.IsNullOrWhiteSpace(onto))
        {
            throw new McpException("Pass upstream (or onto).");
        }
        var args = new GitArgs("rebase")
            .Flag(autostash, "--autostash")
            .Flag(rebaseMerges, "--rebase-merges")
            .Opt("--onto", onto is null ? null : GitArgs.CheckRef(onto, "revision"))
            .Ref(upstream, "upstream")
            .Ref(branch, "branch");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_cherry_pick", Destructive = false)]
    [Description("Apply existing commits onto the current branch, or continue/skip/abort a cherry-pick in progress.")]
    public Task<string> CherryPick(
        [Description(RepoDescription)] string repoPath,
        [Description("Commits or ranges to apply, oldest first (for action start).")] string[]? commits = null,
        [Description(ActionDescription)] string action = "start",
        [Description("Apply changes without committing (-n).")] bool noCommit = false,
        [Description("Append '(cherry picked from commit ...)' to the message (-x).")] bool recordOrigin = false,
        [Description("Parent number to diff against when picking a merge commit (-m).")] int? mainline = null,
        CancellationToken ct = default)
    {
        if (Sequencer("cherry-pick", action, ["continue", "abort", "skip", "quit"]) is { } control)
        {
            return git.WriteAsync(repoPath, control, ct);
        }
        if (commits is not { Length: > 0 })
        {
            throw new McpException("Pass the commits to cherry-pick.");
        }
        var args = new GitArgs("cherry-pick").Flag(noCommit, "--no-commit").Flag(recordOrigin, "-x").Opt("--mainline", mainline)
            .Refs(commits, "commit");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_revert", Destructive = false)]
    [Description("Create commits that undo earlier commits, or continue/skip/abort a revert in progress.")]
    public Task<string> Revert(
        [Description(RepoDescription)] string repoPath,
        [Description("Commits to revert (for action start).")] string[]? commits = null,
        [Description(ActionDescription)] string action = "start",
        [Description("Apply the inverse changes without committing (-n).")] bool noCommit = false,
        [Description("Parent number when reverting a merge commit (-m).")] int? mainline = null,
        CancellationToken ct = default)
    {
        if (Sequencer("revert", action, ["continue", "abort", "skip", "quit"]) is { } control)
        {
            return git.WriteAsync(repoPath, control, ct);
        }
        if (commits is not { Length: > 0 })
        {
            throw new McpException("Pass the commits to revert.");
        }
        var args = new GitArgs("revert", "--no-edit").Flag(noCommit, "--no-commit").Opt("--mainline", mainline)
            .Refs(commits, "commit");
        return git.WriteAsync(repoPath, args, ct);
    }

    /// <summary>Arguments for a control action (continue/abort/...), or null for "start".</summary>
    private static GitArgs? Sequencer(string command, string action, string[] allowed)
    {
        var a = (action ?? "start").Trim().ToLowerInvariant();
        if (a is "" or "start")
        {
            return null;
        }
        if (!allowed.Contains(a))
        {
            throw new McpException($"Unknown action '{action}' for {command}. Use start, {string.Join(", ", allowed)}.");
        }
        return new GitArgs(command, "--" + a);
    }
}
