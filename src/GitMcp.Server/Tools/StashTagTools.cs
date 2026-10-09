using System.ComponentModel;
using GitMcp.Server.Git;
using ModelContextProtocol.Server;
using static GitMcp.Server.Tools.RepositoryTools;

namespace GitMcp.Server.Tools;

[McpServerToolType]
public sealed class StashTools(GitService git)
{
    private const string StashDescription = "Stash entry, e.g. stash@{0} or 0. Default: the latest.";

    [McpServerTool(Name = "git_stash_push", Destructive = false)]
    [Description("Save local changes to a new stash entry and clean the working tree (git stash push).")]
    public Task<string> Push(
        [Description(RepoDescription)] string repoPath,
        [Description("Description for the stash entry.")] string? message = null,
        [Description("Also stash untracked files (-u).")] bool includeUntracked = false,
        [Description("Leave staged changes in the index (--keep-index).")] bool keepIndex = false,
        [Description("Stash only staged changes (--staged).")] bool staged = false,
        [Description("Only stash these paths.")] string[]? paths = null,
        CancellationToken ct = default)
    {
        var args = new GitArgs("stash", "push")
            .Flag(includeUntracked, "--include-untracked")
            .Flag(keepIndex, "--keep-index")
            .Flag(staged, "--staged")
            .Opt("--message", message)
            .Paths(paths);
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_stash_pop", Destructive = false)]
    [Description("Apply a stash entry and remove it from the stash list (git stash pop).")]
    public Task<string> Pop(
        [Description(RepoDescription)] string repoPath,
        [Description(StashDescription)] string? stash = null,
        [Description("Also restore the staged state (--index).")] bool index = false,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("stash", "pop").Flag(index, "--index").Ref(Entry(stash), "stash"), ct);

    [McpServerTool(Name = "git_stash_apply", Destructive = false)]
    [Description("Apply a stash entry but keep it in the stash list (git stash apply).")]
    public Task<string> Apply(
        [Description(RepoDescription)] string repoPath,
        [Description(StashDescription)] string? stash = null,
        [Description("Also restore the staged state (--index).")] bool index = false,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("stash", "apply").Flag(index, "--index").Ref(Entry(stash), "stash"), ct);

    [McpServerTool(Name = "git_stash_list", ReadOnly = true)]
    [Description("List stash entries.")]
    public Task<string> List(
        [Description(RepoDescription)] string repoPath,
        CancellationToken ct = default) =>
        git.ReadAsync(repoPath, new GitArgs("stash", "list"), ct);

    [McpServerTool(Name = "git_stash_show", ReadOnly = true)]
    [Description("Show the changes recorded in a stash entry.")]
    public Task<string> Show(
        [Description(RepoDescription)] string repoPath,
        [Description(StashDescription)] string? stash = null,
        [Description("Show the full patch instead of a summary.")] bool patch = false,
        CancellationToken ct = default) =>
        git.ReadAsync(repoPath, new GitArgs("stash", "show", patch ? "--patch" : "--stat").Ref(Entry(stash), "stash"), ct);

    [McpServerTool(Name = "git_stash_drop", Destructive = true)]
    [Description("Delete a stash entry (git stash drop).")]
    public Task<string> Drop(
        [Description(RepoDescription)] string repoPath,
        [Description(StashDescription)] string? stash = null,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("stash", "drop").Ref(Entry(stash), "stash"), ct);

    /// <summary>Accept "2" as shorthand for "stash@{2}".</summary>
    private static string? Entry(string? stash) =>
        string.IsNullOrWhiteSpace(stash) ? null : int.TryParse(stash, out var n) ? $"stash@{{{n}}}" : stash;
}

[McpServerToolType]
public sealed class TagTools(GitService git)
{
    [McpServerTool(Name = "git_tag_list", ReadOnly = true)]
    [Description("List tags.")]
    public Task<string> List(
        [Description(RepoDescription)] string repoPath,
        [Description("Only tags matching this glob, e.g. 'v1.*'.")] string? pattern = null,
        [Description("Show the first line of each tag's message (-n).")] bool withMessages = false,
        [Description("Sort by version number, newest first.")] bool sortByVersion = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("tag", "--list").Flag(withMessages, "-n").Flag(sortByVersion, "--sort=-version:refname").Ref(pattern, "pattern");
        return git.ReadAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_tag_create", Destructive = false)]
    [Description("Create a tag. With a message it is an annotated tag, otherwise lightweight.")]
    public Task<string> Create(
        [Description(RepoDescription)] string repoPath,
        [Description("Tag name, e.g. v1.2.0.")] string name,
        [Description("Commit to tag. Default: HEAD.")] string? target = null,
        [Description("Annotation message (creates an annotated tag).")] string? message = null,
        [Description("Replace an existing tag (-f).")] bool force = false,
        CancellationToken ct = default)
    {
        var args = new GitArgs("tag").Flag(force, "--force");
        if (!string.IsNullOrEmpty(message))
        {
            args.Add("--annotate");
            args.Add($"--message={message}");
        }
        args.Ref(name, "tag name").Ref(target, "revision");
        return git.WriteAsync(repoPath, args, ct);
    }

    [McpServerTool(Name = "git_tag_delete", Destructive = true)]
    [Description("Delete a local tag. To delete it on a remote use git_push with delete.")]
    public Task<string> Delete(
        [Description(RepoDescription)] string repoPath,
        [Description("Tag to delete.")] string name,
        CancellationToken ct = default) =>
        git.WriteAsync(repoPath, new GitArgs("tag", "--delete").Ref(name, "tag name"), ct);
}
