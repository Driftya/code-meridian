using System.Management.Automation.Language;
using static CodeMeridian.PowerShellIndexer.PowerShellAstFacts;

namespace CodeMeridian.PowerShellIndexer;

public static class PowerShellParser
{
    public const int MaximumCharacters = 262144;
    public const int MaximumFacts = 1000;
    public const int MaximumDiagnostics = 32;

    public static PowerShellFileAnalysis Parse(string project, string path, string text, string sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileId = PowerShellIdentity.File(project, path);
        var diagnostics = new List<PowerShellDiagnostic>();
        var diagnosticCount = 0;
        void Diagnostic(string code, string message, IScriptExtent? extent)
        {
            diagnosticCount++;
            if (diagnostics.Count < MaximumDiagnostics)
                diagnostics.Add(new(code, Clip(message, 2048), extent is null ? null : Span(extent), message.Length > 2048));
        }
        PowerShellFileAnalysis Empty(string status) => new(project, path, fileId, sourceFingerprint, status, 0, [], [], [], null, null,
            diagnostics, diagnosticCount);
        if (text.Length > MaximumCharacters || ExcessiveNesting(text))
        {
            Diagnostic("parse_budget_exceeded", "The parser input exceeds the character or conservative delimiter-nesting budget; no partial facts were retained.", null);
            return Empty("partial");
        }
        // DSC grammar can create a runspace even in ModuleAnalysis mode, including inside
        // expandable-string subexpressions. Reject conservative decoded keyword occurrences first.
        var preflight = System.Text.RegularExpressions.Regex.Replace(text, @"`u\{([0-9a-fA-F]{1,6})\}", match =>
        {
            var scalar = Convert.ToInt32(match.Groups[1].Value, 16);
            return System.Text.Rune.IsValid(scalar) ? char.ConvertFromUtf32(scalar) : match.Value;
        }).Replace("`", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        if (preflight.Contains("configuration", StringComparison.OrdinalIgnoreCase))
        {
            Diagnostic("configuration_unsupported", "The conservative DSC preflight rejects configuration keyword occurrences, including literals/comments; no native grammar was entered.", null);
            return Empty("partial");
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(preflight, @"\busing\s+assembly\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            Diagnostic("using_assembly_uninspected", "Using-assembly text is rejected before native grammar path probing; no assembly was inspected or loaded.", null);
            return Empty("partial");
        }
        ScriptBlockAst ast;
        ParseError[] errors;
        IReadOnlyList<Token> tokens;
        try { (ast, errors, tokens) = PowerShellSyntaxParser.Parse(path, text); }
        catch (UnsupportedPowerShellSyntaxException exception)
        {
            Diagnostic("configuration_unsupported", "DSC configuration syntax is rejected before native grammar processing to prevent runspace and resource loading.", exception.Extent);
            return Empty("partial");
        }
        catch (InvalidOperationException)
        {
            Diagnostic("native_parser_failed", "The pinned syntax-only parser is unavailable or failed; no fallback or partial facts were used.", null);
            return Empty("failed");
        }
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var error in errors) Diagnostic(error.ErrorId, error.Message, error.Extent);
        var functions = ast.FindAll(node => node is FunctionDefinitionAst, true).Cast<FunctionDefinitionAst>().Take(MaximumFacts + 1).ToArray();
        var commands = ast.FindAll(node => node is CommandAst, true).Cast<CommandAst>().Take(MaximumFacts + 1).ToArray();
        if (functions.Length + commands.Length > MaximumFacts || functions.Any(function => function.Name.Length > 256)
            || commands.Any(command => command.GetCommandName()?.Length > 2048))
        {
            Diagnostic("fact_budget_exceeded", "Definition and command facts exceed the per-file budget; no partial facts were retained.", ast.Extent);
            return Empty("partial") with { ParseErrorCount = errors.Length };
        }
        var bodies = new List<PowerShellBody>
        {
            new(PowerShellIdentity.Body(fileId), null, "script", "<script>", Span(ast.Extent), Span(ast.Extent), Clip(text),
                Parameters(ast.ParamBlock?.Parameters ?? []), false)
        };
        if (ast.ParamBlock?.Parameters.Count > 128 || functions.Any(function => (function.Parameters ?? function.Body.ParamBlock?.Parameters)?.Count > 128))
        {
            Diagnostic("parameter_budget_exceeded", "A body exceeds the 128-parameter budget; no partial facts were retained.", ast.Extent);
            return Empty("partial") with { ParseErrorCount = errors.Length };
        }
        var identities = new Dictionary<FunctionDefinitionAst, string>();
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string OwnerId(Ast node) => Owner(node) is { } parent && identities.TryGetValue(parent, out var id) ? id : bodies[0].Id;
        foreach (var function in functions.OrderBy(function => function.Extent.StartOffset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = Owner(function);
            var ownerId = OwnerId(function);
            var kind = function.IsWorkflow ? "unsupported_workflow" : function.IsFilter ? "filter" : "function";
            var key = ownerId + "\0" + kind + "\0" + function.Name;
            var ordinal = ordinals.TryGetValue(key, out var previous) ? previous + 1 : 1;
            ordinals[key] = ordinal;
            var id = PowerShellIdentity.Definition(ownerId, kind, function.Name, ordinal);
            identities[function] = id;
            var parameters = function.Parameters ?? function.Body.ParamBlock?.Parameters ?? [];
            bodies.Add(new(id, ownerId, kind, function.Name, Span(function.Extent), Span(function.Body.Extent), Clip(function.Extent.Text),
                Parameters(parameters), Conditional(function, owner) || function.Name.Contains(':') || Deferred(function)));
            if (function.Name.Contains(':')) Diagnostic("qualified_scope_unsupported", "Scope-qualified declarations are retained without static binding.", function.Extent);
            if (function.IsWorkflow) Diagnostic("workflow_unsupported", "Workflow declarations are retained without supported execution or binding semantics.", function.Extent);
        }
        foreach (var unsupported in ast.FindAll(node => node is TypeDefinitionAst or ConfigurationDefinitionAst or DynamicKeywordStatementAst, true))
            Diagnostic("unsupported_construct", "Classes, enums, configurations, and dynamic keyword constructs do not establish supported callable coverage.", unsupported.Extent);
        var occurrences = commands.Select((command, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expression = PathExpression(command.CommandElements[0]);
            return new PowerShellCommand($"{fileId}::command::{index + 1}", OwnerId(command), command.GetCommandName(),
                command.InvocationOperator == TokenKind.Dot ? "dot_source" : command.InvocationOperator == TokenKind.Ampersand ? "call" : "direct",
                expression.Kind, expression.Value, Span(command.Extent), Clip(command.Extent.Text, 512), Deferred(command));
        }).ToArray();
        var modules = PowerShellModuleExtractor.References(ast, commands, tokens, OwnerId, Diagnostic);
        if (modules.Count + bodies.Count + occurrences.Length > MaximumFacts + 1)
        {
            Diagnostic("fact_budget_exceeded", "Module, definition, and command facts exceed the per-file budget; no partial facts were retained.", ast.Extent);
            return Empty("partial") with { ParseErrorCount = errors.Length };
        }
        var manifest = path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) ? PowerShellModuleExtractor.Manifest(ast, Diagnostic) : null;
        var exports = PowerShellModuleExtractor.Exports(commands, Diagnostic);
        var topFunctions = bodies.Where(body => body.Kind != "script" && body.ParentId == bodies[0].Id).ToArray();
        var dynamicTable = functions.Any(function => function.Name.Contains(':') || function.IsWorkflow
                || function.Name.Equals("Import-Module", StringComparison.OrdinalIgnoreCase)
                || function.Name.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase))
            || commands.Any(command => MutatesCommandTable(command.GetCommandName())
                || command.GetCommandName() is null && PathExpression(command.CommandElements[0]).Kind != "script_root")
            || ast.FindAll(node => node is VariableExpressionAst variable
                && (variable.VariablePath.UserPath.StartsWith("function:", StringComparison.OrdinalIgnoreCase)
                    || variable.VariablePath.UserPath.StartsWith("alias:", StringComparison.OrdinalIgnoreCase)), true).Any()
            || ast.FindAll(node => node is VariableExpressionAst variable && variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase)
                && variable.Parent is AssignmentStatementAst or ParameterAst or ForEachStatementAst or UnaryExpressionAst, true).Any();
        if (dynamicTable) Diagnostic("dynamic_command_table", "Alias/function-table mutation or evaluated commands prevent static name binding in this file.", null);
        if (!commands.Any(command => command.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true))
            exports = topFunctions.Select(body => body.Name).ToArray();
        if (errors.Length > 0 || dynamicTable || topFunctions.Any(body => body.Conditional)
            || topFunctions.GroupBy(body => body.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)
            || modules.Count > 0 || manifest?.Status == "indeterminate") exports = null;
        var definitionsOnly = ast.EndBlock is { } end && end.Statements.All(statement => statement is FunctionDefinitionAst)
            && ast.BeginBlock is null && ast.ProcessBlock is null && ast.CleanBlock is null && ast.ParamBlock is null && modules.Count == 0;
        var staticModuleBody = ast.EndBlock is { } moduleEnd && moduleEnd.Statements.All(statement => statement is FunctionDefinitionAst
            || statement is PipelineAst pipeline && pipeline.PipelineElements.Count == 1
                && pipeline.PipelineElements[0] is CommandAst export && export.Redirections.Count == 0
                && export.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true)
            && ast.BeginBlock is null && ast.ProcessBlock is null && ast.CleanBlock is null && ast.ParamBlock is null && modules.Count == 0;
        return new(project, path, fileId, sourceFingerprint, diagnosticCount > 0 ? "partial" : "complete", errors.Length,
            bodies, occurrences, modules, manifest, exports, diagnostics, diagnosticCount)
            { DefinitionsOnly = definitionsOnly, StaticModuleBody = staticModuleBody, HasDynamicCommandTable = dynamicTable };
    }

    private static IReadOnlyList<PowerShellParameter> Parameters(IEnumerable<ParameterAst> parameters) => parameters.Select(parameter =>
        new PowerShellParameter(parameter.Name.VariablePath.UserPath,
            parameter.Attributes.OfType<TypeConstraintAst>().Select(attribute => Clip(attribute.TypeName.FullName, 256)).ToArray(),
            parameter.DefaultValue is not null, Span(parameter.Extent))).ToArray();

    private static bool MutatesCommandTable(string? name) => name?.ToLowerInvariant() is "set-alias" or "new-alias" or "sal" or "nal"
        or "set-item" or "new-item" or "remove-item" or "si" or "ni" or "ri" or "invoke-expression" or "iex"
        or "remove-module" or "rmo" or "ipmo" or "import-alias" or "import-pssession" or "invoke-command" or "icm"
        or "set-variable" or "sv" or "new-variable" or "nv" or "remove-variable" or "rv" or "clear-variable" or "clv";

    private static bool ExcessiveNesting(string text)
    {
        var depth = 0;
        foreach (var character in text)
        {
            if (character is '(' or '{' or '[' && ++depth > 128) return true;
            if (character is ')' or '}' or ']') depth = Math.Max(0, depth - 1);
        }
        return false;
    }
}
