using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Application.Services;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpDatabaseTracingExtractor
{
    private static readonly Regex SqlTableRegex = new(
        @"\b(?:FROM|JOIN|UPDATE|INTO|MERGE\s+INTO|DELETE\s+FROM|TRUNCATE\s+TABLE)\s+([#\[\]""`A-Za-z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqlStatementRegex = new(
        @"^\s*(SELECT|WITH|INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CypherLabelRegex = new(
        @"\([^\)]*:(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CypherRelationshipTypeRegex = new(
        @"\[[^\]]*:(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CypherReadStatementRegex = new(
        @"^\s*(MATCH|OPTIONAL\s+MATCH|CALL|RETURN|UNWIND|WITH|LOAD\s+CSV|SHOW|PROFILE|EXPLAIN)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CypherWriteStatementRegex = new(
        @"^\s*(CREATE|MERGE|DELETE|DETACH\s+DELETE|SET|REMOVE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> QueryOperatorMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Where",
        "Select",
        "SelectMany",
        "Include",
        "ThenInclude",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "AsNoTracking",
        "AsTracking",
        "AsQueryable",
        "TagWith"
    };

    public static void Extract(
        CompilationUnitSyntax root,
        string relativePath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        CSharpConfigurationConstantRegistry constants,
        DatabaseTracingOptions options)
    {
        if (!options.Enabled || options.Presets.Count == 0)
            return;

        var presets = options.Presets
            .Where(preset => preset.Enabled
                             && !string.IsNullOrWhiteSpace(preset.Id)
                             && !string.IsNullOrWhiteSpace(preset.Provider)
                             && SupportsLanguage(preset, "CSharp"))
            .ToArray();
        if (presets.Length == 0)
            return;

        var commandTextRegistry = BuildCommandTextRegistry(root, presets, constants);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            DatabaseUsageRecognition? recognition = null;
            foreach (var preset in presets)
            {
                recognition = TryRecognize(invocation, preset, constants, commandTextRegistry, options.MaxTablesPerOperation);
                if (recognition is not null)
                    break;
            }

            if (recognition is null)
                continue;

            EmitUsage(nodes, edges, invocation, relativePath, projectContext, recognition.Value);
        }
    }

    private static DatabaseUsageRecognition? TryRecognize(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        CSharpConfigurationConstantRegistry constants,
        Dictionary<string, string> commandTextRegistry,
        int maxTablesPerOperation)
    {
        var memberName = GetMemberName(invocation.Expression);
        if (memberName is null)
            return null;

        return preset.Strategy.Trim() switch
        {
            "EfCore" => GetOperationType(memberName, preset) is { } efCoreOperation
                ? TryRecognizeEfCore(invocation, preset, memberName, efCoreOperation, constants, maxTablesPerOperation)
                : null,
            "Dapper" => GetOperationType(memberName, preset) is { } sqlOperation
                ? TryRecognizeSqlInvocation(invocation, preset, memberName, sqlOperation, constants, maxTablesPerOperation)
                : null,
            "RawSql" => GetOperationType(memberName, preset) is { } rawSqlOperation
                ? TryRecognizeRawSqlInvocation(invocation, preset, memberName, rawSqlOperation, constants, commandTextRegistry, maxTablesPerOperation)
                : null,
            "Cypher" => TryRecognizeCypherInvocation(invocation, preset, memberName, constants, maxTablesPerOperation),
            _ => null
        };
    }

    private static DatabaseUsageRecognition? TryRecognizeEfCore(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        string memberName,
        DatabaseOperationType operationType,
        CSharpConfigurationConstantRegistry constants,
        int maxTablesPerOperation)
    {
        var receiver = GetReceiverExpression(invocation.Expression);
        var tables = new List<string>();

        if (HasTableSource(preset, "SqlText")
            && ResolveSqlText(invocation, preset, constants) is { } sqlText
            && LooksLikeSql(sqlText))
        {
            tables.AddRange(ExtractTablesFromSql(sqlText, maxTablesPerOperation));
        }

        if (tables.Count == 0 && HasTableSource(preset, "GenericTypeArgument"))
        {
            var genericTable = FindGenericTableName(receiver);
            if (!string.IsNullOrWhiteSpace(genericTable))
                tables.Add(genericTable);
        }

        if (tables.Count == 0 && HasTableSource(preset, "ReceiverMemberName"))
        {
            var memberTable = FindReceiverTableName(receiver);
            if (!string.IsNullOrWhiteSpace(memberTable))
                tables.Add(memberTable);
        }

        tables = tables
            .Select(NormalizeTableName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxTablesPerOperation))
            .ToList();

        if (tables.Count == 0)
            return null;

        return new DatabaseUsageRecognition(preset.Id, preset.Provider, memberName, operationType, tables);
    }

    private static DatabaseUsageRecognition? TryRecognizeSqlInvocation(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        string memberName,
        DatabaseOperationType operationType,
        CSharpConfigurationConstantRegistry constants,
        int maxTablesPerOperation)
    {
        var sqlText = ResolveSqlText(invocation, preset, constants);
        if (!LooksLikeSql(sqlText))
            return null;

        var tables = ExtractTablesFromSql(sqlText!, maxTablesPerOperation);
        if (tables.Count == 0)
            return null;

        return new DatabaseUsageRecognition(preset.Id, preset.Provider, memberName, operationType, tables);
    }

    private static DatabaseUsageRecognition? TryRecognizeRawSqlInvocation(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        string memberName,
        DatabaseOperationType operationType,
        CSharpConfigurationConstantRegistry constants,
        Dictionary<string, string> commandTextRegistry,
        int maxTablesPerOperation)
    {
        var sqlText = ResolveSqlText(invocation, preset, constants);
        if (!LooksLikeSql(sqlText))
        {
            var receiver = GetReceiverExpression(invocation.Expression);
            if (receiver is null)
                return null;

            commandTextRegistry.TryGetValue(NormalizeReceiverKey(receiver), out sqlText);
        }

        if (!LooksLikeSql(sqlText))
            return null;

        var tables = ExtractTablesFromSql(sqlText!, maxTablesPerOperation);
        if (tables.Count == 0)
            return null;

        return new DatabaseUsageRecognition(preset.Id, preset.Provider, memberName, operationType, tables);
    }

    private static DatabaseUsageRecognition? TryRecognizeCypherInvocation(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        string memberName,
        CSharpConfigurationConstantRegistry constants,
        int maxTablesPerOperation)
    {
        if (!SupportsCypherMethod(memberName, preset))
            return null;

        var statementText = ResolveSqlText(invocation, preset, constants);
        if (!LooksLikeCypher(statementText))
            return null;

        var operationType = InferCypherOperationType(statementText!);
        if (operationType is null)
            return null;

        var tables = ExtractTablesFromCypher(statementText!, maxTablesPerOperation);
        if (tables.Count == 0)
            return null;

        return new DatabaseUsageRecognition(preset.Id, preset.Provider, memberName, operationType.Value, tables);
    }

    private static void EmitUsage(
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        InvocationExpressionSyntax invocation,
        string relativePath,
        string projectContext,
        DatabaseUsageRecognition recognition)
    {
        var sourceId = CSharpIndexerSyntaxUtilities.ResolveSourceId(invocation, relativePath, projectContext);
        var lineNumber = CSharpIndexerSyntaxUtilities.GetLineNumber(invocation);
        var operationNodeId = BuildOperationNodeId(projectContext, sourceId, relativePath, lineNumber, recognition);
        var relationshipType = recognition.OperationType == DatabaseOperationType.Read ? "Reads" : "Writes";

        nodes.Add(new IngestNodeRequest(
            operationNodeId,
            $"{recognition.Provider} {relationshipType} {recognition.Tables[0]}",
            "ExternalConcept",
            null,
            relativePath,
            lineNumber,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = recognition.Provider,
                ["operationType"] = relationshipType,
                ["recognizerId"] = recognition.RecognizerId,
                ["methodName"] = recognition.MethodName
            }));

        edges.Add(new IngestEdgeRequest(
            sourceId,
            operationNodeId,
            relationshipType,
            CallSite: $"{relativePath}:{lineNumber}",
            Confidence: 0.88d,
            Properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = recognition.Provider,
                ["recognizerId"] = recognition.RecognizerId
            }));

        foreach (var tableName in recognition.Tables)
        {
            var tableId = $"{projectContext}::DatabaseTable::{tableName}";
            nodes.Add(new IngestNodeRequest(
                tableId,
                tableName,
                "DatabaseTable",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["externalKind"] = "DatabaseTable",
                    ["normalizedName"] = tableName.ToLowerInvariant()
                }));

            edges.Add(new IngestEdgeRequest(
                operationNodeId,
                tableId,
                relationshipType,
                CallSite: $"{relativePath}:{lineNumber}",
                Confidence: 0.9d,
                Properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["provider"] = recognition.Provider,
                    ["recognizerId"] = recognition.RecognizerId,
                    ["tableName"] = tableName
                }));
        }
    }

    private static Dictionary<string, string> BuildCommandTextRegistry(
        CompilationUnitSyntax root,
        IReadOnlyCollection<DatabaseTracingPresetOptions> presets,
        CSharpConfigurationConstantRegistry constants)
    {
        var registry = new Dictionary<string, string>(StringComparer.Ordinal);
        var commandPropertyNames = presets
            .SelectMany(preset => preset.GetEffectiveStatementTextProperties())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commandTypeHints = presets
            .SelectMany(preset => preset.CommandCreationTypeHints)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax memberAccess
                || !commandPropertyNames.Contains(memberAccess.Name.Identifier.ValueText))
                continue;

            var sqlText = CSharpIndexerSyntaxUtilities.ResolveStringExpression(assignment.Right, constants);
            if (!LooksLikeStatement(sqlText))
                continue;

            registry[NormalizeReceiverKey(memberAccess.Expression)] = sqlText!;
        }

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is not ObjectCreationExpressionSyntax objectCreation
                || !LooksLikeCommandType(objectCreation.Type, commandTypeHints)
                || objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not { } firstArgument)
                continue;

            var sqlText = CSharpIndexerSyntaxUtilities.ResolveStringExpression(firstArgument, constants);
            if (!LooksLikeStatement(sqlText))
                continue;

            registry[declarator.Identifier.ValueText] = sqlText!;
        }

        return registry;
    }

    private static bool LooksLikeCommandType(TypeSyntax typeSyntax, IReadOnlyCollection<string> commandTypeHints)
    {
        if (commandTypeHints.Count == 0)
            return false;

        var typeText = typeSyntax.ToString();
        return commandTypeHints.Any(typeText.Contains);
    }

    private static DatabaseOperationType? GetOperationType(string methodName, DatabaseTracingPresetOptions preset)
    {
        if (preset.ReadMethods.Any(candidate => string.Equals(candidate, methodName, StringComparison.OrdinalIgnoreCase)))
            return DatabaseOperationType.Read;

        if (preset.WriteMethods.Any(candidate => string.Equals(candidate, methodName, StringComparison.OrdinalIgnoreCase)))
            return DatabaseOperationType.Write;

        return null;
    }

    private static bool HasTableSource(DatabaseTracingPresetOptions preset, string source) =>
        preset.TableSources.Any(candidate => string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveSqlText(
        InvocationExpressionSyntax invocation,
        DatabaseTracingPresetOptions preset,
        CSharpConfigurationConstantRegistry constants)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        foreach (var argumentIndex in preset.GetEffectiveStatementArgumentIndexes().DefaultIfEmpty(0))
        {
            if (argumentIndex < 0 || argumentIndex >= invocation.ArgumentList.Arguments.Count)
                continue;

            var sqlText = CSharpIndexerSyntaxUtilities.ResolveStringExpression(
                invocation.ArgumentList.Arguments[argumentIndex].Expression,
                constants);
            if (LooksLikeStatement(sqlText))
                return sqlText;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var sqlText = CSharpIndexerSyntaxUtilities.ResolveStringExpression(argument.Expression, constants);
            if (LooksLikeStatement(sqlText))
                return sqlText;
        }

        return null;
    }

    private static bool LooksLikeSql(string? sqlText) =>
        !string.IsNullOrWhiteSpace(sqlText) && SqlStatementRegex.IsMatch(sqlText);

    private static bool LooksLikeStatement(string? statementText) =>
        LooksLikeSql(statementText) || LooksLikeCypher(statementText);

    private static bool LooksLikeCypher(string? statementText) =>
        !string.IsNullOrWhiteSpace(statementText)
        && (CypherReadStatementRegex.IsMatch(statementText) || CypherWriteStatementRegex.IsMatch(statementText));

    private static List<string> ExtractTablesFromSql(string sqlText, int maxTablesPerOperation) =>
        SqlTableRegex.Matches(sqlText)
            .Select(match => NormalizeTableName(match.Groups[1].Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxTablesPerOperation))
            .ToList();

    private static List<string> ExtractTablesFromCypher(string statementText, int maxTablesPerOperation) =>
        CypherLabelRegex.Matches(statementText)
            .Cast<Match>()
            .Concat(CypherRelationshipTypeRegex.Matches(statementText).Cast<Match>())
            .Select(match => NormalizeTableName(match.Groups["name"].Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxTablesPerOperation))
            .ToList();

    private static string NormalizeTableName(string tableName)
    {
        var normalized = tableName.Trim().TrimEnd(';');
        normalized = normalized.Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Trim('.');
        return normalized;
    }

    private static string? FindGenericTableName(ExpressionSyntax? expression)
    {
        if (expression is null)
            return null;

        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName })
                continue;

            if (genericName.Identifier.ValueText is not ("Set" or "FromSql" or "FromSqlRaw" or "FromSqlInterpolated"))
                continue;

            var typeArgument = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArgument is not null)
                return NormalizeTypeName(typeArgument.ToString());
        }

        return null;
    }

    private static string? FindReceiverTableName(ExpressionSyntax? expression)
    {
        if (expression is null)
            return null;

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess when IsLikelyRoot(memberAccess.Expression) => NormalizeTypeName(memberAccess.Name.Identifier.ValueText),
            MemberAccessExpressionSyntax memberAccess => FindReceiverTableName(memberAccess.Expression),
            InvocationExpressionSyntax invocation when QueryOperatorMethods.Contains(GetMemberName(invocation.Expression) ?? string.Empty)
                => FindReceiverTableName(GetReceiverExpression(invocation.Expression)),
            InvocationExpressionSyntax invocation => FindReceiverTableName(GetReceiverExpression(invocation.Expression)),
            _ => null
        };
    }

    private static bool IsLikelyRoot(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax
        || expression is ThisExpressionSyntax
        || expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax;

    private static string NormalizeTypeName(string value)
    {
        var name = value.Trim();
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name[(lastDot + 1)..];

        var genericIndex = name.IndexOf('<');
        if (genericIndex >= 0)
            name = name[..genericIndex];

        return NormalizeTableName(name);
    }

    private static string BuildOperationNodeId(
        string projectContext,
        string sourceId,
        string relativePath,
        int lineNumber,
        DatabaseUsageRecognition recognition)
    {
        var payload = $"{projectContext}|{sourceId}|{relativePath}|{lineNumber}|{recognition.Provider}|{recognition.MethodName}|{recognition.OperationType}|{string.Join(",", recognition.Tables)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"{projectContext}::ExternalConcept::DatabaseOperation::{Convert.ToHexString(hash[..8])}";
    }

    private static string? GetMemberName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };

    private static ExpressionSyntax? GetReceiverExpression(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => null,
            IdentifierNameSyntax => null,
            _ => null
        };

    private static string NormalizeReceiverKey(ExpressionSyntax expression) =>
        expression.ToString().Trim();

    private static bool SupportsLanguage(DatabaseTracingPresetOptions preset, string language) =>
        preset.Languages.Count == 0
        || preset.Languages.Any(candidate => string.Equals(candidate, language, StringComparison.OrdinalIgnoreCase));

    private static bool SupportsCypherMethod(string methodName, DatabaseTracingPresetOptions preset) =>
        preset.ReadMethods.Concat(preset.WriteMethods)
            .Any(candidate => string.Equals(candidate, methodName, StringComparison.OrdinalIgnoreCase));

    private static DatabaseOperationType? InferCypherOperationType(string statementText)
    {
        if (CypherWriteStatementRegex.IsMatch(statementText))
            return DatabaseOperationType.Write;

        if (CypherReadStatementRegex.IsMatch(statementText))
            return DatabaseOperationType.Read;

        return null;
    }

    private readonly record struct DatabaseUsageRecognition(
        string RecognizerId,
        string Provider,
        string MethodName,
        DatabaseOperationType OperationType,
        IReadOnlyList<string> Tables);

    private enum DatabaseOperationType
    {
        Read,
        Write
    }
}
