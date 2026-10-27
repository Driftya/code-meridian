using System.Management.Automation.Language;
using System.Reflection;

namespace CodeMeridian.PowerShellIndexer;

// The public ParseInput performs symbol resolution, including a Get-Module runspace.
// This pinned internal entry point skips post-parse resolution. Never fall back to Default.
internal static class PowerShellSyntaxParser
{
    internal const string Mode = "module_analysis_syntax_only";

    internal static (ScriptBlockAst Ast, ParseError[] Errors, IReadOnlyList<Token> Tokens) Parse(string path, string text)
    {
        var parserType = typeof(Parser);
        var build = parserType.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (build is null || !System.Text.RegularExpressions.Regex.IsMatch(build, @"^7\.6\.8(?:\D|$)"))
            throw new InvalidOperationException("Only the tested 7.6.8 syntax parser is supported.");
        var modeType = parserType.Assembly.GetType("System.Management.Automation.Language.ParseMode")
            ?? throw new InvalidOperationException("The pinned syntax-only parse mode is unavailable.");
        if (!modeType.IsEnum || !Enum.GetNames(modeType).Contains("ModuleAnalysis"))
            throw new InvalidOperationException("The pinned syntax-only parse mode is incompatible.");
        var method = parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(string), typeof(string), typeof(List<Token>), typeof(ParseError[]).MakeByRefType(), modeType], null)
            ?? throw new InvalidOperationException("The pinned syntax-only parse signature is unavailable.");
        var parser = Activator.CreateInstance(parserType, nonPublic: true)
            ?? throw new InvalidOperationException("The pinned parser could not be constructed.");
        object?[] arguments = [path, text, new List<Token>(), null, Enum.Parse(modeType, "ModuleAnalysis")];
        DynamicKeyword.Push();
        try
        {
            // DSC can create a runspace inside the grammar itself, before post-parse checks.
            // The native tokenizer is lexical only; reject that token before entering the grammar.
            RejectConfiguration(parser, path, text);
            var ast = method.Invoke(parser, arguments) as ScriptBlockAst
                ?? throw new InvalidOperationException("The pinned parser returned no syntax tree.");
            return (ast, (ParseError[])arguments[3]!, (List<Token>)arguments[2]!);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException("The pinned syntax-only parser failed.", exception.InnerException);
        }
        finally { DynamicKeyword.Pop(); }
    }

    private static void RejectConfiguration(object parser, string path, string text)
    {
        var tokenizer = typeof(Parser).GetField("_tokenizer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(parser)
            ?? throw new InvalidOperationException("The pinned lexical preflight is unavailable.");
        var type = tokenizer.GetType();
        var initialize = type.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(string), typeof(string), typeof(List<Token>)], null)
            ?? throw new InvalidOperationException("The pinned lexical preflight is incompatible.");
        var next = type.GetMethod("NextToken", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("The pinned lexical preflight is incompatible.");
        initialize.Invoke(tokenizer, [path, text, new List<Token>()]);
        for (var count = 0; count <= text.Length + 1; count++)
        {
            var token = next.Invoke(tokenizer, null) as Token ?? throw new InvalidOperationException("The lexical preflight returned no token.");
            if (token.Kind == TokenKind.Configuration) throw new UnsupportedPowerShellSyntaxException(token.Extent);
            if (token.Kind == TokenKind.EndOfInput) return;
        }
        throw new InvalidOperationException("The lexical preflight exceeded its token budget.");
    }
}
