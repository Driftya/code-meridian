using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindFrontendCascadeConflictsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindFrontendCascadeConflictsAsync_WithIndexedSpecificityMetadata_ReturnsLikelyOverrides()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([
                CreateFrontendStyleDeclaration("d1", ".hero", "src/web/site.scss", 4, "color", "red", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "1",
                    ["specificityA"] = "0",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero"
                }),
                CreateFrontendStyleDeclaration("d2", ".hero", "src/web/site.scss", 8, "color", "blue", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "2",
                    ["specificityA"] = "0",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero"
                }),
                CreateFrontendStyleDeclaration("d3", ".layout .hero", "src/web/site.scss", 12, "color", "navy", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "3",
                    ["specificityA"] = "0",
                    ["specificityB"] = "2",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "layout,hero"
                }),
                CreateFrontendStyleDeclaration("d4", "#page .hero", "src/web/site.scss", 16, "color", "white", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "4",
                    ["specificityA"] = "1",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero",
                    ["targetIdConceptsCsv"] = "page"
                })
            ]);

        var result = await sut.FindFrontendCascadeConflictsAsync(projectContext: "Shop.Web");

        result.Should().Contain("## Frontend Cascade Conflicts - Shop.Web");
        result.Should().Contain("likely override/conflict relationships");
        result.Should().Contain("same specificity `0,1,0` and later source order");
        result.Should().Contain("higher specificity `0,2,0` over `0,1,0`");
        result.Should().Contain("`CssClass:hero`");
        result.Should().Contain("Suspiciously Specific Selectors");
        result.Should().Contain(".layout .hero");
        result.Should().Contain("#page .hero");
        result.Should().Contain("inferred from indexed selector specificity");
    }


}

