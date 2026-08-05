using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMeridian.Indexer.Tests.Roslyn;

public sealed class CSharpInvocationEvidenceTests
{
    [Fact]
    public void StaticReceiverEvidence_RequiresExplicitSyntaxCatalogOrAliasEvidence()
    {
        const string source = """
            using Clock = System.DateTime;

            namespace Demo;

            public static class LocalFactory
            {
                public static object Create() => new();
            }

            public sealed class Host
            {
                public void Execute()
                {
                    _ = string.IsNullOrWhiteSpace("value");
                    _ = int.Parse("42");
                    _ = DateTime.Parse("2026-08-05");
                    _ = System.DateTime.Parse("2026-08-05");
                    _ = Clock.Parse("2026-08-05");
                    _ = LocalFactory.Create();
                    _ = UnboundCapitalizedName.Create();
                }
            }
            """;

        var calls = ExtractCalls(source);

        calls.Where(call => call.CallName is "IsNullOrWhiteSpace" or "Parse")
            .Should().OnlyContain(call => call.Properties!["receiverKind"] == "TypedOrStatic");
        calls.Single(call => call.CallName == "IsNullOrWhiteSpace")
            .Properties.Should().Contain("receiverTypeHint", "string");
        calls.Should().Contain(call =>
            call.CallName == "Create"
            && call.Properties!["receiverTypeHint"] == "LocalFactory"
            && call.Properties["receiverEvidenceSource"] == "syntax-local-type-catalog");
        calls.Should().Contain(call =>
            call.CallName == "Create"
            && call.Properties!["receiverKind"] == "UnknownMember"
            && !call.Properties.ContainsKey("receiverTypeHint"));
    }

    [Fact]
    public void ExplicitReceiverShapes_PreserveExactTypeEvidence()
    {
        const string source = """
            namespace Demo;

            public interface IRunner
            {
                void RunParameter();
                void RunField();
                void RunProperty();
                void RunLocal();
                void RunVar();
                void RunCast();
                void RunAs();
                void RunConditional();
            }

            public sealed class Runner : IRunner
            {
                public void RunParameter() { }
                public void RunField() { }
                public void RunProperty() { }
                public void RunLocal() { }
                public void RunVar() { }
                public void RunCast() { }
                public void RunAs() { }
                public void RunConditional() { }
            }

            public sealed class Host
            {
                private readonly IRunner field = new Runner();
                private IRunner Property => field;

                public void Execute(IRunner parameter, object candidate, bool condition)
                {
                    IRunner local = parameter;
                    var inferred = new Runner();

                    parameter.RunParameter();
                    field.RunField();
                    Property.RunProperty();
                    local.RunLocal();
                    inferred.RunVar();
                    ((IRunner)candidate)!.RunCast();
                    (candidate as IRunner)!.RunAs();
                    (condition ? parameter : local).RunConditional();
                }
            }
            """;

        var calls = ExtractCalls(source)
            .Where(call => call.CallName?.StartsWith("Run", StringComparison.Ordinal) == true)
            .ToArray();

        calls.Should().HaveCount(8);
        calls.Should().OnlyContain(call =>
            call.Properties!["receiverKind"] == "TypedOrStatic"
            && call.Properties["receiverEvidenceConfidence"] == "Exact");
        calls.Single(call => call.CallName == "RunVar")
            .Properties.Should().Contain("receiverTypeHint", "Runner");
        calls.Where(call => call.CallName != "RunVar")
            .Should().OnlyContain(call => call.Properties!["receiverTypeHint"] == "IRunner");
    }

    [Fact]
    public void ExplicitContextVariables_PreserveExactTypeEvidence()
    {
        const string source = """
            namespace Demo;

            public interface IRunner
            {
                void RunLambda();
                void RunAnonymous();
                void RunForeach();
                void RunCatch();
                void RunUsing();
                void RunPattern();
                void RunDeconstruction();
            }

            public sealed class Host
            {
                public void Execute(object candidate, IRunner[] runners)
                {
                    System.Action<IRunner> lambda = (IRunner runner) => runner.RunLambda();
                    System.Action<IRunner> anonymous = delegate(IRunner runner) { runner.RunAnonymous(); };

                    foreach (IRunner runner in runners)
                        runner.RunForeach();

                    try { }
                    catch (RunnerException error) { error.RunCatch(); }

                    using (RunnerResource resource = new())
                        resource.RunUsing();

                    if (candidate is IRunner matched)
                        matched.RunPattern();

                    (IRunner deconstructed, _) = GetPair();
                    deconstructed.RunDeconstruction();
                }

                private (IRunner, object) GetPair() => throw new System.NotImplementedException();
            }

            public sealed class RunnerException : System.Exception
            {
                public void RunCatch() { }
            }

            public sealed class RunnerResource : System.IDisposable
            {
                public void RunUsing() { }
                public void Dispose() { }
            }
            """;

        var calls = ExtractCalls(source)
            .Where(call => call.CallName?.StartsWith("Run", StringComparison.Ordinal) == true)
            .ToArray();

        calls.Should().HaveCount(7);
        calls.Should().OnlyContain(call =>
            call.Properties!["receiverKind"] == "TypedOrStatic"
            && call.Properties["receiverEvidenceConfidence"] == "Exact");
        calls.Single(call => call.CallName == "RunDeconstruction")
            .Properties.Should().Contain("receiverTypeHint", "IRunner");
    }

    [Fact]
    public void ChainedReceiver_PreservesOnlyRootTypeProvenance()
    {
        const string source = """
            namespace Demo;

            public interface IServiceCollection
            {
                IServiceCollection AddFirst();
                IServiceCollection AddSecond();
            }

            public sealed class Host
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddFirst().AddSecond();
                }
            }
            """;

        var calls = ExtractCalls(source);

        calls.Single(call => call.CallName == "AddFirst")
            .Properties.Should().Contain("receiverKind", "TypedOrStatic");
        calls.Single(call => call.CallName == "AddSecond").Properties.Should().Contain(new Dictionary<string, string>
        {
            ["receiverKind"] = "Chained",
            ["receiverTypeHint"] = "IServiceCollection",
            ["receiverEvidenceSource"] = "syntax-chain-root",
            ["receiverEvidenceConfidence"] = "RootOnly"
        });
    }

    [Fact]
    public void LaterLocalDeclaration_DoesNotLeakTypeEvidenceBackward()
    {
        const string source = """
            namespace Demo;

            public interface IRunner
            {
                void Run();
            }

            public sealed class Host
            {
                public void Execute()
                {
                    future.Run();
                    IRunner future = default!;
                }
            }
            """;

        var call = ExtractCalls(source).Single(call => call.CallName == "Run");

        call.Properties.Should().Contain("receiverKind", "UnknownMember");
        call.Properties.Should().NotContainKey("receiverTypeHint");
    }

    private static IngestEdgeRequest[] ExtractCalls(string source)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var root = CSharpSyntaxTree.ParseText(source, path: "src/InvocationFixture.cs").GetCompilationUnitRoot();

        new CSharpAstWalker("src/InvocationFixture.cs", "Project", nodes, edges).Visit(root);

        return edges.Where(edge => edge.RelationshipType == "Calls").ToArray();
    }
}
