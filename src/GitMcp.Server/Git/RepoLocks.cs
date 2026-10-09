using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GitMcp.Server.Git;

/// <summary>
/// One reader/writer lock per repository. Reads share it; writes are exclusive within this process and,
/// through a lock file, across every GitMcp process on the machine (each Claude Desktop window starts its own
/// stdio server, so two of them can target the same repository).
/// </summary>
public sealed class RepoLocks(string lockDirectory, TimeSpan writeTimeout)
{
    private static readonly StringComparer KeyComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _locks = new(KeyComparer);

    public AsyncReaderWriterLock For(string repoKey) => _locks.GetOrAdd(repoKey, _ => new AsyncReaderWriterLock());

    public Task<IDisposable> ReadAsync(string repoKey, CancellationToken ct) => For(repoKey).ReadAsync(ct);

    public async Task<IDisposable> WriteAsync(string repoKey, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(writeTimeout);
        IDisposable local;
        try
        {
            local = await For(repoKey).WriteAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out after {writeTimeout.TotalSeconds:0}s waiting for another write on {repoKey}.");
        }

        try
        {
            var file = await AcquireFileLockAsync(repoKey, timeout.Token, ct);
            return new Combined(file, local);
        }
        catch
        {
            local.Dispose();
            throw;
        }
    }

    private async Task<FileStream> AcquireFileLockAsync(string repoKey, CancellationToken timeout, CancellationToken ct)
    {
        Directory.CreateDirectory(lockDirectory);
        var normalized = OperatingSystem.IsWindows() ? repoKey.ToUpperInvariant() : repoKey;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        var path = Path.Combine(lockDirectory, hash + ".lock");
        while (true)
        {
            try
            {
                var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                fs.SetLength(0);
                fs.Write(Encoding.UTF8.GetBytes($"{Environment.ProcessId} {repoKey}"));
                fs.Flush();
                return fs;
            }
            catch (IOException)
            {
                // Held by another GitMcp process; poll until it lets go.
            }
            try
            {
                await Task.Delay(50, timeout);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for another GitMcp process writing to {repoKey}.");
            }
        }
    }

    private sealed class Combined(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
