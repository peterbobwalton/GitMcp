# GitMcp

Repo: https://github.com/peterbobwalton/GitMcp (public)

C# (.NET 10) MCP server wrapping the git CLI, on ModelContextProtocol(.AspNetCore) 2.2.0.
See README.md for tools, transports, client setup and settings; docs/HANDOVER.md for current state and open items;
docs/SUMMARY.md for what was built and why.

## Build / verify

- Build: `dotnet build GitMcp.slnx` (or Visual Studio 2026). Tests: `dotnet test GitMcp.slnx` (needs git on PATH,
  makes throwaway repos under `%TEMP%\GitMcpTests`).
- Installer: `.\Build-Installer.ps1` (tests, self-contained publish, Inno Setup 6) ->
  `installer\Output\GitMcpSetup-<version>.exe`. Version comes from `<Version>` in `src/GitMcp.Server/GitMcp.Server.csproj`.
- Silent install: `GitMcpSetup-<ver>.exe /VERYSILENT /SUPPRESSMSGBOXES /TASKS=claude` (kills running GitMcp.Server.exe).

## Rules

- Every tool goes through `GitService` (path validation + per-repo lock). Read-only commands use `ReadAsync`,
  anything that changes a repo uses `WriteAsync`; never call `GitRunner` from a tool directly.
- Values that name refs/remotes/URLs go through `GitArgs.Ref`/`CheckRef` (reject leading `-`); file paths go through
  `GitArgs.Paths` (after `--`). `git_run` must keep refusing global options.
- Keep the HTTP Origin check and the AllowedHosts default in `Program.cs`.
- Adding or renaming a tool: update the README tool table and the expected list in `tests/GitMcp.Tests/ServerTests.cs`.
- Tests must never write global or system git config (they would change Pete's real config).
- Ask before reinstalling over Pete's install or touching `claude_desktop_config.json` outside the installer.
- Git identity for this repo: Pete / peterbobwalton@gmail.com.
