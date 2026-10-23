using System.Text.Json.Serialization;

namespace CodeMeridian.Core.RepositoryConstraints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepositoryConstraintException(string Path, string Reason);
