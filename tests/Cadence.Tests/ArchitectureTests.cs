using System.Xml.Linq;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Cadence_consumes_Tandem_as_packages_not_source_projects()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Cadence", "Cadence.csproj"));
        var packageNames = project
            .Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .ToArray();
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .ToArray();

        packageNames.Should().Contain(["Tandem", "Tandem.Advanced", "Tandem.Generators"]);
        projectReferences
            .Any(reference =>
                reference is not null
                && reference.Contains("tandem", StringComparison.OrdinalIgnoreCase)
            )
            .Should()
            .BeFalse();

        var host = XDocument.Load(Path.Combine(root, "src", "Cadence.Host", "Cadence.Host.csproj"));
        var hostPackages = host.Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .ToArray();
        hostPackages.Should().Contain("Tandem.Packets");
        hostPackages.Should().NotContain("YamlDotNet");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Cadence.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("Could not find the Cadence repository root.");
    }
}
