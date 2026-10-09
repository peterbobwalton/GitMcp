using GitMcp.Server;
using GitMcp.Server.Git;
using GitMcp.Server.Tools;
using Microsoft.Extensions.Options;

namespace GitMcp.Tests;

/// <summary>A throwaway folder with a GitService and every tool class pointed at it.</summary>
public sealed class TestRepo : IDisposable
{
    public string Root { get; }
    public string Path { get; }
    public GitService Git { get; }
    public RepositoryTools Repo { get; }
    public WorkTreeTools Work { get; }
    public BranchTools Branch { get; }
    public StashTools Stash { get; }
    public TagTools Tag { get; }
    public RemoteTools Remote { get; }
    public ConfigTools Config { get; }

    public TestRepo(string[]? allowedRoots = null)
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitMcpTests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);
        Path = System.IO.Path.Combine(Root, "repo");
        Git = new GitService(Options.Create(new GitMcpOptions
        {
            LockDirectory = System.IO.Path.Combine(Root, "locks"),
            AllowedRoots = allowedRoots ?? [],
            CommandTimeoutSeconds = 60,
        }));
        Repo = new RepositoryTools(Git);
        Work = new WorkTreeTools(Git);
        Branch = new BranchTools(Git);
        Stash = new StashTools(Git);
        Tag = new TagTools(Git);
        Remote = new RemoteTools(Git);
        Config = new ConfigTools(Git);
    }

    /// <summary>git init on main with a local identity so commits work on any machine.</summary>
    public static async Task<TestRepo> CreateAsync()
    {
        var t = new TestRepo();
        await t.InitAsync(t.Path);
        return t;
    }

    public async Task InitAsync(string path, bool bare = false)
    {
        await Repo.Init(path, bare: bare, initialBranch: "main");
        await Config.Set("user.name", "GitMcp Test", path);
        await Config.Set("user.email", "test@gitmcp.local", path);
        await Config.Set("commit.gpgsign", "false", path);
        await Config.Set("core.autocrlf", "false", path);
    }

    public void Write(string relative, string content, string? repo = null)
    {
        var full = System.IO.Path.Combine(repo ?? Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public string Read(string relative, string? repo = null) => File.ReadAllText(System.IO.Path.Combine(repo ?? Path, relative));

    public async Task CommitFileAsync(string relative, string content, string message, string? repo = null)
    {
        Write(relative, content, repo);
        await Work.Add(repo ?? Path, [relative]);
        await Work.Commit(repo ?? Path, message);
    }

    public async Task<string> HeadAsync(string? repo = null) =>
        (await Config.Run(repo ?? Path, ["rev-parse", "HEAD"], readOnly: true)).Trim();

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal); // git object files are read-only
            }
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
