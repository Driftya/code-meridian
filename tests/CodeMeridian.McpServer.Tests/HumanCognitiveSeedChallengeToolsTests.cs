using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class HumanCognitiveSeedChallengeToolsTests
{
    [Fact]
    public async Task StartChangeContextChallengeAsync_WithholdsCorrectnessFromStructuredContent()
    {
        var contextService = ContextServiceWithExactNode();
        var tools = CreateTools(contextService);

        var result = await tools.StartChangeContextChallengeAsync("node", "Which code is correct?", Choices());

        var json = result.StructuredContent!.Value;
        json.GetProperty("requiredSelectionCount").GetInt32().Should().Be(2);
        json.GetProperty("choices").GetArrayLength().Should().Be(4);
        json.GetRawText().Should().NotContain("isCorrect");
        json.GetRawText().Should().NotContain("feedback");
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("## Choice A");
        text.Should().Contain("return Validate(input);");
        text.Should().NotContain("Choice B is wrong");
        await contextService.Received(1).GetAsync("node", false, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnswerChangeContextChallenge_WrongChoiceHaltsAndAllowsCorrectRetry()
    {
        var tools = CreateTools(ContextServiceWithExactNode());
        var started = await tools.StartChangeContextChallengeAsync("node", "Which code is correct?", Choices());
        var challengeId = started.StructuredContent!.Value.GetProperty("challengeId").GetString()!;

        var wrong = tools.AnswerChangeContextChallenge(challengeId, ["B", "C"])
            .StructuredContent!.Value;

        wrong.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        wrong.GetProperty("halted").GetBoolean().Should().BeTrue();
        wrong.GetProperty("canRetry").GetBoolean().Should().BeTrue();
        wrong.GetProperty("feedback").GetRawText().Should().Contain("Choice B is wrong");
        wrong.GetProperty("feedback").GetRawText().Should().NotContain("Choice D is wrong");

        var correct = tools.AnswerChangeContextChallenge(challengeId, ["A", "C"])
            .StructuredContent!.Value;

        correct.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        correct.GetProperty("halted").GetBoolean().Should().BeFalse();
        correct.GetProperty("canRetry").GetBoolean().Should().BeFalse();
        correct.GetProperty("attempt").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task RecordChangeContextChallengeNoteAsync_AfterCorrectAnswerStoresUserStatementWithoutEcho()
    {
        const string statement = "Keep the validation at the application boundary.";
        var contextService = ContextServiceWithExactNode();
        contextService.RecordAsync(
                "node", statement, "constraint", "user-stated", false, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChangeContextReceipt(
                "1.0", "human-cognitive-seed:note", "node", "constraint", "user-stated", false,
                "recorded-unverified", "hash"));
        var tools = CreateTools(contextService);
        var started = await tools.StartChangeContextChallengeAsync("node", "Which code is correct?", Choices());
        var challengeId = started.StructuredContent!.Value.GetProperty("challengeId").GetString()!;
        tools.AnswerChangeContextChallenge(challengeId, ["A", "C"]);

        var result = await tools.RecordChangeContextChallengeNoteAsync(challengeId, statement, "constraint");

        result.StructuredContent!.Value.GetProperty("contextId").GetString()
            .Should().Be("human-cognitive-seed:note");
        result.StructuredContent.Value.GetRawText().Should().NotContain(statement);
        result.Content.OfType<TextContentBlock>().Single().Text.Should().NotContain(statement);
        await contextService.Received(1).RecordAsync(
            "node", statement, "constraint", "user-stated", false,
            $"challenge-note:{challengeId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordChangeContextChallengeNoteAsync_BeforeCorrectAnswerIsRejected()
    {
        var contextService = ContextServiceWithExactNode();
        var tools = CreateTools(contextService);
        var started = await tools.StartChangeContextChallengeAsync("node", "Which code is correct?", Choices());
        var challengeId = started.StructuredContent!.Value.GetProperty("challengeId").GetString()!;

        var action = () => tools.RecordChangeContextChallengeNoteAsync(
            challengeId, "A durable note.", "decision");

        await action.Should().ThrowAsync<InvalidOperationException>();
        await contextService.DidNotReceiveWithAnyArgs().RecordAsync(
            default!, default!, default!, default!, default, default, default);
    }

    private static HumanCognitiveSeedChallengeTools CreateTools(
        IHumanCognitiveSeedContextService contextService) =>
        new(new HumanCognitiveSeedChallengeStore(TimeProvider.System), contextService);

    private static IHumanCognitiveSeedContextService ContextServiceWithExactNode()
    {
        var contextService = Substitute.For<IHumanCognitiveSeedContextService>();
        contextService.GetAsync("node", false, 1, Arg.Any<CancellationToken>())
            .Returns(new ChangeContextListResult(
                "1.0", "node", true, [], false,
                "Context statements are attributed, unverified memory."));
        return contextService;
    }

    private static List<ChangeContextChallengeChoiceInput> Choices() =>
    [
        new("A", "return Validate(input);", true, "Choice A preserves validation."),
        new("B", "return input;", false, "Choice B is wrong because it bypasses validation."),
        new("C", "return validator.Check(input);", true, "Choice C preserves the boundary."),
        new("D", "return null;", false, "Choice D is wrong because it hides failure.")
    ];
}
