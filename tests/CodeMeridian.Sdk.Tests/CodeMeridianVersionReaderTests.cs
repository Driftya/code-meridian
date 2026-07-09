using System.Reflection;
using System.Reflection.Emit;
using CodeMeridian.Sdk.Versioning;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class CodeMeridianVersionReaderTests
{
    [Fact]
    public void ReadFrom_ReturnsVersionAndMetadataFromAssembly()
    {
        var assembly = CreateAssembly(
            informationalVersion: "9.8.7-preview",
            graphContractVersion: "4",
            cacheVersion: "3");
        var version = CodeMeridianVersionReader.ReadFrom(assembly, "CodeMeridian.Test");

        version.Should().BeEquivalentTo(new CodeMeridianComponentVersion("CodeMeridian.Test", "9.8.7-preview", 4, 3));
    }

    [Fact]
    public void ReadFrom_UsesFallbacksWhenMetadataIsMissingOrInvalid()
    {
        var assembly = CreateAssembly(
            informationalVersion: null,
            graphContractVersion: "invalid",
            cacheVersion: null);
        var version = CodeMeridianVersionReader.ReadFrom(assembly, "CodeMeridian.Test");

        version.ProductVersion.Should().Be("0.0.0-unknown");
        version.GraphContractVersion.Should().Be(0);
        version.CacheVersion.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadFrom_RejectsMissingComponentNames(string? component)
    {
        var assembly = CreateAssembly(
            informationalVersion: "9.8.7-preview",
            graphContractVersion: "4",
            cacheVersion: "3");
        var act = () => CodeMeridianVersionReader.ReadFrom(assembly, component!);

        act.Should().Throw<ArgumentException>();
    }

    private static Assembly CreateAssembly(string? informationalVersion, string? graphContractVersion, string? cacheVersion)
    {
        var assemblyName = new AssemblyName($"CodeMeridian.VersionReader.Tests.{Guid.NewGuid():N}");
        var builder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);

        if (informationalVersion is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
                [informationalVersion]));
        }

        if (graphContractVersion is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["GraphContractVersion", graphContractVersion]));
        }

        if (cacheVersion is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["CacheVersion", cacheVersion]));
        }

        return builder;
    }
}
