namespace CodeMeridian.PowerShellIndexer;

internal static class PowerShellExportResolver
{
    internal static IReadOnlyList<PowerShellBody>? Exports(PowerShellFileAnalysis file,
        IReadOnlyDictionary<string, PowerShellFileAnalysis> files, bool dotSource = false)
    {
        if (file.Status != "complete" || file.Bodies.Count == 0 || file.HasDynamicCommandTable) return null;
        if (file.Manifest is { } manifest)
        {
            if (dotSource || manifest.Status != "literal" || manifest.RootModule is null) return null;
            var path = PowerShellPathBinding.ManifestRoot(file.Path, manifest.RootModule);
            if (path is null || !path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) || !files.TryGetValue(path, out var root)) return null;
            var exported = Exports(root, files);
            return exported?.Where(body => manifest.FunctionsToExport is null || manifest.FunctionsToExport.Contains(body.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        }
        if (dotSource ? !file.DefinitionsOnly : !file.StaticModuleBody || !file.Path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase)) return null;
        if (file.ExportedFunctions is null || file.Modules.Count > 0 || file.Commands.Any(command => command.OwnerId == file.Bodies[0].Id
            && !string.Equals(command.Name, "Export-ModuleMember", StringComparison.OrdinalIgnoreCase))) return null;
        return file.Bodies.Where(body => body.ParentId == file.Bodies[0].Id
            && file.ExportedFunctions.Contains(body.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }
}
