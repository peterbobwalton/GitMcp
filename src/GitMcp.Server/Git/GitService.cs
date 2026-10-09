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
        // Capture a little more than we show, so huge output (a binary diff, a giant log) can't exhaust memory.
        _runner = new GitRunner(_options.GitPath, _options.MaxOutputChars + 1);
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

    /// <summary>
    /// The lock key for <paramref name="dir"/>: the repository's working tree root, so subfolders share a lock.
    /// Linked worktrees (git worktree add) resolve to their main repository, because they share its refs.
    /// Falls back to dir itself when it is not inside a repository.
    /// </summary>
    public static string RepoKey(string dir)
    {
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var dotGit = Path.Combine(d.FullName, ".git");
            if (Directory.Exists(dotGit))
            {
                return Normalize(d.FullName);
            }
            if (File.Exists(dotGit))
            {
                return MainRepositoryOf(dotGit) ?? Normalize(d.FullName);
            }
            if (File.Exists(Path.Combine(d.FullName, "HEAD")) && Directory.Exists(Path.Combine(d.FullName, "objects")))
            {
                return Normalize(d.FullName); // bare repository
            }
        }
        return Normalize(dir);
    }

    /// <summary>For a ".git" file ("gitdir: .../.git/worktrees/x"), the main repository's key, if it can be found.</summary>
    private static string? MainRepositoryOf(string dotGitFile)
    {
        try
        {
            var line = File.ReadLines(dotGitFile).FirstOrDefault()?.Trim();
            if (line is null || !line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return null;
            }
            var gitDir = Path.GetFullPath(line["gitdir:".Length..].Trim(), Path.GetDirectoryName(dotGitFile)!);
            var commonDirFile = Path.Combine(gitDir, "commondir");
            if (!File.Exists(commonDirFile))
            {
                return null; // a submodule: its own repository
            }
            var common = Path.TrimEndingDirectorySeparator(Path.GetFullPath(File.ReadAllText(commonDirFile).Trim(), gitDir));
            // A non-bare main repository is keyed on its working tree, like every other call on it.
            return Normalize(Path.GetFileName(common) == ".git" ? Path.GetDirectoryName(common)! : common);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
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
            throw new McpException($"{GitRunner.Display(args)} timed out after {timeout.TotalSeconds:0}s and was stopped.\n{Format(result, errorFirst: true)}");
        }
        // An accepted exit code (1 from "config --get" for a missing key) only counts when git said nothing on
        // stderr: "error: invalid key" exits 1 as well.
        var accepted = okExitCodes?.Contains(result.ExitCode) == true && string.IsNullOrWhiteSpace(result.StdErr);
        if (!result.Success && !accepted)
        {
            throw new McpException($"{GitRunner.Display(args)} failed (exit code {result.ExitCode}).\n{Format(result, errorFirst: true)}");
        }
        return Format(result, errorFirst: false);
    }

    private string Format(GitResult r, bool errorFirst)
    {
        // git reports many results ("Switched to branch...") on stderr. On failure stderr holds the reason, so it
        // goes first where truncation can't cut it off.
        var parts = new[] { r.StdOut.TrimEnd(), r.StdErr.TrimEnd() };
        if (errorFirst)
        {
            Array.Reverse(parts);
        }
        var sb = new StringBuilder();
        foreach (var part in parts.Where(p => p.Length > 0))
        {
            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }
            sb.Append(part);
        }
        if (sb.Length == 0)
        {
            sb.Append("(done, no output)");
        }
        if (sb.Length > _options.MaxOutputChars || r.OutputDropped)
        {
            sb.Length = Math.Min(sb.Length, _options.MaxOutputChars);
            sb.Append($"\n\n[output truncated at {_options.MaxOutputChars:N0} characters; narrow the request with paths, maxCount or stat]");
        }
        return sb.ToString();
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsUnder(string path, string root)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // A drive root ("C:\") keeps its trailing separator after normalizing.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, cmp) || path.StartsWith(prefix, cmp);
    }
}
