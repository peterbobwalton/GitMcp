using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static GitMcp.Server.Tools.RepositoryTools;

namespace GitMcp.Server.Tools;

/// <summary>Remotes and synchronizing with them.</summary>
[McpServerToolType]
public sealed class RemoteTools(GitService git)
{
    [McpServerTool(Name = "git_fetch", Destructive = false, OpenWorld = true)]
    [Description("Download objects and refs from a remote without changing local branches (git fetch).")]
    public Task<string> Fetch(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name. Default: the branch's upstream remote, usually origin.")] string? remote = null,
        [Description("Refspecs or branch names to fetch.")] string[]? refspecs = null,
        [Description("Fetch all remotes (--all).")] bool all = false,
        [Description("Remove remote-tracking refs that no longer exist on the remote (--prune).")] bool prune = false,
        [Description("Also fetch all tags (--tags).")] bool tags = false,
        [Description("Limit history depth (--depth).")] int? depth = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("fetch", "--progress").Flag(all, "--all").Flag(prune, "--prune").Flag(tags, "--tags").Opt("--depth", depth);
        if (!all)
        {
            args.Ref(remote, "remote").Refs(refspecs, "refspec");
        }
        return git.WriteAsync(repoPath, args, ct, network: true);
    }

    [McpServerTool(Name = "git_pull", Destructive = false, OpenWorld = true)]
    [Description("Fetch and integrate a remote branch into the current branch (git pull).")]
    public Task<string> Pull(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name. Default: the upstream remote.")] string? remote = null,
        [Description("Remote branch. Default: the upstream branch.")] string? branch = null,
        [Description("Rebase instead of merge (--rebase).")] bool rebase = false,
        [Description("Only fast-forward (--ff-only).")] bool fastForwardOnly = false,
        [Description("Stash local changes before and restore them after (--autostash).")] bool autostash = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(branch) && string.IsNullOrWhiteSpace(remote))
        {
            throw new McpException("Pass remote together with branch.");
        }
        var args = new GitArgs("pull", "--progress", rebase ? "--rebase" : "--no-rebase")
            .Flag(fastForwardOnly, "--ff-only")
            .Flag(autostash, "--autostash")
            .Ref(remote, "remote")
            .Ref(branch, "branch");
        return git.WriteAsync(repoPath, args, ct, network: true);
    }

    [McpServerTool(Name = "git_push", Destructive = true, OpenWorld = true)]
    [Description("Upload local commits to a remote (git push). Also deletes remote branches/tags with delete.")]
    public Task<string> Push(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name. Default: the upstream remote, usually origin.")] string? remote = null,
        [Description("Branches, tags or refspecs (src:dst) to push. Default: the current branch's push target.")] string[]? refspecs = null,
        [Description("Set the pushed branch as upstream (-u).")] bool setUpstream = false,
        [Description("Overwrite remote history only if it is what we last fetched (--force-with-lease).")] bool forceWithLease = false,
        [Description("Overwrite remote history unconditionally (--force). Prefer forceWithLease.")] bool force = false,
        [Description("Push all tags (--tags).")] bool tags = false,
        [Description("Delete the given refspecs on the remote (--delete).")] bool delete = false,
        [Description("Show what would be pushed without pushing (--dry-run).")] bool dryRun = false,
        CancellationToken ct = default)
    {
        if ((setUpstream || delete) && string.IsNullOrWhiteSpace(remote))
        {
            remote = "origin";
        }
        if (delete && refspecs is not { Length: > 0 })
        {
            throw new McpException("Pass the branches or tags to delete in refspecs.");
        }
        var args = new GitArgs("push", "--progress")
            .Flag(setUpstream, "--set-upstream")
            .Flag(forceWithLease && !force, "--force-with-lease")
            .Flag(force, "--force")
            .Flag(tags, "--tags")
            .Flag(delete, "--delete")
            .Flag(dryRun, "--dry-run")
            .Ref(remote, "remote")
            .Refs(refspecs, "refspec");
        return git.WriteAsync(repoPath, args, ct, network: true);
    }

    [McpServerTool(Name = "git_remote_list", ReadOnly = true)]
    [Description("List remotes with their fetch and push URLs.")]
    public Task<string> List(
        [Description(RepoDescription)] string repoPath,
        CancellationToken ct = default) =>
        git.ReadAsync(repoPath, new GitArgs("remote", "--verbose"), ct);

    [McpServerTool(Name = "git_remote_add", Destructive = false)]
    [Description("Add a remote.")]
    public Task<string> Add(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name, e.g. origin or upstream.")] string name,
        [Description("Remote URL.")] string url,
        [Description("Fetch it right away (-f).")] bool fetch = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("remote", "add").Flag(fetch, "--fetch").Ref(name, "remote name").Ref(url, "url");
        return git.WriteAsync(repoPath, args, ct, network: fetch);
    }

    [McpServerTool(Name = "git_remote_remove", Destructive = true)]
    [Description("Remove a remote and its remote-tracking branches.")]
    public Task<string> Remove(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name.")] string name,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("remote", "remove").Ref(name, "remote name"), ct);

    [McpServerTool(Name = "git_remote_set_url", Destructive = false)]
    [Description("Change a remote's URL.")]
    public Task<string> SetUrl(
        [Description(RepoDescription)] string repoPath,
        [Description("Remote name.")] string name,
        [Description("New URL.")] string url,
        [Description("Change only the push URL (--push).")] bool push = false,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("remote", "set-url").Flag(push, "--push").Ref(name, "remote name").Ref(url, "url"), ct);
}
