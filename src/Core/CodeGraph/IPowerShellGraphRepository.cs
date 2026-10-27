namespace CodeMeridian.Core.CodeGraph;

public interface IPowerShellGraphRepository
{
    Task PublishPowerShellGraphAsync(PowerShellGraphSnapshot snapshot, CancellationToken cancellationToken = default);
}
