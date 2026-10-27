using System.Management.Automation.Language;
using static CodeMeridian.PowerShellIndexer.PowerShellAstFacts;

namespace CodeMeridian.PowerShellIndexer;

internal static class PowerShellModuleExtractor
{
    internal static IReadOnlyList<PowerShellModuleReference> References(ScriptBlockAst ast, IReadOnlyList<CommandAst> commands, IReadOnlyList<Token> tokens,
        Func<Ast, string> owner, Action<string, string, IScriptExtent?> diagnostic)
    {
        var references = new List<PowerShellModuleReference>();
        foreach (var command in commands.Where(command => command.GetCommandName()?.Equals("Import-Module", StringComparison.OrdinalIgnoreCase) == true))
        {
            var arguments = Arguments(command, "Name");
            var expression = arguments is { Count: 1 } ? PathExpression(arguments[0]) : ("dynamic", (string?)null);
            references.Add(new("import", owner(command), expression.Item2, expression.Item1, Span(command.Extent),
                arguments is { Count: 1 } && !Deferred(command) && !Conditional(command.Parent!, Owner(command))));
        }
        foreach (var item in ast.FindAll(node => node is UsingStatementAst { UsingStatementKind: UsingStatementKind.Module }, true).Cast<UsingStatementAst>())
        {
            var expression = item.Name is null ? ("dynamic", (string?)null) : PathExpression(item.Name);
            references.Add(new("using", owner(item), expression.Item2, expression.Item1, Span(item.Extent), item.ModuleSpecification is null));
        }
        if (ast.ScriptRequirements is { } requirements)
        {
            foreach (var module in requirements.RequiredModules)
            {
                var constraints = new Dictionary<string, string>();
                if (module.RequiredVersion is { } required) constraints["requiredVersion"] = required.ToString();
                if (module.Version is { } minimum) constraints["minimumVersion"] = minimum.ToString();
                if (module.MaximumVersion is { } maximum) constraints["maximumVersion"] = maximum.ToString();
                if (module.Guid is { } guid) constraints["guid"] = guid.ToString();
                var span = tokens.FirstOrDefault(token => token.Kind == TokenKind.Comment
                    && token.Text.TrimStart().StartsWith("#requires", StringComparison.OrdinalIgnoreCase)
                    && token.Text.Contains(module.Name, StringComparison.OrdinalIgnoreCase))?.Extent ?? ast.Extent;
                references.Add(new("requires", owner(ast), module.Name, "literal", Span(span), constraints.Count == 0)
                    { Constraints = constraints });
            }
            if (requirements.RequiredAssemblies.Count > 0)
                diagnostic("required_assemblies_uninspected", "Required assemblies are recorded by the parser but are never loaded or inspected.", ast.Extent);
        }
        return references;
    }

    internal static PowerShellManifest Manifest(ScriptBlockAst ast, Action<string, string, IScriptExtent?> diagnostic)
    {
        if (ast.BeginBlock is not null || ast.ProcessBlock is not null || ast.EndBlock?.Statements.Count != 1
            || !PowerShellLiteralReader.TryRead(ast.EndBlock.Statements[0], out var literal)
            || literal is not Dictionary<string, object?> metadata)
        {
            diagnostic("manifest_not_literal", "Only a single bounded literal manifest hashtable is supported; no expression was evaluated.", ast.Extent);
            return new("indeterminate", new Dictionary<string, object?>(), null, null);
        }
        var supported = true;
        string? rootModule = null;
        if (metadata.TryGetValue("RootModule", out var root))
        {
            rootModule = root as string;
            supported &= rootModule is not null;
        }
        if (metadata.TryGetValue("ModuleToProcess", out _)) supported = false;
        string[]? exports = null;
        if (metadata.TryGetValue("FunctionsToExport", out var functions))
        {
            exports = PowerShellLiteralReader.Names(functions);
            supported &= exports is not null;
        }
        foreach (var key in new[] { "NestedModules", "RequiredModules", "AliasesToExport", "CmdletsToExport", "ScriptsToProcess", "RequiredAssemblies" })
            if (metadata.TryGetValue(key, out var value) && value is not null && value is not object?[] { Length: 0 }) supported = false;
        if (!supported) diagnostic("manifest_visibility_indeterminate", "Computed/wildcard exports, module dependencies, aliases, assemblies, or script hooks prevent static module binding.", ast.Extent);
        return new(supported ? "literal" : "indeterminate", metadata, rootModule, exports);
    }

    internal static IReadOnlyList<string>? Exports(IReadOnlyList<CommandAst> commands, Action<string, string, IScriptExtent?> diagnostic)
    {
        var exported = new List<string>();
        var explicitExport = false;
        foreach (var command in commands.Where(command => command.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true))
        {
            explicitExport = true;
            var arguments = Arguments(command, "Function");
            if (arguments is null || Deferred(command) || Owner(command) is not null || Conditional(command.Parent!, null))
            {
                diagnostic("exports_indeterminate", "Conditional, scoped, or unsupported export options prevent static export visibility.", command.Extent);
                return null;
            }
            foreach (var argument in arguments)
            {
                if (!PowerShellLiteralReader.TryRead(argument, out var literal) || PowerShellLiteralReader.Names(literal) is not { } names)
                {
                    diagnostic("exports_indeterminate", "Computed or wildcard export lists are unsupported.", command.Extent);
                    return null;
                }
                exported.AddRange(names);
            }
        }
        // A null default export list is distinguished by the extractor from invalid explicit exports.
        return explicitExport ? exported.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : null;
    }

    private static IReadOnlyList<Ast>? Arguments(CommandAst command, string parameter)
    {
        var values = new List<Ast>();
        foreach (var element in command.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst option)
            {
                if (!option.ParameterName.Equals(parameter, StringComparison.OrdinalIgnoreCase)) return null;
                if (option.Argument is not null) values.Add(option.Argument);
            }
            else values.Add(element);
        }
        return values;
    }
}
