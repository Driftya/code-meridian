using CodeMeridian.Core.Knowledge;
using CodeMeridian.Sdk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpRouteExtractor
{
    public static void Extract(
        CompilationUnitSyntax root,
        string relPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges)
    {
        var constants = RouteConstantResolver.BuildStringConstants(root);
        AspNetControllerRouteExtractor.Extract(root, relPath, projectContext, nodes, edges, constants);
        MinimalApiRouteExtractor.Extract(root, relPath, projectContext, nodes, edges, constants);
    }
}
