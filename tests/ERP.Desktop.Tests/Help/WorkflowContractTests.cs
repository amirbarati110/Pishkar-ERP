namespace ERP.Desktop.Tests.Help;

public sealed class WorkflowContractTests
{
    [Theory]
    [InlineData("category-management.yaml", "categories")]
    [InlineData("product-create.yaml", "product-editor")]
    [InlineData("opening-stock.yaml", "opening-stock")]
    public void EveryPageHasCompletePersianWorkflow(string fileName, string expectedPage)
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "ERP.Desktop",
            "Help",
            "Workflows",
            fileName);

        Assert.True(File.Exists(path), $"Workflow file is missing: {fileName}");
        var content = File.ReadAllText(path);
        Assert.Contains($"page: {expectedPage}", content, StringComparison.Ordinal);
        Assert.Contains("control:", content, StringComparison.Ordinal);
        Assert.Contains("instruction_fa:", content, StringComparison.Ordinal);
        Assert.Contains("action:", content, StringComparison.Ordinal);
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
