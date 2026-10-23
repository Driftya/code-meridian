namespace CodeMeridian.Sdk;

public sealed record RepositoryConstraintFindingResponse(string Path, string Status, string Reason, int? Line, int? Column);
