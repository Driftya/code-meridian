using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Domain.Governance;

namespace CodeMeridian.Evolution.Application.Tests;

public sealed class GovernanceKernelTests
{
    [Fact]
    public void PausedKernelDeniesEveryAutonomyLevel()
    {
        Assert.False(GovernanceKernel.Allows(
            AutonomyLevel.Experiment,
            AutonomyLevel.Observe,
            isPaused: true));
    }

    [Theory]
    [InlineData(AutonomyLevel.Recommend, AutonomyLevel.Observe, true)]
    [InlineData(AutonomyLevel.Recommend, AutonomyLevel.Recommend, true)]
    [InlineData(AutonomyLevel.Recommend, AutonomyLevel.Prepare, false)]
    public void ActiveKernelEnforcesConfiguredLevel(
        AutonomyLevel configured,
        AutonomyLevel required,
        bool expected)
    {
        Assert.Equal(expected, GovernanceKernel.Allows(configured, required, isPaused: false));
    }
}
