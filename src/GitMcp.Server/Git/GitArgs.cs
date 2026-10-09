using ModelContextProtocol;

namespace GitMcp.Server.Git;

/// <summary>Fluent git argument list. Refs and names are checked so a value can never be read as an option.</summary>
public sealed class GitArgs : List<string>
{
    public GitArgs(params string[] start) : base(start) { }

    public GitArgs Flag(bool condition, string flag)
    {
        if (condition)
        {
            Add(flag);
        }
        return this;
    }

    /// <summary>Adds "--name=value" when value is set.</summary>
    public GitArgs Opt(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Add($"{name}={value}");
        }
        return this;
    }

    public GitArgs Opt(string name, int? value) => value is null ? this : Opt(name, value.Value.ToString());

    /// <summary>A revision, branch, remote, tag or similar positional value.</summary>
    public GitArgs Ref(string? value, string what = "value")
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Add(CheckRef(value, what));
        }
        return this;
    }

    public GitArgs Refs(IEnumerable<string>? values, string what = "value")
    {
        foreach (var v in values ?? [])
        {
            Ref(v, what);
        }
        return this;
    }

    /// <summary>Pathspecs, always after "--" so a file named like an option or a branch is still a path.</summary>
    public GitArgs Paths(IEnumerable<string>? paths)
    {
        var list = (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count > 0)
        {
            Add("--");
            AddRange(list);
        }
        return this;
    }

    public static string CheckRef(string value, string what = "value")
    {
        value = value.Trim();
        if (value.StartsWith('-'))
        {
            throw new McpException($"Invalid {what} '{value}': must not start with '-'.");
        }
        return value;
    }
}
