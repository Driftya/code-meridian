using FluentAssertions;

namespace CodeMeridian.McpServer.Tests;

public sealed class HumanCognitiveSeedFizzBuzzTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(3, "Fizz")]
    [InlineData(5, "Buzz")]
    [InlineData(15, "FizzBuzz")]
    [InlineData(30, "FizzBuzz")]
    public void FizzBuzz_PreservesTheHumanSeedAndTestsTheExpandedModel(int number, string expected)
    {
        // Human seed: use the familiar rule set—3 means Fizz, 5 means Buzz,
        // both means FizzBuzz, and every other number remains visible.

        // AI expansion: turn that idea into distinct examples, including a second
        // common multiple so the test checks the rule rather than memorizing 15.

        // Challenge: the combined rule must be evaluated first. Checking 3 or 5
        // first would make 15 return only Fizz or Buzz and expose a hidden assumption.
        var result = FizzBuzz(number);

        // Synthesis returned to the human: these examples are the executable model.
        // The human can now judge whether this is the intended domain (for example,
        // whether zero or negative numbers deserve a separately chosen policy).
        result.Should().Be(expected);
    }

    private static string FizzBuzz(int number)
    {
        if (number % 15 == 0)
        {
            return "FizzBuzz";
        }

        if (number % 3 == 0)
        {
            return "Fizz";
        }

        if (number % 5 == 0)
        {
            return "Buzz";
        }

        return number.ToString();
    }
}
