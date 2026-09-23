using System.Xml.Linq;

namespace ERP.Architecture.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void DomainHasNoProjectReferences()
    {
        var references = ReadProjectReferences("src/ERP.Domain/ERP.Domain.csproj");

        Assert.Empty(references);
    }

    [Fact]
    public void ApplicationReferencesOnlyDomain()
    {
        var references = ReadProjectReferences("src/ERP.Application/ERP.Application.csproj");

        Assert.Equal(["ERP.Domain"], references);
    }

    private static string[] ReadProjectReferences(string repositoryRelativePath)
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, repositoryRelativePath));

        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            // csproj paths use '\'; only Windows treats it as a separator, so normalize for Linux CI
            .Select(path => Path.GetFileNameWithoutExtension(path!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();
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

        throw new DirectoryNotFoundException("ریشه پروژه RetailERP پیدا نشد.");
    }
}
