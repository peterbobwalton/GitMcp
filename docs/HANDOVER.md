# Handover (2026-10-09)

## State

- Version **0.1.1**, two commits on `main`, pushed to github.com/peterbobwalton/GitMcp (private).
- Installed on Pete's PC from `installer\Output\GitMcpSetup-0.1.1.exe`:
  `%APPDATA%\GitMcp\server\GitMcp.Server.exe` + `appsettings.json`.
- `%APPDATA%\Claude\claude_desktop_config.json` has `"git": { command: <exe>, args: ["--stdio"] }`; the other six
  servers were kept. Backup from the first install: `claude_desktop_config.json.bak-20261009-121041`.
- Claude Desktop had **not** been restarted when this was written, so the tools have not yet been used from a chat.
- Tests: 48/48 pass (`dotnet test GitMcp.slnx`), three runs in a row. A live script drove the installed exe over HTTP
  with 8 parallel sessions on one repo + 4 on separate repos: 34/34 checks passed.

## How it fits together

| File | Role |
|------|------|
| `src/GitMcp.Server/Program.cs` | entry: `--stdio`, HTTP (default `127.0.0.1:5180/mcp`), `--register`/`--unregister`, `--version`; Origin check, AllowedHosts default |
| `Git/GitRunner.cs` | starts git non-interactively (no prompts/editor/pager, `LC_ALL=C`), caps captured output |
| `Git/GitService.cs` | path validation, AllowedRoots, repo lock key (`RepoKey`), runs under the lock, formats output/errors |
| `Git/RepoLocks.cs`, `Git/AsyncReaderWriterLock.cs` | per-repo RW lock (writer-preferring) + lock file in `%LOCALAPPDATA%\GitMcp\locks` for cross-process writes |
| `Git/GitArgs.cs` | argument builder; `Ref` rejects leading `-`, `Paths` adds `--` |
| `Tools/*.cs` | 44 MCP tools |
| `ClaudeConfig.cs` | edits claude_desktop_config.json (classic + MS Store paths), copied from PCHardwareMcp |
| `installer/GitMcp.iss` | per-user Inno Setup script |

## Gotchas

- Claude Desktop only supports stdio servers in its config, so each Claude Desktop window runs its own GitMcp
  process. That's why writes also take a lock file; reads are only locked in-process.
- Linked worktrees resolve to the main repo's lock key (they share refs); submodules get their own.
- `git config --get` exits 1 for "not set" and for "invalid key"; only an exit 1 with empty stderr is "not set".
- `git push -u origin` without a refspec fails on a branch with no upstream, so the tool pushes `HEAD`.
- The installer copies `appsettings.json` only if it doesn't exist (user edits survive upgrades), so new defaults
  must live in code, not only in appsettings.json.
- Clones follow the global `core.autocrlf` (true on Pete's PC): tests compare trimmed content.
- Bash heredocs on this PC re-interpret `\n` inside Python edit scripts; use the Edit/Write tools for C# with escapes.

## Open items / ideas

- Restart Claude Desktop and try the tools from a chat (e.g. "show git status for C:\source\Claude\GitMcp").
- Optional: register in Claude Code too (`claude mcp add --transport http git http://127.0.0.1:5180/mcp` with the
  HTTP server running, or the stdio exe).
- Optional: start the HTTP server at logon (shared server for all clients) - not built; would need a hidden launcher.
- Optional: copy installers to a `Z:\Software\Claude\GitMcp` share like the other MCP projects (not asked for yet).
- Interactive git (rebase -i, add -p) is out of scope; credential prompts rely on the git credential manager.
- AllowedRoots restricts repository locations only, not clone sources or config values.
