using ModelContextProtocol;

namespace GitMcp.Tests;

public sealed class GitToolsTests
{
    [Fact]
    public async Task Init_Add_Commit_Status_Log_Show()
    {
        using var t = await TestRepo.CreateAsync();
        Assert.True(Directory.Exists(Path.Combine(t.Path, ".git")));

        t.Write("readme.md", "hello\n");
        Assert.Contains("readme.md", await t.Repo.Status(t.Path, @short: true));

        await t.Work.Add(t.Path, ["readme.md"]);
        var commit = await t.Work.Commit(t.Path, "first commit");
        Assert.Contains("first commit", commit);

        var status = await t.Repo.Status(t.Path);
        Assert.Contains("nothing to commit", status);
        Assert.Contains("On branch main", status);

        var log = await t.Repo.Log(t.Path, oneline: true);
        Assert.Contains("first commit", log);

        Assert.Equal("hello", (await t.Repo.Show(t.Path, "HEAD", path: "readme.md")).Trim());
        Assert.Contains("readme.md", await t.Repo.Show(t.Path, "HEAD", nameOnly: true));
    }

    [Fact]
    public async Task Diff_Unstaged_Staged_And_Between_Revisions()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "one\n", "c1");
        t.Write("a.txt", "one\ntwo\n");

        Assert.Contains("+two", await t.Repo.Diff(t.Path));
        Assert.Equal("(done, no output)", await t.Repo.Diff(t.Path, staged: true));

        await t.Work.Add(t.Path, ["a.txt"]);
        Assert.Contains("+two", await t.Repo.Diff(t.Path, staged: true));

        await t.Work.Commit(t.Path, "c2");
        Assert.Contains("a.txt", await t.Repo.Diff(t.Path, @base: "HEAD~1", target: "HEAD", stat: true));
    }

    [Fact]
    public async Task Branch_Create_List_Rename_Switch_Checkout_Delete()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a", "c1");

        await t.Branch.Create(t.Path, "feature");
        Assert.Contains("feature", await t.Branch.List(t.Path));

        await t.Branch.Rename(t.Path, "feature2", oldName: "feature");
        var list = await t.Branch.List(t.Path);
        Assert.Contains("feature2", list);
        Assert.DoesNotContain(" feature\n", list);

        await t.Branch.Switch(t.Path, "feature2");
        Assert.Contains("On branch feature2", await t.Repo.Status(t.Path));

        await t.Branch.Checkout(t.Path, "main");
        await t.Branch.Checkout(t.Path, "main", newBranch: "topic");
        Assert.Contains("On branch topic", await t.Repo.Status(t.Path));

        await t.Branch.Switch(t.Path, "made-by-switch", create: true);
        Assert.Contains("On branch made-by-switch", await t.Repo.Status(t.Path));

        await t.Branch.Switch(t.Path, "main");
        await t.Branch.Delete(t.Path, "feature2");
        Assert.DoesNotContain("feature2", await t.Branch.List(t.Path));
    }

    [Fact]
    public async Task Checkout_Paths_Restores_File()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "original", "c1");
        t.Write("a.txt", "changed");
        await t.Branch.Checkout(t.Path, paths: ["a.txt"]);
        Assert.Equal("original", t.Read("a.txt"));
    }

    [Fact]
    public async Task Merge_FastForward_And_Conflict_Abort()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "base\n", "base");

        await t.Branch.Switch(t.Path, "ff", create: true);
        await t.CommitFileAsync("b.txt", "b\n", "on ff");
        await t.Branch.Switch(t.Path, "main");
        Assert.Contains("Fast-forward", await t.Branch.Merge(t.Path, ["ff"]));
        Assert.True(File.Exists(Path.Combine(t.Path, "b.txt")));

        await t.Branch.Switch(t.Path, "other", create: true);
        await t.CommitFileAsync("a.txt", "other\n", "other change");
        await t.Branch.Switch(t.Path, "main");
        await t.CommitFileAsync("a.txt", "main\n", "main change");

        var ex = await Assert.ThrowsAsync<McpException>(() => t.Branch.Merge(t.Path, ["other"]));
        Assert.Contains("CONFLICT", ex.Message);
        Assert.Contains("both modified", await t.Repo.Status(t.Path));

        await t.Branch.Merge(t.Path, action: "abort");
        Assert.Equal("main\n", t.Read("a.txt"));
        Assert.Contains("nothing to commit", await t.Repo.Status(t.Path));
    }

    [Fact]
    public async Task Merge_Conflict_Resolved_Then_Continue()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "base\n", "base");
        await t.Branch.Switch(t.Path, "other", create: true);
        await t.CommitFileAsync("a.txt", "other\n", "other");
        await t.Branch.Switch(t.Path, "main");
        await t.CommitFileAsync("a.txt", "main\n", "main");

        await Assert.ThrowsAsync<McpException>(() => t.Branch.Merge(t.Path, ["other"]));
        t.Write("a.txt", "resolved\n");
        await t.Work.Add(t.Path, ["a.txt"]);
        await t.Branch.Merge(t.Path, action: "continue");

        Assert.Contains("Merge branch 'other'", await t.Repo.Log(t.Path, maxCount: 1));
    }

    [Fact]
    public async Task Rebase_Onto_Main_And_Abort_On_Conflict()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "base\n", "base");
        await t.Branch.Switch(t.Path, "feature", create: true);
        await t.CommitFileAsync("f.txt", "f\n", "feature work");
        await t.Branch.Switch(t.Path, "main");
        await t.CommitFileAsync("m.txt", "m\n", "main work");
        await t.Branch.Switch(t.Path, "feature");

        await t.Branch.Rebase(t.Path, "main");
        var log = await t.Repo.Log(t.Path, oneline: true);
        Assert.True(log.IndexOf("feature work") < log.IndexOf("main work"), log);

        // Conflict, then abort.
        await t.CommitFileAsync("a.txt", "feature\n", "feature edits a");
        await t.Branch.Switch(t.Path, "main");
        await t.CommitFileAsync("a.txt", "main\n", "main edits a");
        await t.Branch.Switch(t.Path, "feature");
        var before = await t.HeadAsync();
        await Assert.ThrowsAsync<McpException>(() => t.Branch.Rebase(t.Path, "main"));
        await t.Branch.Rebase(t.Path, action: "abort");
        Assert.Equal(before, await t.HeadAsync());
    }

    [Fact]
    public async Task CherryPick_And_Revert()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a\n", "base");
        await t.Branch.Switch(t.Path, "side", create: true);
        await t.CommitFileAsync("pick.txt", "pick\n", "to pick");
        var pick = await t.HeadAsync();
        await t.Branch.Switch(t.Path, "main");

        await t.Branch.CherryPick(t.Path, [pick]);
        Assert.Equal("pick\n", t.Read("pick.txt"));

        await t.Branch.Revert(t.Path, ["HEAD"]);
        Assert.False(File.Exists(Path.Combine(t.Path, "pick.txt")));
        Assert.Contains("Revert", await t.Repo.Log(t.Path, maxCount: 1, oneline: true));
    }

    [Fact]
    public async Task Reset_Restore_Rm_Mv()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "1", "c1");
        var first = await t.HeadAsync();
        await t.CommitFileAsync("a.txt", "2", "c2");

        await t.Work.Reset(t.Path, "soft", "HEAD~1");
        Assert.Equal(first, await t.HeadAsync());
        Assert.Contains("a.txt", await t.Repo.Diff(t.Path, staged: true, nameOnly: true));

        await t.Work.Restore(t.Path, ["a.txt"], staged: true);
        Assert.Equal("(done, no output)", await t.Repo.Diff(t.Path, staged: true));
        await t.Work.Restore(t.Path, ["a.txt"]);
        Assert.Equal("1", t.Read("a.txt"));

        await t.Work.Move(t.Path, "a.txt", "b.txt");
        Assert.True(File.Exists(Path.Combine(t.Path, "b.txt")));
        await t.Work.Remove(t.Path, ["b.txt"], force: true);
        Assert.False(File.Exists(Path.Combine(t.Path, "b.txt")));

        await t.Work.Reset(t.Path, "hard");
        Assert.Equal("1", t.Read("a.txt"));
        await Assert.ThrowsAsync<McpException>(() => t.Work.Reset(t.Path, "sideways"));
    }

    [Fact]
    public async Task Stash_Push_List_Show_Apply_Pop_Drop()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "base", "c1");

        t.Write("a.txt", "wip1");
        await t.Stash.Push(t.Path, message: "first");
        Assert.Equal("base", t.Read("a.txt"));
        t.Write("a.txt", "wip2");
        await t.Stash.Push(t.Path, message: "second");

        var list = await t.Stash.List(t.Path);
        Assert.Contains("first", list);
        Assert.Contains("second", list);
        Assert.Contains("a.txt", await t.Stash.Show(t.Path, "0"));

        await t.Stash.Apply(t.Path, "1");
        Assert.Equal("wip1", t.Read("a.txt"));
        await t.Work.Reset(t.Path, "hard");

        await t.Stash.Pop(t.Path);
        Assert.Equal("wip2", t.Read("a.txt"));
        await t.Work.Reset(t.Path, "hard");

        await t.Stash.Drop(t.Path);
        Assert.Equal("(done, no output)", await t.Stash.List(t.Path));
    }

    [Fact]
    public async Task Stash_Push_Untracked()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "base", "c1");
        t.Write("new.txt", "untracked");
        await t.Stash.Push(t.Path, includeUntracked: true);
        Assert.False(File.Exists(Path.Combine(t.Path, "new.txt")));
        await t.Stash.Pop(t.Path);
        Assert.True(File.Exists(Path.Combine(t.Path, "new.txt")));
    }

    [Fact]
    public async Task Tags_Create_List_Delete()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a", "c1");
        await t.Tag.Create(t.Path, "v1.0.0");
        await t.Tag.Create(t.Path, "v1.1.0", message: "release 1.1");

        var list = await t.Tag.List(t.Path, withMessages: true);
        Assert.Contains("v1.0.0", list);
        Assert.Contains("release 1.1", list);
        Assert.Contains("v1.1.0", await t.Tag.List(t.Path, pattern: "v1.1*"));

        await t.Tag.Delete(t.Path, "v1.0.0");
        Assert.DoesNotContain("v1.0.0", await t.Tag.List(t.Path));
    }

    [Fact]
    public async Task Clone_Push_Fetch_Pull_Through_Bare_Remote()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a\n", "c1");

        var bare = Path.Combine(t.Root, "remote.git");
        await t.Repo.Init(bare, bare: true, initialBranch: "main");
        await t.Remote.Add(t.Path, "origin", bare);
        Assert.Contains("origin", await t.Remote.List(t.Path));
        await t.Remote.Push(t.Path, refspecs: ["main"], setUpstream: true);

        var clone = Path.Combine(t.Root, "clone");
        await t.Repo.Clone(bare, clone);
        Assert.Equal("a", t.Read("a.txt", clone).Trim()); // the clone follows the global core.autocrlf
        await t.Config.Set("user.name", "Clone", clone);
        await t.Config.Set("user.email", "clone@gitmcp.local", clone);

        await t.CommitFileAsync("b.txt", "b\n", "from clone", clone);
        await t.Remote.Push(clone);

        await t.Remote.Fetch(t.Path, "origin", prune: true);
        Assert.Contains("behind 1", await t.Branch.List(t.Path, verbose: true));
        await t.Remote.Pull(t.Path, fastForwardOnly: true);
        Assert.Equal("b", t.Read("b.txt").Trim());

        // Delete a remote branch, then the remote itself.
        await t.Remote.Push(t.Path, "origin", ["main:gone"]);
        await t.Remote.Push(t.Path, "origin", ["gone"], delete: true);
        await t.Remote.SetUrl(t.Path, "origin", bare);
        await t.Remote.Remove(t.Path, "origin");
        Assert.Equal("(done, no output)", await t.Remote.List(t.Path));
    }

    [Fact]
    public async Task Blame_Shows_Author()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "l1\nl2\nl3\n", "c1");
        var blame = await t.Repo.Blame(t.Path, "a.txt", startLine: 2, endLine: 3);
        Assert.Contains("GitMcp Test", blame);
        Assert.Contains("l2", blame);
        Assert.DoesNotContain("l1", blame);
    }

    [Fact]
    public async Task Config_Get_Set_Unset_List()
    {
        using var t = await TestRepo.CreateAsync();
        await t.Config.Set("gitmcp.test", "value 1", t.Path);
        Assert.Equal("value 1", (await t.Config.Get("gitmcp.test", t.Path)).Trim());
        Assert.Contains("gitmcp.test=value 1", await t.Config.List(t.Path, "local"));
        await t.Config.Set("gitmcp.test", repoPath: t.Path, unset: true);
        Assert.Equal("gitmcp.test is not set.", await t.Config.Get("gitmcp.test", t.Path));
        await Assert.ThrowsAsync<McpException>(() => t.Config.Get("user.name"));
    }

    [Fact]
    public async Task Clean_DryRun_Then_Force()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a", "c1");
        t.Write("junk.tmp", "x");
        t.Write("junkdir/inner.tmp", "x");

        var dry = await t.Work.Clean(t.Path, directories: true);
        Assert.Contains("Would remove junk.tmp", dry);
        Assert.True(File.Exists(Path.Combine(t.Path, "junk.tmp")));

        await t.Work.Clean(t.Path, dryRun: false, directories: true);
        Assert.False(File.Exists(Path.Combine(t.Path, "junk.tmp")));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "junkdir")));
    }

    [Fact]
    public async Task Run_Escape_Hatch_And_Commit_Amend()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("a.txt", "a", "orig message");
        await t.Work.Commit(t.Path, "new message", amend: true);
        Assert.Contains("new message", await t.Config.Run(t.Path, ["log", "-1", "--format=%s"], readOnly: true));
        Assert.Equal("main", (await t.Config.Run(t.Path, ["rev-parse", "--abbrev-ref", "HEAD"], readOnly: true)).Trim());
        await Assert.ThrowsAsync<McpException>(() => t.Config.Run(t.Path, ["-C", "C:\\", "status"]));
    }

    [Fact]
    public async Task Works_From_A_Subfolder()
    {
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("sub/a.txt", "a", "c1");
        var sub = Path.Combine(t.Path, "sub");
        Assert.Contains("nothing to commit", await t.Repo.Status(sub));
        Assert.Equal(t.Path, GitMcp.Server.Git.GitService.RepoKey(sub), ignoreCase: OperatingSystem.IsWindows());
    }
}
