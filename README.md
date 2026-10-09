# GitMcp

An MCP server that wraps the git CLI, built on the official
[ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (.NET 10).

- **Every tool takes the repository path**, so any number of sessions can work on different repositories at once.
- **Per-repository locking:** reads (status, log, diff, ...) run in parallel; writes (add, commit, merge, ...) to the
  same repository are serialized. Writes are also serialized *across* GitMcp processes through a lock file in
  `%LOCALAPPDATA%\GitMcp\locks`, because each Claude Desktop window starts its own stdio server.
- **Two transports:** streamable HTTP (default, many clients share one server) or stdio (`--stdio`).
- Non-interactive by design: git never prompts (`GIT_TERMINAL_PROMPT=0`), editors are suppressed, output is
  uncoloured and unpaged, and long output is truncated (`MaxOutputChars`).

## Install (Windows)

Run `installer\Output\GitMcpSetup-<version>.exe` (build it with `.\Build-Installer.ps1`). It installs per user, no
admin needed:

| Path | What |
|------|------|
| `%APPDATA%\GitMcp\server\GitMcp.Server.exe` | self-contained server (no .NET needed) |
| `%APPDATA%\GitMcp\server\appsettings.json` | settings, kept across upgrades |

With the default task ticked it adds this entry to `%APPDATA%\Claude\claude_desktop_config.json` (and the Microsoft
Store build's copy, if present), keeping every other entry and writing a `.bak-<timestamp>` backup first.
Uninstalling removes only that entry. Restart Claude Desktop afterwards.

```json
"mcpServers": {
  "git": {
    "command": "C:\\Users\\<you>\\AppData\\Roaming\\GitMcp\\server\\GitMcp.Server.exe",
    "args": ["--stdio"]
  }
}
```

The same can be done by hand: `GitMcp.Server.exe --register [--name git]` / `--unregister [--name git]`.

## Run

```powershell
GitMcp.Server.exe                                  # HTTP: http://127.0.0.1:5180/mcp
GitMcp.Server.exe --urls http://127.0.0.1:6000     # HTTP on another port
GitMcp.Server.exe --stdio                          # stdio, for clients that launch the server themselves
GitMcp.Server.exe --version

# from source
dotnet run --project src\GitMcp.Server -- --stdio
```

The HTTP server listens on localhost only by default. It uses the SDK's stateless streamable HTTP mode, so clients
need no session affinity and survive a server restart. It refuses browser requests from other origins and requests
addressed to other host names (DNS rebinding); if you serve it on the LAN with `--urls http://0.0.0.0:5180`, also set
`--AllowedHosts=<machine name>`.

## Configure a client

**Claude Desktop** - use the installer (stdio entry above). To share one HTTP server instead, start
`GitMcp.Server.exe` and point Claude Desktop at it through `mcp-remote`:

```json
"git": { "command": "cmd", "args": ["/c", "npx", "-y", "mcp-remote", "http://127.0.0.1:5180/mcp"] }
```

**Claude Code**

```powershell
claude mcp add --transport http git http://127.0.0.1:5180/mcp                               # shared HTTP server
claude mcp add git -- "$env:APPDATA\GitMcp\server\GitMcp.Server.exe" --stdio                # or stdio
```

**VS Code / Visual Studio** (`.vscode/mcp.json` or `.mcp.json`):

```json
{ "servers": { "git": { "type": "http", "url": "http://127.0.0.1:5180/mcp" } } }
```

## Tools

All paths are absolute. `repoPath` may be the working tree or any folder inside it.

| Area | Tools |
|------|-------|
| Repository | `git_init`, `git_clone`, `git_status`, `git_log`, `git_show`, `git_diff`, `git_blame` |
| Working tree | `git_add`, `git_restore`, `git_rm`, `git_mv`, `git_commit`, `git_reset`, `git_clean` (dry run unless `dryRun=false`) |
| Branches | `git_branch_list`, `git_branch_create`, `git_branch_delete`, `git_branch_rename`, `git_checkout`, `git_switch` |
| History | `git_merge`, `git_rebase`, `git_cherry_pick`, `git_revert` - each takes `action`: `start` (default), `continue`, `abort`, `skip`, `quit` |
| Stash | `git_stash_push`, `git_stash_pop`, `git_stash_apply`, `git_stash_list`, `git_stash_show`, `git_stash_drop` |
| Tags | `git_tag_list`, `git_tag_create` (annotated when `message` is set), `git_tag_delete` |
| Remotes | `git_fetch`, `git_pull`, `git_push` (incl. `forceWithLease`, `delete`), `git_remote_list`, `git_remote_add`, `git_remote_remove`, `git_remote_set_url` |
| Config | `git_config_get`, `git_config_set`, `git_config_list` (scope `local`, `global`, `system`, `worktree`) |
| Anything else | `git_run` - arguments after `git`, starting with the subcommand (global options like `-C`/`-c` are refused); set `readOnly=true` for read-only commands so they run in parallel |

`git_pull` follows your `pull.rebase` setting unless `rebase` is given, and `git_push` with `setUpstream` and no
refspecs pushes the current branch. Linked worktrees share their main repository's lock.

Values that name refs, remotes or URLs are rejected if they start with `-`, and file paths are always passed after
`--`, so arguments can't be smuggled in as git options. Interactive commands (`rebase -i`, `add -p`) aren't supported.

## Settings

`appsettings.json` next to the exe, `GitMcp__<Key>` environment variables, or `--GitMcp:<Key>=<value>`:

| Key | Default | Meaning |
|-----|---------|---------|
| `GitPath` | `git` | git executable |
| `CommandTimeoutSeconds` | 300 | timeout for local commands |
| `NetworkTimeoutSeconds` | 1800 | timeout for clone / fetch / pull / push / git_run |
| `LockTimeoutSeconds` | 600 | how long a write waits for another write on the same repository |
| `MaxOutputChars` | 200000 | longer output is truncated |
| `AllowedRoots` | `[]` | if set, repositories must be inside one of these folders (clone sources and config values are not restricted) |
| `LockDirectory` | `%LOCALAPPDATA%\GitMcp\locks` | cross-process lock files |

## Build and test

Open `GitMcp.slnx` in Visual Studio 2026, or:

```powershell
dotnet build GitMcp.slnx
dotnet test GitMcp.slnx       # needs git on PATH; creates throwaway repos under %TEMP%\GitMcpTests
.\Build-Installer.ps1         # tests + publish + Inno Setup 6 -> installer\Output\GitMcpSetup-<version>.exe
```

Layout: `src/GitMcp.Server` (server: `Git/` runner, locks and service; `Tools/` MCP tools; `ClaudeConfig.cs`
registration), `tests/GitMcp.Tests` (xUnit: every tool against real repositories, locking and concurrency, end-to-end
over HTTP with the SDK client, Claude config editing), `installer/GitMcp.iss`.
