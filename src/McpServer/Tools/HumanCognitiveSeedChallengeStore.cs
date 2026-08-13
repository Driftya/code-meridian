using System.Collections.Concurrent;

namespace CodeMeridian.McpServer.Tools;

public sealed class HumanCognitiveSeedChallengeStore(TimeProvider timeProvider)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, ChallengeState> _challenges = new(StringComparer.Ordinal);

    public ChangeContextChallengeView Start(
        string nodeId,
        string question,
        IReadOnlyCollection<ChangeContextChallengeChoiceInput> choices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(choices);

        if (question.Length > 2_000)
            throw new ArgumentException("Question must be at most 2,000 characters.", nameof(question));
        if (choices.Count is < 3 or > 4)
            throw new ArgumentException("A challenge must contain three or four choices.", nameof(choices));

        var normalized = choices.Select(NormalizeChoice).ToArray();
        if (normalized.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Choice IDs must be unique.", nameof(choices));

        var correctCount = normalized.Count(choice => choice.IsCorrect);
        var incorrectCount = normalized.Length - correctCount;
        if (correctCount is < 1 or > 2)
            throw new ArgumentException("A challenge must contain one or two correct choices.", nameof(choices));
        if (incorrectCount < 2)
            throw new ArgumentException("A challenge must contain at least two incorrect choices.", nameof(choices));

        RemoveExpiredChallenges();
        var now = timeProvider.GetUtcNow();
        var state = new ChallengeState(
            $"change-context-challenge:{Guid.NewGuid():N}",
            nodeId.Trim(),
            question.Trim(),
            normalized,
            now.Add(ChallengeLifetime));
        _challenges[state.ChallengeId] = state;
        return ToView(state);
    }

    public ChangeContextChallengeAnswerResult Answer(
        string challengeId,
        IReadOnlyCollection<string> selectedChoiceIds)
    {
        var state = GetActiveChallenge(challengeId);
        ArgumentNullException.ThrowIfNull(selectedChoiceIds);

        lock (state.SyncRoot)
        {
            var selected = selectedChoiceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var knownIds = state.Choices.Select(choice => choice.Id).ToHashSet(StringComparer.Ordinal);
            if (selected.Any(id => !knownIds.Contains(id)))
                throw new ArgumentException("The answer contains an unknown choice ID.", nameof(selectedChoiceIds));

            state.Attempt++;
            var correctIds = state.Choices
                .Where(choice => choice.IsCorrect)
                .Select(choice => choice.Id)
                .ToHashSet(StringComparer.Ordinal);
            var isCorrect = selected.Length == correctIds.Count && correctIds.SetEquals(selected);
            var feedback = new List<ChangeContextChallengeFeedback>();

            if (selected.Length != correctIds.Count)
            {
                feedback.Add(new ChangeContextChallengeFeedback(
                    null,
                    $"Select exactly {correctIds.Count} answer{(correctIds.Count == 1 ? string.Empty : "s")} before retrying."));
            }

            foreach (var choice in state.Choices.Where(choice => selected.Contains(choice.Id, StringComparer.Ordinal) && !choice.IsCorrect))
                feedback.Add(new ChangeContextChallengeFeedback(choice.Id, choice.Feedback));

            if (isCorrect)
            {
                state.Completed = true;
                feedback.AddRange(state.Choices
                    .Where(choice => choice.IsCorrect)
                    .Select(choice => new ChangeContextChallengeFeedback(choice.Id, choice.Feedback)));
            }
            else if (feedback.Count == 0)
            {
                feedback.Add(new ChangeContextChallengeFeedback(
                    null,
                    "That combination is incomplete. Re-check the change context and try again."));
            }

            return new ChangeContextChallengeAnswerResult(
                "1.0",
                state.ChallengeId,
                isCorrect,
                !isCorrect,
                !isCorrect,
                state.Attempt,
                isCorrect ? "completed" : "halted-for-retry",
                selected,
                feedback);
        }
    }

    public string GetCompletedNodeId(string challengeId)
    {
        var state = GetActiveChallenge(challengeId);
        lock (state.SyncRoot)
        {
            if (!state.Completed)
                throw new InvalidOperationException("Solve the challenge before recording a change-context note.");

            return state.NodeId;
        }
    }

    private ChallengeState GetActiveChallenge(string challengeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        if (!_challenges.TryGetValue(challengeId.Trim(), out var state))
            throw new System.Collections.Generic.KeyNotFoundException("The challenge was not found or has expired.");
        if (state.ExpiresAt > timeProvider.GetUtcNow())
            return state;

        _challenges.TryRemove(state.ChallengeId, out _);
        throw new System.Collections.Generic.KeyNotFoundException("The challenge was not found or has expired.");
    }

    private void RemoveExpiredChallenges()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var challenge in _challenges.Where(pair => pair.Value.ExpiresAt <= now))
            _challenges.TryRemove(challenge.Key, out _);
    }

    private static ChallengeChoice NormalizeChoice(ChangeContextChallengeChoiceInput choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.Feedback);
        if (choice.Id.Length > 40)
            throw new ArgumentException("Choice IDs must be at most 40 characters.", nameof(choice));
        if (choice.Code.Length > 6_000)
            throw new ArgumentException("Choice code must be at most 6,000 characters.", nameof(choice));
        if (choice.Feedback.Length > 1_500)
            throw new ArgumentException("Choice feedback must be at most 1,500 characters.", nameof(choice));

        return new ChallengeChoice(
            choice.Id.Trim(),
            choice.Code.Trim(),
            choice.IsCorrect,
            choice.Feedback.Trim());
    }

    private static ChangeContextChallengeView ToView(ChallengeState state) =>
        new(
            "1.0",
            state.ChallengeId,
            state.NodeId,
            state.Question,
            state.Choices.Count(choice => choice.IsCorrect),
            state.Choices.Select(choice => new ChangeContextChallengeChoiceView(choice.Id, choice.Code)).ToArray(),
            state.Attempt,
            "awaiting-answer",
            state.ExpiresAt,
            "The choices are an LLM-authored learning scaffold grounded in current code and change context; verify them against source and tests.");

    private sealed record ChallengeChoice(string Id, string Code, bool IsCorrect, string Feedback);

    private sealed class ChallengeState(
        string challengeId,
        string nodeId,
        string question,
        IReadOnlyList<ChallengeChoice> choices,
        DateTimeOffset expiresAt)
    {
        public string ChallengeId { get; } = challengeId;
        public string NodeId { get; } = nodeId;
        public string Question { get; } = question;
        public IReadOnlyList<ChallengeChoice> Choices { get; } = choices;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public object SyncRoot { get; } = new();
        public int Attempt { get; set; }
        public bool Completed { get; set; }
    }
}
