using GitMcp.Server.Git;
using ModelContextProtocol;

namespace GitMcp.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task Relative_Path_Is_Rejected()
    {
        using var t = new TestRepo();
        var ex = await Assert.ThrowsAsync<McpException>(() => t.Repo.Status("relative\\repo"));
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public async Task Missing_Directory_Is_Reported()
    {
        using var t = new TestRepo();
        var ex = await Assert.ThrowsAsync<McpException>(() => t.Repo.Status(Path.Combine(t.Root, "nope")));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Option_Injection_Through_Refs_Is_Rejected()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a", "c1");
        await Assert.ThrowsAsync<McpException>(() => t.Branch.Create(t.Path, "--upload-pack=evil"));
        await Assert.ThrowsAsync<McpException>(() => t.Repo.Log(t.Path, revision: "--output=x"));
        await Assert.ThrowsAsync<McpException>(() => t.Repo.Clone("--upload-pack=calc", Path.Combine(t.Root, "c")));
    }

    [Fact]
    public async Task Paths_That_Look_Like_Options_Are_Treated_As_Files()
    {
        using var t = await TestRepo.CreateAsync();
        t.Write("-n", "dash file");
        await t.Work.Add(t.Path, ["-n"]);
        Assert.Contains("-n", await t.Repo.Status(t.Path, @short: true));
    }

    [Fact]
    public async Task Allowed_Roots_Are_Enforced()
    {
        using var probe = new TestRepo();
        var allowed = Path.Combine(probe.Root, "allowed");
        using var t = new TestRepo([allowed]);
        await t.Repo.Init(Path.Combine(allowed, "ok"));
        var ex = await Assert.ThrowsAsync<McpException>(() => t.Repo.Init(Path.Combine(probe.Root, "outside")));
        Assert.Contains("outside the allowed roots", ex.Message);
        // A sibling that merely shares the prefix is not inside.
        await Assert.ThrowsAsync<McpException>(() => t.Repo.Init(allowed + "-evil"));
    }

    [Fact]
    public async Task Drive_Root_Works_As_Allowed_Root()
    {
        using var probe = new TestRepo();
        using var t = new TestRepo([Path.GetPathRoot(probe.Root)!]);
        await t.Repo.Init(Path.Combine(probe.Root, "under-drive-root"));
    }

    [Fact]
    public async Task Runner_Stops_Capturing_At_The_Limit()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", string.Join("\n", Enumerable.Range(0, 2000)), "c1");
        var runner = new GitRunner("git", maxCaptureChars: 100);
        var r = await runner.RunAsync(t.Path, ["show", "HEAD"], TimeSpan.FromSeconds(60), default);
        Assert.True(r.Success);
        Assert.True(r.OutputDropped);
        Assert.True(r.StdOut.Length < 1000, $"captured {r.StdOut.Length} chars");
    }

    [Fact]
    public async Task Failing_Command_Puts_Git_Error_First()
    {
        using var t = await TestRepo.CreateAsync();
        var ex = await Assert.ThrowsAsync<McpException>(() => t.Branch.Merge(t.Path, ["no-such-branch"]));
        var secondLine = ex.Message.Split('\n')[1];
        Assert.Contains("no-such-branch", secondLine);
    }

    [Fact]
    public async Task Failing_Command_Reports_Exit_Code_And_Stderr()
    {
        using var t = await TestRepo.CreateAsync();
        var ex = await Assert.ThrowsAsync<McpException>(() => t.Branch.Switch(t.Path, "does-not-exist"));
        Assert.Contains("exit code", ex.Message);
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public async Task Long_Output_Is_Truncated()
    {
        using var t = new TestRepo();
        var git = new GitService(Microsoft.Extensions.Options.Options.Create(new GitMcp.Server.GitMcpOptions
        {
            MaxOutputChars = 50,
            LockDirectory = Path.Combine(t.Root, "locks"),
        }));
        await t.InitAsync(t.Path);
        await t.CommitFileAsync("a.txt", new string('x', 500), "c1");
        var shown = await git.ReadAsync(t.Path, ["show", "HEAD"], default);
        Assert.Contains("[output truncated", shown);
    }
}
