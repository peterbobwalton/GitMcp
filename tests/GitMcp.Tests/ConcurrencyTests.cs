using GitMcp.Server.Git;

namespace GitMcp.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Readers_Share_The_Lock()
    {
        var rw = new AsyncReaderWriterLock();
        using var r1 = await rw.ReadAsync();
        using var r2 = await rw.ReadAsync();
        Assert.Equal(2, rw.ActiveReaders);
    }

    [Fact]
    public async Task Writer_Waits_For_Readers_And_Blocks_New_Readers()
    {
        var rw = new AsyncReaderWriterLock();
        var reader = await rw.ReadAsync();

        var writer = rw.WriteAsync();
        Assert.False(writer.IsCompleted);

        // Writer preference: a reader arriving after a waiting writer queues behind it.
        var lateReader = rw.ReadAsync();
        Assert.False(lateReader.IsCompleted);

        reader.Dispose();
        var w = await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(rw.WriterActive);
        Assert.False(lateReader.IsCompleted);

        w.Dispose();
        (await lateReader.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Cancelled_Waiting_Writer_Releases_Queued_Readers()
    {
        var rw = new AsyncReaderWriterLock();
        var reader = await rw.ReadAsync();
        using var cts = new CancellationTokenSource();
        var writer = rw.WriteAsync(cts.Token);
        var lateReader = rw.ReadAsync();

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        (await lateReader.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        reader.Dispose();
        (await rw.WriteAsync().WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Writes_Are_Exclusive()
    {
        var rw = new AsyncReaderWriterLock();
        var inside = 0;
        var maxInside = 0;
        await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            using (await rw.WriteAsync())
            {
                var now = Interlocked.Increment(ref inside);
                maxInside = Math.Max(maxInside, now);
                await Task.Delay(1);
                Interlocked.Decrement(ref inside);
            }
        }));
        Assert.Equal(1, maxInside);
    }

    [Fact]
    public async Task Lock_File_Serializes_Writers_Across_Processes()
    {
        // Two RepoLocks sharing a lock folder behave like two GitMcp processes.
        var dir = Path.Combine(Path.GetTempPath(), "GitMcpTests", Guid.NewGuid().ToString("N"));
        try
        {
            var a = new RepoLocks(dir, TimeSpan.FromSeconds(30));
            var b = new RepoLocks(dir, TimeSpan.FromSeconds(30));
            var held = await a.WriteAsync(@"C:\Repo", default);

            var waiting = b.WriteAsync(@"c:\repo", default); // same repo, different case
            await Task.Delay(300);
            Assert.False(waiting.IsCompleted);

            // A different repository is not blocked.
            (await b.WriteAsync(@"C:\Other", default).WaitAsync(TimeSpan.FromSeconds(5))).Dispose();

            held.Dispose();
            (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Write_Lock_Times_Out()
    {
        var dir = Path.Combine(Path.GetTempPath(), "GitMcpTests", Guid.NewGuid().ToString("N"));
        try
        {
            var locks = new RepoLocks(dir, TimeSpan.FromMilliseconds(200));
            using var held = await locks.WriteAsync(@"C:\Repo", default);
            await Assert.ThrowsAsync<TimeoutException>(() => locks.WriteAsync(@"C:\Repo", default));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Parallel_Commits_To_One_Repository_All_Succeed()
    {
        // Without the per-repo lock, concurrent git add/commit fail on .git/index.lock.
        using var t = await TestRepo.CreateAsync();
        await t.CommitFileAsync("base.txt", "base", "base");

        const int n = 16;
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => Task.Run(async () =>
        {
            t.Write($"f{i}.txt", i.ToString());
            await t.Work.Commit(t.Path, $"commit {i}", allowEmpty: true);
            await t.Work.Add(t.Path, [$"f{i}.txt"]);
            await t.Repo.Status(t.Path); // reads interleave with the writes
        })));
        await t.Work.Commit(t.Path, "rest");

        var count = (await t.Config.Run(t.Path, ["rev-list", "--count", "HEAD"], readOnly: true)).Trim();
        Assert.Equal((n + 2).ToString(), count);
        Assert.Contains("nothing to commit", await t.Repo.Status(t.Path));
    }

    [Fact]
    public async Task Different_Repositories_Do_Not_Block_Each_Other()
    {
        using var t = await TestRepo.CreateAsync();
        var other = Path.Combine(t.Root, "other");
        await t.InitAsync(other);

        using var held = await t.Git.Locks.WriteAsync(GitService.RepoKey(t.Path), default);
        // A write on another repository and a read... the read of the locked repo waits.
        await t.CommitFileAsync("x.txt", "x", "other repo", other).WaitAsync(TimeSpan.FromSeconds(30));
        var blockedRead = t.Repo.Status(t.Path);
        await Task.Delay(300);
        Assert.False(blockedRead.IsCompleted);
        held.Dispose();
        await blockedRead.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
