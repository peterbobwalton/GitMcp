using System.Diagnostics;
using System.Text;

namespace GitMcp.Server.Git;

/// <summary>Runs the git CLI non-interactively and captures its output.</summary>
/// <param name="maxCaptureChars">Per stream; anything beyond is read and discarded (git must not block on a full pipe).</param>
public sealed class GitRunner(string gitPath = "git", int maxCaptureChars = int.MaxValue)
{
    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        // Stable, parseable, never-paging output.
        foreach (var setting in new[] { "core.quotepath=false", "color.ui=false", "core.pager=cat" })
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(setting);
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never wait for a username/password on stdin
        psi.Environment["GIT_EDITOR"] = "true";         // merge/rebase/revert --continue keep the prepared message
        psi.Environment["GIT_SEQUENCE_EDITOR"] = "true";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["LC_ALL"] = "C";                 // English messages so clients can read them

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var dropped = false;
        void Append(StringBuilder sb, string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (sb)
            {
                if (sb.Length < maxCaptureChars)
                {
                    sb.AppendLine(line);
                }
                else
                {
                    dropped = true;
                }
            }
        }
        process.OutputDataReceived += (_, e) => Append(stdout, e.Data);
        process.ErrorDataReceived += (_, e) => Append(stderr, e.Data);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start '{gitPath}'. Is git installed and on PATH? ({ex.Message})", ex);
        }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            ct.ThrowIfCancellationRequested();
            timedOut = true;
        }
        // Drain the async readers.
        process.WaitForExit();

        string o, e;
        lock (stdout) { o = stdout.ToString(); }
        lock (stderr) { e = stderr.ToString(); }
        return new GitResult(timedOut ? -1 : process.ExitCode, o, e, timedOut, dropped);
    }

    public static string Display(IEnumerable<string> args) =>
        "git " + string.Join(' ', args.Select(a => a.Length == 0 || a.Any(char.IsWhiteSpace) ? $"\"{a}\"" : a));
}
