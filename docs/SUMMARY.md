# Summary

## Request (Pete, 2026-10-09)

A git MCP tool in C# / Visual Studio 2026, named GitMcp, in `C:\source\Claude`, with multi-session support so several
tasks can use the server at once, covering all commonly used git commands. Follow-ups: install per user into the
roaming folder with Inno Setup and register in Claude's JSON config; install it; test it; code review and fix errors.

## What was built

- **Server:** .NET 10, official ModelContextProtocol C# SDK 2.2.0. Streamable HTTP by default (stateless mode, so
  any number of clients and no session affinity) or `--stdio`.
- **Multi-session model:** every tool takes the repository path; nothing is per-session state. Per-repository
  reader/writer lock: reads in parallel, writes serialized, plus a lock file so separate GitMcp processes
  (one per Claude Desktop window) also serialize writes.
- **44 tools:** init, clone, status, add, restore, rm, mv, commit, log, show, diff, blame; branch list/create/delete/
  rename, checkout, switch; merge, rebase, cherry-pick, revert (each with continue/abort/skip/quit); reset, clean;
  stash push/pop/apply/list/show/drop; tag list/create/delete; fetch, pull, push; remote list/add/remove/set-url;
  config get/set/list; `git_run` for anything else.
- **Safety:** absolute paths only, optional AllowedRoots, refs can't start with `-`, file paths after `--`,
  `git_clean` dry run by default, `git_run` refuses global options, HTTP refuses foreign Origins and Host names,
  output truncated at 200,000 characters, timeouts on every command.
- **Installer:** `Build-Installer.ps1` + `installer/GitMcp.iss`, per user to `%APPDATA%\GitMcp`, registers a stdio
  `"git"` entry in `claude_desktop_config.json` (backup first, removed on uninstall).
- **Tests:** 48 xUnit tests: every tool on real repos, conflicts and aborts, remotes via a bare repo, validation,
  lock semantics, 16 parallel commits on one repo, cross-process lock file, HTTP end to end with the SDK client,
  Origin/Host rejection, Claude config editing.

## Code review (0.1.0 -> 0.1.1)

11 issues fixed: HTTP open to browser pages (Origin/Host checks added); `git_run` global options escaping repoPath;
`push -u` without refspec failing; pull ignoring `pull.rebase`; config "not set" masking real errors and needing a
repo for effective values; global config writes not sharing a lock; linked worktrees taking separate locks; unbounded
output capture; errors cut off by long stdout; drive-root AllowedRoots never matching; progress-line noise.

## Decisions

- HTTP default port 5180, localhost only. Claude Desktop entry uses stdio because its config can't point at a URL
  (mcp-remote is the alternative, documented in the README).
- Registration code copied from PCHardwareMcp so all of Pete's MCP installers behave the same.
- Server name in Claude config: `git` (tools appear as `git_*`).
