namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianPowerShellOptions
{
    public bool Enabled { get; set; }
    // Explicit policy for hosts where uniform filesystem case semantics cannot be observed.
    public bool? CaseSensitive { get; set; }
}
