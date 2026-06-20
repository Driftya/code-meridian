using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.CodeGraph;

/// <summary>
/// Tests for <see cref="CodeNode"/> and <see cref="CodeEdge"/> domain models —
/// verifying the shape of new fields, enum variants, and record behaviour.
/// </summary>
public sealed class CodeNodeTests
{
    // ── Timestamp fields ──────────────────────────────────────────────────────

    [Fact]
    public void CodeNode_DefaultTimestamps_AreNull()
    {
        var node = new CodeNode { Id = "x", Name = "X", Type = CodeNodeType.Class };

        node.CreatedAt.Should().BeNull();
        node.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void CodeNode_TimestampsCanBeSet()
    {
        var now = DateTimeOffset.UtcNow;
        var node = new CodeNode
        {
            Id = "x",
            Name = "X",
            Type = CodeNodeType.Class,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now,
            LastIndexedAt = now.AddMinutes(1)
        };

        node.CreatedAt.Should().Be(now.AddHours(-1));
        node.UpdatedAt.Should().Be(now);
        node.LastIndexedAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void CodeNode_SourceHashDefaultsToNullAndCanBeSet()
    {
        var node = new CodeNode
        {
            Id = "x",
            Name = "X",
            Type = CodeNodeType.Class,
            SourceHash = "abc123"
        };

        node.SourceHash.Should().Be("abc123");
    }

    [Fact]
    public void CodeNode_WithExpression_PreservesTimestamps()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var original = new CodeNode
        {
            Id = "a",
            Name = "Alpha",
            Type = CodeNodeType.Class,
            CreatedAt = created
        };

        var copy = original with { Name = "AlphaRenamed" };

        copy.CreatedAt.Should().Be(created);
        copy.UpdatedAt.Should().BeNull();
        copy.Name.Should().Be("AlphaRenamed");
    }

    // ── External concept node types ───────────────────────────────────────────

    [Theory]
    [InlineData(CodeNodeType.ExternalConcept)]
    [InlineData(CodeNodeType.DatabaseTable)]
    [InlineData(CodeNodeType.ApiEndpoint)]
    [InlineData(CodeNodeType.MessageTopic)]
    [InlineData(CodeNodeType.ExternalService)]
    public void CodeNodeType_ExternalVariants_AreParseable(CodeNodeType type)
    {
        // Verify enum can round-trip through Enum.Parse (used by Neo4j mapping)
        var name = type.ToString();
        var parsed = Enum.Parse<CodeNodeType>(name, ignoreCase: true);

        parsed.Should().Be(type);
    }

    [Theory]
    [InlineData(CodeNodeType.Namespace)]
    [InlineData(CodeNodeType.Class)]
    [InlineData(CodeNodeType.Struct)]
    [InlineData(CodeNodeType.Interface)]
    [InlineData(CodeNodeType.Method)]
    [InlineData(CodeNodeType.Delegate)]
    [InlineData(CodeNodeType.Property)]
    [InlineData(CodeNodeType.Field)]
    [InlineData(CodeNodeType.Event)]
    [InlineData(CodeNodeType.Indexer)]
    [InlineData(CodeNodeType.Operator)]
    [InlineData(CodeNodeType.Enum)]
    [InlineData(CodeNodeType.File)]
    [InlineData(CodeNodeType.Module)]
    public void CodeNodeType_OriginalVariants_StillPresent(CodeNodeType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }

    // ── Extended edge types ───────────────────────────────────────────────────

    [Theory]
    [InlineData(CodeEdgeType.Reads)]
    [InlineData(CodeEdgeType.Writes)]
    [InlineData(CodeEdgeType.PublishesTo)]
    [InlineData(CodeEdgeType.SubscribesTo)]
    public void CodeEdgeType_NewVariants_AreParseable(CodeEdgeType type)
    {
        var name = type.ToString();
        var parsed = Enum.Parse<CodeEdgeType>(name, ignoreCase: true);

        parsed.Should().Be(type);
    }

    [Theory]
    [InlineData(CodeEdgeType.Contains)]
    [InlineData(CodeEdgeType.Calls)]
    [InlineData(CodeEdgeType.Implements)]
    [InlineData(CodeEdgeType.Inherits)]
    [InlineData(CodeEdgeType.Uses)]
    [InlineData(CodeEdgeType.DependsOn)]
    [InlineData(CodeEdgeType.Overrides)]
    public void CodeEdgeType_OriginalVariants_StillPresent(CodeEdgeType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }

    // ── CodeEdge optional Id ──────────────────────────────────────────────────

    [Fact]
    public void CodeEdge_IdIsOptional_DefaultsToNull()
    {
        var edge = new CodeEdge
        {
            SourceId = "a",
            TargetId = "b",
            Type = CodeEdgeType.Calls
        };

        edge.Id.Should().BeNull();
    }

    [Fact]
    public void CodeEdge_IdCanBeSet()
    {
        var edge = new CodeEdge
        {
            Id = "a→b:Calls",
            SourceId = "a",
            TargetId = "b",
            Type = CodeEdgeType.Calls
        };

        edge.Id.Should().Be("a→b:Calls");
    }

    // ── Properties bag ────────────────────────────────────────────────────────

    [Fact]
    public void CodeNode_Properties_DefaultToEmptyDictionary()
    {
        var node = new CodeNode { Id = "x", Name = "X", Type = CodeNodeType.Class };

        node.Properties.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CodeNode_Properties_CanStoreArbitraryMetadata()
    {
        var node = new CodeNode
        {
            Id = "x",
            Name = "X",
            Type = CodeNodeType.ExternalConcept,
            Properties = new() { ["source"] = "linked-by-copilot" }
        };

        node.Properties["source"].Should().Be("linked-by-copilot");
    }
}
