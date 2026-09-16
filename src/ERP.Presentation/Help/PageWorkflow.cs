using System.Globalization;

namespace ERP.Presentation.Help;

/// <summary>One line of «راهنمای این صفحه» (§4.1) — what to do with one control, in Persian.</summary>
public sealed record WorkflowStep(string Control, string InstructionFa, string Action);

/// <summary>The whole walkthrough for one page — what a Help/Workflows/*.yaml file describes.</summary>
public sealed record PageWorkflow(string Page, string TitleFa, IReadOnlyList<WorkflowStep> Steps);

/// <summary>
/// Reads the fixed, flat shape every file under Help/Workflows/ uses:
/// <c>page:</c>, <c>title_fa:</c>, then a <c>steps:</c> list of
/// <c>control:</c>/<c>instruction_fa:</c>/<c>action:</c> triples. A real YAML
/// library would be overkill for one hand-written, always-this-shape schema —
/// this parser only understands that shape and throws a plain message on
/// anything else, rather than silently misreading a typo in a step.
/// </summary>
public static class PageWorkflowFile
{
    public static PageWorkflow Parse(string yaml, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        string? page = null;
        string? title = null;
        var steps = new List<WorkflowStep>();
        string? control = null;
        string? instruction = null;
        string? action = null;

        void FlushStep()
        {
            if (control is null)
            {
                return;
            }

            if (instruction is null || action is null)
            {
                throw new FormatException(
                    $"«{sourceName}»: مرحله‌ی «{control}» ناقص است (instruction_fa یا action ندارد).");
            }

            steps.Add(new WorkflowStep(control, instruction, action));
            control = instruction = action = null;
        }

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            var isListItem = trimmed.StartsWith("- ", StringComparison.Ordinal);
            if (isListItem)
            {
                FlushStep();
                trimmed = trimmed[2..];
            }

            var colon = trimmed.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            var value = Unquote(trimmed[(colon + 1)..].Trim());

            switch (key)
            {
                case "page": page = value; break;
                case "title_fa": title = value; break;
                case "control": control = value; break;
                case "instruction_fa": instruction = value; break;
                case "action": action = value; break;
            }
        }

        FlushStep();

        if (page is null || title is null)
        {
            throw new FormatException($"«{sourceName}»: page یا title_fa ندارد.");
        }

        if (steps.Count == 0)
        {
            throw new FormatException($"«{sourceName}»: هیچ مرحله‌ای ندارد.");
        }

        return new PageWorkflow(page, title, steps);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
        {
            return value[1..^1];
        }

        return value;
    }
}

/// <summary>
/// Loads a page's workflow from Help/Workflows/&lt;page&gt;.yaml next to the
/// app (a Content item, so it ships in both the packaged MSIX and an
/// unpackaged run), caching each file after its first read since the content
/// never changes while the app is running.
/// </summary>
public sealed class WorkflowLoader
{
    private readonly string _directory;
    private readonly Dictionary<string, PageWorkflow?> _cache = [];

    /// <param name="workflowsDirectory">
    /// Full path to the folder holding the *.yaml files. Defaults to
    /// Help/Workflows next to the running app; tests pass their own
    /// temporary folder instead of touching the real one.
    /// </param>
    public WorkflowLoader(string? workflowsDirectory = null)
    {
        _directory = workflowsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Help", "Workflows");
    }

    /// <summary>Null if the page has no workflow file yet — callers show nothing rather than an error (§15.20).</summary>
    public PageWorkflow? Load(string page)
    {
        if (_cache.TryGetValue(page, out var cached))
        {
            return cached;
        }

        var fileName = string.Create(CultureInfo.InvariantCulture, $"{page}.yaml");
        var path = Path.Combine(_directory, fileName);
        var workflow = File.Exists(path) ? PageWorkflowFile.Parse(File.ReadAllText(path), fileName) : null;
        _cache[page] = workflow;
        return workflow;
    }
}
