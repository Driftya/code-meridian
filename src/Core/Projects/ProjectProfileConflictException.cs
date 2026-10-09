namespace CodeMeridian.Core.Projects;

public sealed class ProjectProfileConflictException(string message) : InvalidOperationException(message);
