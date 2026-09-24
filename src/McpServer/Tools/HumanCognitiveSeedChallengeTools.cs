using System.ComponentModel;
using System.Text;
using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Apps;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

[McpServerToolType]
public sealed class HumanCognitiveSeedChallengeTools(
    HumanCognitiveSeedChallengeStore challengeStore,
    IHumanCognitiveSeedContextService contextService)
{
    [McpServerTool(Name = "start_change_context_challenge", Title = "Start Change Context Code Challenge", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChangeContextChallengeView))]
#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
    [McpAppUi(
        ResourceUri = HumanCognitiveSeedChallengeAppResources.ResourceUri,
        Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
#pragma warning restore MCPEXP003
    [Description(
        "Present a human-cognitive-seed code reasoning challenge for one exact code node. " +
        "Use only when the user explicitly wants interactive code reasoning; do not turn ordinary work into a quiz. " +
        "Before calling, resolve the exact target and inspect its current source, relevant tests, and get_change_context. Supply concise source and test evidence that directly supports the alternatives. Author three or four plausible code choices with one or two correct choices and at least two wrong choices. " +
        "If the target exists in source but is not indexed, stop and run the normal repository indexer, then resolve the canonical symbol and verify its relationships. Never create a temporary node or manually ingest guessed code relationships for a challenge. " +
        "Give every choice concise teaching feedback and do not reveal the correct choices. Only relay choice IDs to the answer tool after the user explicitly selects them; never choose for the user.")]
    public async Task<CallToolResult> StartChangeContextChallengeAsync(
        [Description("Exact canonical code node ID that owns the change and any optional note.")]
        string nodeId,
        [Description("A focused question asking the user which code answer correctly respects the current source, tests, and change context.")]
        string question,
        [Description("One to six concise facts from the exact target's current source that support the alternatives.")]
        List<string> sourceEvidence,
        [Description("One to six concise facts from relevant tests, or an explicit test-gap fact, that support the alternatives.")]
        List<string> testEvidence,
        [Description("Three or four LLM-authored code choices. Exactly one or two must be correct and at least two must be wrong.")]
        List<ChangeContextChallengeChoiceInput> choices,
        CancellationToken cancellationToken = default)
    {
        var context = await contextService.GetAsync(nodeId, false, 1, cancellationToken);
        if (!context.TargetFound)
            throw new ArgumentException(
                "The challenge nodeId must resolve to an exact indexed code node. " +
                "If the code exists in the workspace, run the normal CodeMeridian indexer, resolve the canonical symbol, and verify its source and test relationships before retrying. " +
                "Do not create a temporary node or manually ingest guessed relationships for a challenge.",
                nameof(nodeId));

        var result = challengeStore.Start(nodeId, question, sourceEvidence, testEvidence, choices);
        return StructuredToolResult.Create(ToStartMarkdown(result), result);
    }

    [McpServerTool(Name = "answer_change_context_challenge", Title = "Answer Change Context Code Challenge", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChangeContextChallengeAnswerResult))]
#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
    [McpAppUi(
        ResourceUri = HumanCognitiveSeedChallengeAppResources.ResourceUri,
        Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
#pragma warning restore MCPEXP003
    [Description("Validate choice IDs explicitly selected by the user in the change-context challenge app or Markdown fallback. Never select answers for the user.")]
    public CallToolResult AnswerChangeContextChallenge(
        [Description("Opaque challenge ID returned by start_change_context_challenge.")]
        string challengeId,
        [Description("The one or two choice IDs explicitly selected by the user.")]
        string[] selectedChoiceIds)
    {
        var result = challengeStore.Answer(challengeId, selectedChoiceIds);
        return StructuredToolResult.Create(ToAnswerMarkdown(result), result);
    }

    [McpServerTool(Name = "record_change_context_challenge_note", Title = "Save Solved Challenge Note", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChangeContextChallengeNoteResult))]
#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
    [McpAppUi(
        ResourceUri = HumanCognitiveSeedChallengeAppResources.ResourceUri,
        Visibility = [McpUiToolVisibility.App])]
#pragma warning restore MCPEXP003
    [Description("Optionally record the developer's explanation after a solved challenge. A disputed AI answer is stored as user-stated context. A reported target mismatch is never written to the graph.")]
    public async Task<CallToolResult> RecordChangeContextChallengeNoteAsync(
        [Description("Opaque challenge ID returned by start_change_context_challenge.")]
        string challengeId,
        [Description("User-written durable note, at most 800 characters.")]
        string statement,
        [Description("Context kind: decision, constraint, limitation, assumption, or follow-up.")]
        string contextKind,
        [Description("Whether the developer believes the AI-authored correct answer was wrong.")]
        bool aiAnswerWasWrong = false,
        [Description("Whether the challenge targeted the wrong code node. When true, no graph note is written.")]
        bool targetNodeWasWrong = false,
        CancellationToken cancellationToken = default)
    {
        var nodeId = challengeStore.GetCompletedNodeId(challengeId);
        if (targetNodeWasWrong)
        {
            var rejected = new ChangeContextChallengeNoteResult(
                "1.1", challengeId, null, nodeId, null, null,
                "target-mismatch-not-recorded", aiAnswerWasWrong, true);
            return StructuredToolResult.Create(
                "The developer reported that the challenge targeted the wrong node. No graph note was recorded; resolve the correct node and start a new challenge.",
                rejected);
        }

        var recordedStatement = aiAnswerWasWrong
            ? $"AI-authored answer disputed by developer: {statement}"
            : statement;
        var receipt = await contextService.RecordAsync(
            nodeId,
            recordedStatement,
            contextKind,
            "user-stated",
            false,
            $"challenge-note:{challengeId}",
            cancellationToken);
        var result = new ChangeContextChallengeNoteResult(
            "1.1",
            challengeId,
            receipt.ContextId,
            receipt.NodeId,
            receipt.ContextKind,
            receipt.Provenance,
            receipt.Status,
            aiAnswerWasWrong,
            false);
        return StructuredToolResult.Create(
            $"Recorded user-stated change context `{result.ContextId}` for `{result.NodeId}` without echoing the note body.",
            result);
    }

    private static string ToStartMarkdown(ChangeContextChallengeView result)
    {
        var markdown = new StringBuilder()
            .AppendLine("# Change Context Code Challenge")
            .AppendLine()
            .AppendLine($"**Target:** `{result.NodeId}`")
            .AppendLine($"**Question:** {result.Question}")
            .AppendLine("**Source evidence:**")
            .AppendLine(string.Join(Environment.NewLine, result.Evidence.Source.Select(item => $"- {item}")))
            .AppendLine("**Test evidence:**")
            .AppendLine(string.Join(Environment.NewLine, result.Evidence.Tests.Select(item => $"- {item}")))
            .AppendLine($"**Selection:** Choose {result.RequiredSelectionCount} answer{(result.RequiredSelectionCount == 1 ? string.Empty : "s")} in the app or reply with the choice IDs.")
            .AppendLine()
            .AppendLine("> Correctness is intentionally withheld. Let the user evaluate and explicitly select the choices.")
            .AppendLine();
        foreach (var choice in result.Choices)
        {
            markdown.AppendLine($"## Choice {choice.Id}");
            foreach (var line in choice.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                markdown.AppendLine($"    {line}");
            markdown.AppendLine();
        }
        return markdown.ToString().TrimEnd();
    }

    private static string ToAnswerMarkdown(ChangeContextChallengeAnswerResult result)
    {
        var markdown = new StringBuilder()
            .AppendLine(result.IsCorrect ? "# Challenge solved" : "# Attempt halted")
            .AppendLine()
            .AppendLine($"**Attempt:** {result.Attempt}")
            .AppendLine($"**State:** `{result.State}`")
            .AppendLine();
        foreach (var feedback in result.Feedback)
            markdown.AppendLine($"- {feedback.Message}");
        return markdown.ToString().TrimEnd();
    }
}
