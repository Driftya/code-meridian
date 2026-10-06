using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class PackageReferenceService(ICodeGraphRepository repository, PackageReferenceMatcher matcher)
{
    public async Task PublishAsync(PackageReferenceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Validate(snapshot);
        await repository.PublishPackageReferencesAsync(snapshot, cancellationToken);
        await ReconcileAsync(cancellationToken);
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = await repository.GetPendingPackageReferencesAsync(100, cancellationToken);
            if (pending.Count == 0) return;
            foreach (var reference in pending)
            {
                var exports = await repository.GetPackageExportCandidatesAsync(reference, cancellationToken);
                await repository.TryResolvePackageReferenceAsync(reference, matcher.Match(reference, exports), cancellationToken);
            }
        }
    }

    public static void Validate(PackageReferenceSnapshot snapshot)
    {
        if (snapshot.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(snapshot.ProjectContext)
            || !Guid.TryParse(snapshot.Generation, out _) || snapshot.Scopes is null || snapshot.Exports is null
            || snapshot.References is null || snapshot.Diagnostics is null)
            throw new ArgumentException("A version 1.0 snapshot with project, generation and non-null collections is required.");
        if (snapshot.Scopes.Count > 1000 || snapshot.Exports.Count > 100_000 || snapshot.References.Count > 100_000)
            throw new ArgumentException("Package reference snapshot exceeds the supported bounds.");
        var scopes = snapshot.Scopes.Select(scope => scope.Id).ToHashSet(StringComparer.Ordinal);
        if (scopes.Count != snapshot.Scopes.Count || snapshot.Exports.Select(export => export.Id).Distinct().Count() != snapshot.Exports.Count
            || snapshot.References.Select(reference => reference.Id).Distinct().Count() != snapshot.References.Count
            || snapshot.Scopes.Any(scope => string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.AssemblyIdentity))
            || snapshot.Exports.Any(export => !scopes.Contains(export.ScopeId) || string.IsNullOrWhiteSpace(export.SymbolKey)
                || string.IsNullOrWhiteSpace(export.SourceId))
            || snapshot.References.Any(reference => !scopes.Contains(reference.ScopeId) || string.IsNullOrWhiteSpace(reference.SymbolKey)
                || string.IsNullOrWhiteSpace(reference.SourceId) || reference.Relationship is not ("Calls" or "Uses" or "Inherits" or "Implements")
                || reference.Line < 1 || reference.Column < 1 || reference.EndLine < reference.Line || reference.EndColumn < 1))
            throw new ArgumentException("Invalid scopes, exports or bound symbol references.");
    }
}
