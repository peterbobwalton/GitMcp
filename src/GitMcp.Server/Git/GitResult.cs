namespace GitMcp.Server.Git;

/// <param name="OutputDropped">True when git wrote more than the runner's capture limit and the rest was discarded.</param>
public sealed record GitResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, bool OutputDropped = false)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}
