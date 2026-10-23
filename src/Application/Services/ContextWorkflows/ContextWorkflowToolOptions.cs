using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public sealed record ContextWorkflowToolOptions(
    bool IncludeTests = true,
    bool IncludeDocs = true,
    bool IncludeNamespacePlan = true,
    DocumentQueryScope? DocumentScope = null);
