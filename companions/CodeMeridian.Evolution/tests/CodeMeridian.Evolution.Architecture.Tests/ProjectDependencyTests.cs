using System.Xml.Linq;

namespace CodeMeridian.Evolution.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly Dictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["CodeMeridian.Evolution.Domain"] = new HashSet<string>(StringComparer.Ordinal),
            ["CodeMeridian.Evolution.Application"] = new HashSet<string>(
                ["CodeMeridian.Evolution.Domain"],
                StringComparer.Ordinal),
            ["CodeMeridian.Evolution.Infrastructure"] = new HashSet<string>(
                ["CodeMeridian.Evolution.Application", "CodeMeridian.Evolution.Domain"],
                StringComparer.Ordinal),
            ["CodeMeridian.Evolution.Api"] = new HashSet<string>(
                ["CodeMeridian.Evolution.Application", "CodeMeridian.Evolution.Infrastructure"],
                StringComparer.Ordinal),
            ["CodeMeridian.Evolution.Worker"] = new HashSet<string>(
                ["CodeMeridian.Evolution.Application", "CodeMeridian.Evolution.Infrastructure"],
                StringComparer.Ordinal)
        };

    [Fact]
    public void ProductionProjectReferencesStayInsideCompanionBoundary()
    {
        var companionRoot = FindCompanionRoot();
        var boundary = companionRoot.FullName + Path.DirectorySeparatorChar;

        foreach (var projectFile in GetProductionProjectFiles(companionRoot))
        {
            foreach (var reference in GetProjectReferences(projectFile))
            {
                var resolvedReference = Path.GetFullPath(
                    Path.Combine(projectFile.DirectoryName!, reference));

                Assert.True(
                    resolvedReference.StartsWith(boundary, StringComparison.OrdinalIgnoreCase),
                    $"{projectFile.Name} references a project outside the standalone companion: {reference}");
            }
        }
    }

    [Fact]
    public void ProductionProjectsRespectCleanArchitectureDependencies()
    {
        var companionRoot = FindCompanionRoot();

        foreach (var projectFile in GetProductionProjectFiles(companionRoot))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
            var actualReferences = GetProjectReferences(projectFile)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(
                AllowedReferences.TryGetValue(projectName, out var allowed),
                $"No architecture rule is registered for {projectName}.");
            Assert.True(
                actualReferences.SetEquals(allowed!),
                $"{projectName} references [{string.Join(", ", actualReferences)}], " +
                $"expected [{string.Join(", ", allowed!)}].");
        }
    }

    [Fact]
    public void DomainProjectHasNoExternalPackages()
    {
        var companionRoot = FindCompanionRoot();
        var domainProject = new FileInfo(Path.Combine(
            companionRoot.FullName,
            "src",
            "CodeMeridian.Evolution.Domain",
            "CodeMeridian.Evolution.Domain.csproj"));
        var document = XDocument.Load(domainProject.FullName);

        Assert.Empty(document.Descendants("PackageReference"));
    }

    private static DirectoryInfo FindCompanionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "CodeMeridian.Evolution.slnx")))
        {
            current = current.Parent;
        }

        return current ?? throw new DirectoryNotFoundException(
            "Could not locate the Meridian Evolution solution root.");
    }

    private static IEnumerable<FileInfo> GetProductionProjectFiles(DirectoryInfo companionRoot)
    {
        return new DirectoryInfo(Path.Combine(companionRoot.FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .OrderBy(file => file.FullName, StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetProjectReferences(FileInfo projectFile)
    {
        return XDocument.Load(projectFile.FullName)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!);
    }
}
