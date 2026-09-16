using ERP.Presentation.Help;

namespace ERP.Desktop.Tests.Help;

/// <summary>
/// The hand-written parser behind «راهنمای این صفحه» (§4.1) — it only ever
/// has to understand the one flat shape every Help/Workflows/*.yaml file
/// uses, so these check that shape rather than general YAML correctness.
/// </summary>
public sealed class PageWorkflowFileTests
{
    private const string Valid = """
        page: sales-workspace
        title_fa: فروش سریع
        steps:
          - control: product-search
            instruction_fa: بارکد کالا را اسکن کنید.
            action: focus
          - control: invoice-lines
            instruction_fa: تعداد را با + و − عوض کنید.
            action: invoke
        """;

    [Fact]
    public void ParsesThePageTitleAndEveryStepInOrder()
    {
        var workflow = PageWorkflowFile.Parse(Valid, "sales-workspace.yaml");

        Assert.Equal("sales-workspace", workflow.Page);
        Assert.Equal("فروش سریع", workflow.TitleFa);
        Assert.Equal(2, workflow.Steps.Count);
        Assert.Equal("product-search", workflow.Steps[0].Control);
        Assert.Equal("بارکد کالا را اسکن کنید.", workflow.Steps[0].InstructionFa);
        Assert.Equal("focus", workflow.Steps[0].Action);
        Assert.Equal("invoice-lines", workflow.Steps[1].Control);
        Assert.Equal("invoke", workflow.Steps[1].Action);
    }

    [Fact]
    public void QuotedValuesAreUnquoted()
    {
        const string yaml = """
            page: home
            title_fa: "میز کار"
            steps:
              - control: tile
                instruction_fa: 'برای شروع کلیک کنید.'
                action: invoke
            """;

        var workflow = PageWorkflowFile.Parse(yaml, "home.yaml");

        Assert.Equal("میز کار", workflow.TitleFa);
        Assert.Equal("برای شروع کلیک کنید.", workflow.Steps[0].InstructionFa);
    }

    [Fact]
    public void BlankLinesAndCommentsAreIgnored()
    {
        const string yaml = """
            # این یک راهنماست
            page: home

            title_fa: میز کار
            steps:
              # مرحله‌ی اول
              - control: tile
                instruction_fa: کلیک کنید.
                action: invoke
            """;

        var workflow = PageWorkflowFile.Parse(yaml, "home.yaml");

        Assert.Single(workflow.Steps);
    }

    [Theory]
    [InlineData("title_fa: بدون page\nsteps:\n  - control: a\n    instruction_fa: b\n    action: c")]
    [InlineData("page: home\nsteps:\n  - control: a\n    instruction_fa: b\n    action: c")]
    public void MissingPageOrTitleFails(string yaml)
    {
        Assert.Throws<FormatException>(() => PageWorkflowFile.Parse(yaml, "broken.yaml"));
    }

    [Fact]
    public void NoStepsFails()
    {
        const string yaml = "page: home\ntitle_fa: میز کار";

        Assert.Throws<FormatException>(() => PageWorkflowFile.Parse(yaml, "broken.yaml"));
    }

    [Fact]
    public void AStepMissingInstructionOrActionFails()
    {
        const string yaml = """
            page: home
            title_fa: میز کار
            steps:
              - control: tile
                instruction_fa: کلیک کنید.
            """;

        var exception = Assert.Throws<FormatException>(() => PageWorkflowFile.Parse(yaml, "broken.yaml"));
        Assert.Contains("tile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRealWorkflowFileParsesWithoutError()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "src", "ERP.Desktop", "Help", "Workflows");
        var files = Directory.GetFiles(directory, "*.yaml");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var workflow = PageWorkflowFile.Parse(File.ReadAllText(file), Path.GetFileName(file));
            Assert.NotEmpty(workflow.Page);
            Assert.NotEmpty(workflow.TitleFa);
            Assert.NotEmpty(workflow.Steps);
            Assert.All(workflow.Steps, step => Assert.NotEmpty(step.InstructionFa));
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RetailERP.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ریشه پروژه پیدا نشد.");
    }
}

public sealed class WorkflowLoaderTests
{
    [Fact]
    public void LoadsAFileFromTheGivenDirectory()
    {
        var directory = MakeTempDirectory();
        File.WriteAllText(
            Path.Combine(directory, "home.yaml"),
            """
            page: home
            title_fa: میز کار
            steps:
              - control: tile
                instruction_fa: کلیک کنید.
                action: invoke
            """);
        var loader = new WorkflowLoader(directory);

        var workflow = loader.Load("home");

        Assert.NotNull(workflow);
        Assert.Equal("میز کار", workflow!.TitleFa);
    }

    [Fact]
    public void AMissingFileComesBackNullRatherThanThrowing()
    {
        var loader = new WorkflowLoader(MakeTempDirectory());

        Assert.Null(loader.Load("no-such-page"));
    }

    [Fact]
    public void ASecondLoadReusesTheCachedResultEvenIfTheFileChangesAfterward()
    {
        var directory = MakeTempDirectory();
        var path = Path.Combine(directory, "home.yaml");
        File.WriteAllText(path, """
            page: home
            title_fa: نسخه یک
            steps:
              - control: tile
                instruction_fa: الف.
                action: invoke
            """);
        var loader = new WorkflowLoader(directory);
        var first = loader.Load("home");

        File.WriteAllText(path, """
            page: home
            title_fa: نسخه دو
            steps:
              - control: tile
                instruction_fa: ب.
                action: invoke
            """);
        var second = loader.Load("home");

        Assert.Equal("نسخه یک", first!.TitleFa);
        Assert.Same(first, second);
    }

    private static string MakeTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pishkar-help-tests-" + Guid.NewGuid().ToString("N"), "Help", "Workflows");
        Directory.CreateDirectory(path);
        return path;
    }
}
