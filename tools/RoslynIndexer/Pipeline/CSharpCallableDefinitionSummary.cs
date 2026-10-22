namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>Syntax-backed callable accounting, excluding compiler-synthesized declarations.</summary>
public sealed record CSharpCallableDefinitionSummary(
    int Candidates,
    int Represented,
    int Unrepresented,
    int IdentityConflictDeclarations)
{
    public bool HasValidAccounting => Candidates == Represented + Unrepresented
        && IdentityConflictDeclarations <= Represented;
    public bool HasGaps => Unrepresented > 0 || IdentityConflictDeclarations > 0;
}
