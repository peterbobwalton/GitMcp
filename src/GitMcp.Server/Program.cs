// GitMcp: an MCP server that wraps the git CLI.
//
//   GitMcp.Server                          streamable HTTP on http://127.0.0.1:5180/mcp (many clients at once)
//   GitMcp.Server --urls http://127.0.0.1:6000
//   GitMcp.Server --stdio                  one client over stdin/stdout (what Claude Desktop launches)
//   GitMcp.Server --register [--name git]  add to Claude Desktop's claude_desktop_config.json
//   GitMcp.Server --unregister [--name git]
//
// Settings: appsettings.json next to the exe, GitMcp__* environment variables, or --GitMcp:Key=value.

using System.Reflection;
using GitMcp.Server;
using GitMcp.Server.Git;
using ModelContextProtocol.Protocol;

if (args.Contains("--register") || args.Contains("--unregister"))
{
    return ClaudeConfig.Run(args);
}
if (args.Contains("--version"))
{
    Console.WriteLine(ServerSetup.Version);
    return 0;
}

var stdio = args.Contains("--stdio");
var hostArgs = args.Where(a => a != "--stdio").ToArray();

if (stdio)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = hostArgs,
        ContentRootPath = AppContext.BaseDirectory,
    });
    // stdout is the MCP channel: every log line goes to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    ServerSetup.AddGitMcp(builder.Services, builder.Configuration)
        .WithStdioServerTransport();
    await builder.Build().RunAsync();
}
else
{
    var app = ServerSetup.BuildHttpApp(hostArgs);
    await app.RunAsync();
}
return 0;

public static class ServerSetup
{
    public const string DefaultUrl = "http://127.0.0.1:5180";

    public static string Version =>
        typeof(ServerSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static IMcpServerBuilder AddGitMcp(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitMcpOptions>(configuration.GetSection(GitMcpOptions.Section));
        services.AddSingleton<GitService>();
        return services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = "GitMcp", Version = Version };
                o.ServerInstructions =
                    "Git tools. Every tool takes the absolute path of the repository, so several sessions can work on " +
                    "different repositories at once. Writes to the same repository are serialized; reads run in parallel. " +
                    "Prefer the typed tools; git_run covers anything else. Interactive commands are not supported.";
            })
            .WithToolsFromAssembly(typeof(ServerSetup).Assembly);
    }

    /// <summary>The streamable HTTP server. Tests call this with --urls http://127.0.0.1:0.</summary>
    public static WebApplication BuildHttpApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }
        // Refuse requests addressed to any other host name (DNS rebinding). In code rather than only in
        // appsettings.json, which upgrades keep as the user left it. Set AllowedHosts when serving the LAN.
        if (string.IsNullOrEmpty(builder.Configuration["AllowedHosts"]))
        {
            builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
        }
        AddGitMcp(builder.Services, builder.Configuration).WithHttpTransport();

        var app = builder.Build();
        // A web page in the user's browser can reach a localhost server. Browsers always send Origin on such
        // requests, so refuse any that don't come from this machine (the MCP spec requires the check). DNS
        // rebinding is covered by AllowedHosts in appsettings.json.
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !IsLocalOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Cross-origin requests are not allowed.");
                return;
            }
            await next(context);
        });
        app.MapMcp("/mcp");
        app.MapGet("/", () => Results.Text($"GitMcp {Version}: MCP endpoint at /mcp\n"));
        return app;
    }

    public static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
