using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.PowerShellIndexer;

public static class PowerShellGraphMapper
{
    public static (IReadOnlyList<CodeNode> Nodes, IReadOnlyList<CodeEdge> Edges) Map(PowerShellAnalysisReport report)
    {
        var nodes = new List<CodeNode>();
        var edges = new List<CodeEdge>();
        foreach (var file in report.Files.Where(file => file.Status != "failed"))
        {
            nodes.Add(new()
            {
                Id = file.FileId, Name = Path.GetFileName(file.Path), Type = CodeNodeType.File, FilePath = file.Path,
                ProjectContext = report.Project, SourceHash = file.SourceFingerprint,
                Properties = new() { ["language"] = "powershell", ["powershellKind"] = "file", ["analysisStatus"] = file.Status, ["analyzer"] = "powershell" }
            });
            foreach (var body in file.Bodies.Where(body => body.Kind is "script" or "function" or "filter"))
            {
                nodes.Add(new()
                {
                    Id = body.Id, Name = body.Name, Type = CodeNodeType.Method, FilePath = file.Path, LineNumber = body.Span.Line,
                    LineCount = body.Span.EndLine - body.Span.Line + 1, SourceSnippet = body.Snippet,
                    SourceHash = file.SourceFingerprint, ProjectContext = report.Project,
                    Properties = new()
                    {
                        ["language"] = "powershell", ["powershellKind"] = body.Kind, ["analyzer"] = "powershell",
                        ["lexicalParentId"] = body.ParentId ?? file.FileId, ["conditional"] = body.Conditional.ToString().ToLowerInvariant()
                    }
                });
                edges.Add(new() { SourceId = body.ParentId ?? file.FileId, TargetId = body.Id, Type = CodeEdgeType.Contains,
                    SourceFilePath = file.Path,
                    Properties = new() { ["analyzer"] = "powershell" } });
            }
            foreach (var command in file.Commands.Where(command => command.BindingStatus == "resolved" && command.TargetId is not null))
                edges.Add(new()
                {
                    SourceId = command.OwnerId, TargetId = command.TargetId!, Type = command.Invocation == "dot_source" ? CodeEdgeType.DependsOn : CodeEdgeType.Calls,
                    Confidence = 0.8, EvidenceKind = EdgeEvidenceKind.Inferred, EvidenceReason = command.Reason, Resolver = "powershell_static_v1",
                    SourceFilePath = file.Path, SourceLine = command.Span.Line, SourceColumn = command.Span.Column,
                    SourceEndLine = command.Span.EndLine, SourceEndColumn = command.Span.EndColumn,
                    Properties = new() { ["analyzer"] = "powershell", ["powershellInvocation"] = command.Invocation }
                });
            foreach (var module in file.Modules.Where(module => module.BindingStatus == "resolved" && module.TargetId is not null))
                edges.Add(new()
                {
                    SourceId = module.OwnerId, TargetId = module.TargetId!, Type = CodeEdgeType.DependsOn,
                    Confidence = 0.8, EvidenceKind = EdgeEvidenceKind.Inferred, EvidenceReason = module.Reason, Resolver = "powershell_static_v1",
                    SourceFilePath = file.Path, SourceLine = module.Span.Line, SourceColumn = module.Span.Column,
                    SourceEndLine = module.Span.EndLine, SourceEndColumn = module.Span.EndColumn,
                    Properties = new() { ["analyzer"] = "powershell", ["powershellInvocation"] = module.Kind }
                });
        }
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        // A mapper never manufactures a target from an unresolved or absent selected source.
        return (nodes, edges.Where(edge => ids.Contains(edge.SourceId) && ids.Contains(edge.TargetId)).ToArray());
    }

    public static IReadOnlyList<PowerShellFileGraph> MapFiles(PowerShellAnalysisReport report)
    {
        var (nodes, edges) = Map(report);
        return report.Files.Select(file => new PowerShellFileGraph(file.Path, file.Status, file.SourceFingerprint,
            nodes.Where(node => node.FilePath == file.Path).ToArray(), edges.Where(edge => edge.SourceFilePath == file.Path).ToArray())).ToArray();
    }
}
