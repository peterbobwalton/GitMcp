using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace GitMcp.Server.Git;

/// <summary>
/// Validates repository paths, takes the per-repository lock (shared for reads, exclusive for writes) and
/// runs git. Every tool goes through here, so sessions only ever meet on the lock of the repository they share.
/// </summary>
public sealed class GitService
{
    private readonly GitMcpOptions _options;
    private readonly GitRunner _runner;
    private readonly RepoLocks _locks;
    private readonly string[] _allowedRoots;

    public GitService(IOptions<GitMcpOptions> options)
    {
        _options = options.Value;
        _runner = new GitRunner(_options.GitPath);
        var lockDir = _options.LockDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitMcp", "locks");
        _locks = new RepoLocks(lockDir, TimeSpan.FromSeconds(_options.LockTimeoutSeconds));
        _allowedRoots = _options.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Normalize).ToArray();
    }

    public RepoLocks Locks => _locks;

    /// <summary>Read-only command: runs alongside other reads of the same repository.</summary>
    public Task<string> ReadAsync(string repo, IEnumerable<string> args, CancellationToken ct, int[]? okExitCodes = null)
    {
        var dir = ExistingDirectory(repo);
        // --no-optional-locks: status & co. must not take index.lock, or parallel reads would collide.
        return RunLockedAsync(dir, RepoKey(dir), write: false, ["--no-optional-locks", .. args], network: false, ct, okExitCodes);
    }

    /// <summary>Modifying command: exclusive on this repository (in this process and across GitMcp processes).</summary>
    public Task<string> WriteAsync(string repo, IEnumerable<string> args, CancellationToken ct, bool network = false)
    {
        var dir = ExistingDirectory(repo);
        return RunLockedAsync(dir, RepoKey(dir), write: true, [.. args], network, ct);
    }

    /// <summary>Like <see cref="WriteAsync"/> for commands that create the repository (init, clone):
    /// the lock is keyed on <paramref name="target"/>, git runs in <paramref name="workingDirectory"/>.</summary>
    public Task<string> CreateAsync(string workingDirectory, string target, IEnumerable<string> args, CancellationToken ct, bool network = false)
    {
        Directory.CreateDirectory(workingDirectory);
        return RunLockedAsync(workingDirectory, target, write: true, [.. args], network, ct);
    }

    /// <summary>Runs in an arbitrary folder under an explicit lock key (used for --global/--system config).</summary>
    public Task<string> RunInAsync(string workingDirectory, string lockKey, bool write, IEnumerable<string> args, CancellationToken ct, int[]? okExitCodes = null) =>
        RunLockedAsync(workingDirectory, lockKey, write, [.. args], network: false, ct, okExitCodes);

    /// <summary>Absolute, normalized path inside the allowed roots. Throws for anything else.</summary>
    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("A repository path is required.");
        }
        path = path.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path))
        {
            throw new McpException($"'{path}' is not an absolute path. Pass the full path of the repository.");
        }
        var full = Normalize(path);
        if (_allowedRoots.Length > 0 && !_allowedRoots.Any(root => IsUnder(full, root)))
        {
            throw new McpException($"'{full}' is outside the allowed roots ({string.Join(", ", _allowedRoots)}).");
        }
        return full;
    }

    /// <summary>The working tree root that contains <paramref name="dir"/> (so subfolders share a lock), else dir itself.</summary>
    public static string RepoKey(string dir)
    {
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var dotGit = Path.Combine(d.FullName, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
            {
                return Normalize(d.FullName);
            }
            if (File.Exists(Path.Combine(d.FullName, "HEAD")) && Directory.Exists(Path.Combine(d.FullName, "objects")))
            {
                return Normalize(d.FullName); // bare repository
            }
        }
        return Normalize(dir);
    }

    private string ExistingDirectory(string repo)
    {
        var dir = ResolvePath(repo);
        if (!Directory.Exists(dir))
        {
            throw new McpException($"Directory not found: {dir}");
        }
        return dir;
    }

    private async Task<string> RunLockedAsync(string workingDirectory, string lockKey, bool write, string[] args, bool network, CancellationToken ct, int[]? okExitCodes = null)
    {
        var timeout = TimeSpan.FromSeconds(network ? _options.NetworkTimeoutSeconds : _options.CommandTimeoutSeconds);
        IDisposable held;
        try
        {
            held = write ? await _locks.WriteAsync(lockKey, ct) : await _locks.ReadAsync(lockKey, ct);
        }
        catch (TimeoutException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        GitResult result;
        using (held)
        {
            try
            {
                result = await _runner.RunAsync(workingDirectory, args, timeout, ct);
            }
            catch (InvalidOperationException ex)
            {
                throw new McpException(ex.Message, ex);
            }
        }

        if (result.TimedOut)
        {
            throw new McpException($"{GitRunner.Display(args)} timed out after {timeout.TotalSeconds:0}s and was stopped.\n{Format(result)}");
        }
        if (!result.Success && okExitCodes?.Contains(result.ExitCode) != true)
        {
            throw new McpException($"{GitRunner.Display(args)} failed (exit code {result.ExitCode}).\n{Format(result)}");
        }
        return Format(result);
    }

    private string Format(GitResult r)
    {
        var sb = new StringBuilder();
        var stdout = r.StdOut.TrimEnd();
        var stderr = r.StdErr.TrimEnd();
        if (stdout.Length > 0)
        {
            sb.Append(stdout);
        }
        if (stderr.Length > 0)
        {
            // git reports progress and many results ("Switched to branch...") on stderr.
            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }
            sb.Append(stderr);
        }
        if (sb.Length == 0)
        {
            sb.Append("(done, no output)");
        }
        if (sb.Length > _options.MaxOutputChars)
        {
            var total = sb.Length;
            sb.Length = _options.MaxOutputChars;
            sb.Append($"\n\n[output truncated: showing {_options.MaxOutputChars:N0} of {total:N0} characters; narrow the request with paths, maxCount or stat]");
        }
        return sb.ToString();
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsUnder(string path, string root)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, cmp) || path.StartsWith(root + Path.DirectorySeparatorChar, cmp);
    }
}
