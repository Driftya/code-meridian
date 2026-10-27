namespace CodeMeridian.PowerShellIndexer;

public static class PowerShellBinder
{
    public static IReadOnlyList<PowerShellFileAnalysis> Bind(IReadOnlyList<PowerShellFileAnalysis> analyses, bool caseSensitive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = analyses.ToDictionary(file => file.Path, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        return analyses.Select(file => BindFile(file, files, cancellationToken)).ToArray();
    }

    private static PowerShellFileAnalysis BindFile(PowerShellFileAnalysis file,
        IReadOnlyDictionary<string, PowerShellFileAnalysis> files, CancellationToken cancellationToken)
    {
        if (file.Bodies.Count == 0) return file;
        var modules = file.Modules.Select(module => BindModule(file, module, files)).ToArray();
        var bodies = file.Bodies.ToDictionary(body => body.Id, StringComparer.Ordinal);
        var commands = new List<PowerShellCommand>();
        foreach (var command in file.Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PowerShellCommand Result(string status, string reason, string? target = null) => command with
                { BindingStatus = status, Reason = reason, TargetId = target };
            if (file.ParseErrorCount > 0 || file.HasDynamicCommandTable || command.Deferred || command.ExpressionKind == "dynamic"
                || command.Name?.IndexOfAny(['*', '?', '[', ']']) >= 0)
            {
                commands.Add(Result("indeterminate", file.ParseErrorCount > 0 ? "parse_errors" : command.Deferred ? "deferred_scope" : "dynamic_resolution"));
                continue;
            }
            if (command.ExpressionKind == "script_root")
            {
                var path = PowerShellPathBinding.Resolve(file.Path, command.ExpressionKind, command.PathValue);
                commands.Add(path is null ? Result("indeterminate", "unsupported_or_outside_path")
                    : files.TryGetValue(path, out var target) && target.Bodies.Count > 0 && target.Status == "complete"
                        && target.Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                        ? Result("resolved", command.Invocation == "dot_source" ? "static_dot_source" : "static_script_path",
                            command.Invocation == "dot_source" ? target.FileId : target.Bodies[0].Id)
                        : Result("unresolved_local", "target_not_in_successful_selection"));
                continue;
            }
            if (command.Invocation == "dot_source" || command.PathValue?.IndexOfAny(['/', '\\', ':']) >= 0
                || command.PathValue?.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) == true
                || command.PathValue?.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) == true)
            {
                commands.Add(Result("indeterminate", "runtime_working_directory_or_qualified_command"));
                continue;
            }
            commands.Add(BindName(file, command, modules, bodies, files));
        }
        var limited = commands.Any(command => command.BindingStatus is "indeterminate" or "unresolved_local")
            || modules.Any(module => module.BindingStatus != "resolved");
        return file with { Commands = commands, Modules = modules, Status = limited && file.Status == "complete" ? "partial" : file.Status };
    }

    private static PowerShellModuleReference BindModule(PowerShellFileAnalysis file, PowerShellModuleReference module,
        IReadOnlyDictionary<string, PowerShellFileAnalysis> files)
    {
        PowerShellModuleReference Result(string status, string reason, string? id = null) => module with
            { BindingStatus = status, Reason = reason, TargetId = id };
        if (file.HasDynamicCommandTable || !module.OptionsSupported || module.ExpressionKind == "dynamic") return Result("indeterminate", "unsupported_module_expression_or_options");
        var path = PowerShellPathBinding.Resolve(file.Path, module.ExpressionKind, module.Target);
        if (path is null) return Result("indeterminate", "module_search_path_or_working_directory");
        if (!files.TryGetValue(path, out var target)) return Result("unresolved_local", "module_not_in_selection");
        return PowerShellExportResolver.Exports(target, files) is null ? Result("indeterminate", "module_visibility_unverified")
            : Result("resolved", "static_local_module", target.FileId);
    }

    private static PowerShellCommand BindName(PowerShellFileAnalysis file, PowerShellCommand command,
        IReadOnlyList<PowerShellModuleReference> modules, IReadOnlyDictionary<string, PowerShellBody> bodies,
        IReadOnlyDictionary<string, PowerShellFileAnalysis> files)
    {
        PowerShellCommand Result(string status, string reason, string? id = null) => command with
            { BindingStatus = status, Reason = reason, TargetId = id };
        var owner = command.OwnerId;
        var before = command.Span.Offset;
        while (true)
        {
            var imports = modules.Where(module => module.OwnerId == owner && (module.Kind == "using" || module.Span.Offset < before)).ToArray();
            var dotSources = file.Commands.Where(item => item.OwnerId == owner && item.Invocation == "dot_source" && item.Span.Offset < before).ToArray();
            if (imports.Any(module => module.BindingStatus != "resolved") || dotSources.Any(item =>
                PowerShellPathBinding.Resolve(file.Path, item.ExpressionKind, item.PathValue) is not { } path
                || !files.TryGetValue(path, out var target) || PowerShellExportResolver.Exports(target, files, true) is null))
                return Result("indeterminate", "unverified_import_visibility");
            var local = file.Bodies.Where(body => body.ParentId == owner && body.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (local.Any(body => body.Conditional) || local.Length > 1) return Result("indeterminate", "conditional_or_redefined_name");
            var imported = new List<(PowerShellBody Body, int Offset)>();
            foreach (var module in imports)
            {
                var target = files.Values.Single(file => file.FileId == module.TargetId);
                imported.AddRange(PowerShellExportResolver.Exports(target, files)!.Where(body => body.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(body => (body, module.Kind == "using" ? -1 : module.Span.Offset)));
            }
            foreach (var source in dotSources)
            {
                var target = files[PowerShellPathBinding.Resolve(file.Path, source.ExpressionKind, source.PathValue)!];
                imported.AddRange(PowerShellExportResolver.Exports(target, files, true)!.Where(body => body.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(body => (body, source.Span.Offset)));
            }
            if (imported.Count > 1) return Result("indeterminate", "multiple_imported_definitions");
            if (local.Length == 1 && local[0].Span.Offset >= before) return Result("unresolved_local", "definition_not_yet_established");
            var selected = local.Length == 1 && (imported.Count == 0 || local[0].Span.Offset > imported[0].Offset)
                ? local[0] : imported.FirstOrDefault().Body;
            if (selected is not null) return Result("resolved", imported.Any(item => item.Body.Id == selected.Id) ? "static_import_visibility" : "lexical_definition", selected.Id);
            if (!bodies.TryGetValue(owner, out var body) || body.ParentId is null) break;
            before = body.Span.Offset + 1; // The currently executing function's own declaration is established.
            owner = body.ParentId;
        }
        return file.Bodies.Any(body => body.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase))
            ? Result("unresolved_local", "definition_outside_visible_scope") : Result("external_unindexed", "no_supported_repository_definition");
    }
}
