using System.Text.Json.Nodes;
using GitMcp.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GitMcp.Tests;

/// <summary>End to end over streamable HTTP, the way a real client talks to the server.</summary>
public sealed class ServerTests : IAsyncLifetime
{
    private WebApplication? _app;
    private Uri? _endpoint;

    public async Task InitializeAsync()
    {
        var locks = Path.Combine(Path.GetTempPath(), "GitMcpTests", "server-locks");
        _app = ServerSetup.BuildHttpApp(["--urls", "http://127.0.0.1:0", $"--GitMcp:LockDirectory={locks}"]);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _endpoint = new Uri(address.TrimEnd('/') + "/mcp");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = _endpoint! }));

    private static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task Lists_All_Git_Tools()
    {
        await using var client = await ConnectAsync();
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        string[] expected =
        [
            "git_init", "git_clone", "git_status", "git_add", "git_restore", "git_rm", "git_mv", "git_commit",
            "git_log", "git_show", "git_diff", "git_branch_list", "git_branch_create", "git_branch_delete",
            "git_branch_rename", "git_checkout", "git_switch", "git_merge", "git_rebase", "git_cherry_pick",
            "git_revert", "git_reset", "git_stash_push", "git_stash_pop", "git_stash_list", "git_stash_apply",
            "git_stash_drop", "git_stash_show", "git_tag_list", "git_tag_create", "git_tag_delete", "git_fetch",
            "git_pull", "git_push", "git_remote_list", "git_remote_add", "git_remote_remove", "git_remote_set_url",
            "git_blame", "git_config_get", "git_config_set", "git_config_list", "git_clean", "git_run",
        ];
        Assert.Empty(expected.Except(names));
    }

    [Fact]
    public async Task Two_Clients_Work_On_Their_Own_Repositories_Concurrently()
    {
        using var t = new TestRepo();
        await using var c1 = await ConnectAsync();
        await using var c2 = await ConnectAsync();

        async Task Work(McpClient client, string name)
        {
            var repo = Path.Combine(t.Root, name);
            await client.CallToolAsync("git_init", new Dictionary<string, object?> { ["path"] = repo, ["initialBranch"] = "main" });
            foreach (var (k, v) in new[] { ("user.name", name), ("user.email", $"{name}@gitmcp.local"), ("commit.gpgsign", "false") })
            {
                await client.CallToolAsync("git_config_set", new Dictionary<string, object?> { ["repoPath"] = repo, ["key"] = k, ["value"] = v });
            }
            File.WriteAllText(Path.Combine(repo, "f.txt"), name);
            await client.CallToolAsync("git_add", new Dictionary<string, object?> { ["repoPath"] = repo, ["paths"] = new[] { "f.txt" } });
            var commit = await client.CallToolAsync("git_commit", new Dictionary<string, object?> { ["repoPath"] = repo, ["message"] = $"by {name}" });
            Assert.NotEqual(true, commit.IsError);
            var log = await client.CallToolAsync("git_log", new Dictionary<string, object?> { ["repoPath"] = repo, ["oneline"] = true });
            Assert.Contains($"by {name}", Text(log));
        }

        await Task.WhenAll(Work(c1, "one"), Work(c2, "two"));
    }

    [Theory]
    [InlineData("http://evil.example", null, 403)]
    [InlineData("null", null, 403)]
    [InlineData(null, "evil.example", 400)]
    [InlineData("http://localhost:3000", null, 200)]
    [InlineData(null, null, 200)]
    public async Task Rejects_Foreign_Origins_And_Hosts(string? origin, string? host, int expected)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        if (host is not null)
        {
            request.Headers.Host = host;
        }
        using var response = await http.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task Git_Failure_Comes_Back_As_Tool_Error()
    {
        using var t = new TestRepo();
        await using var client = await ConnectAsync();
        var result = await client.CallToolAsync("git_status", new Dictionary<string, object?> { ["repoPath"] = Path.Combine(t.Root, "missing") });
        Assert.True(result.IsError);
        Assert.Contains("not found", Text(result));
    }
}

public sealed class ClaudeConfigTests
{
    [Fact]
    public void Register_Preserves_Other_Entries_And_Unregister_Removes_Only_Ours()
    {
        var dir = Path.Combine(Path.GetTempPath(), "GitMcpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "claude_desktop_config.json");
        File.WriteAllText(path, """
            {
              // comments and trailing commas are tolerated
              "mcpServers": { "other": { "command": "other.exe" }, },
              "preferences": { "x": true }
            }
            """);
        try
        {
            var entry = new JsonObject { ["command"] = @"C:\Users\me\AppData\Roaming\GitMcp\server\GitMcp.Server.exe", ["args"] = new JsonArray("--stdio") };
            Assert.True(ClaudeConfig.Register(path, "git", entry));
            Assert.False(ClaudeConfig.Register(path, "git", (JsonObject)entry.DeepClone())); // idempotent

            var root = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal("other.exe", (string?)root["mcpServers"]!["other"]!["command"]);
            Assert.Equal("--stdio", (string?)root["mcpServers"]!["git"]!["args"]![0]);
            Assert.True((bool)root["preferences"]!["x"]!);

            Assert.True(ClaudeConfig.Unregister(path, "git"));
            root = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Null(root["mcpServers"]!["git"]);
            Assert.NotNull(root["mcpServers"]!["other"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
