namespace GitMcp.Server;

/// <summary>Settings bound from the "GitMcp" section of appsettings.json, environment (GitMcp__*) or command line.</summary>
public sealed class GitMcpOptions
{
    public const string Section = "GitMcp";

    /// <summary>git executable. Default: "git" from PATH.</summary>
    public string GitPath { get; set; } = "git";

    /// <summary>Timeout for local commands (status, commit, merge...).</summary>
    public int CommandTimeoutSeconds { get; set; } = 300;

    /// <summary>Timeout for commands that talk to a remote (clone, fetch, pull, push).</summary>
    public int NetworkTimeoutSeconds { get; set; } = 1800;

    /// <summary>How long a write waits for another write on the same repository before giving up.</summary>
    public int LockTimeoutSeconds { get; set; } = 600;

    /// <summary>Output longer than this is truncated (head kept) so huge diffs don't flood the client.</summary>
    public int MaxOutputChars { get; set; } = 200_000;

    /// <summary>If non-empty, repository paths must be inside one of these folders.</summary>
    public string[] AllowedRoots { get; set; } = [];

    /// <summary>Folder for the cross-process lock files. Default: %LOCALAPPDATA%\GitMcp\locks.</summary>
    public string? LockDirectory { get; set; }
}
