using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public interface IAnalysisProfilePolicy
{
    bool Allows(AnalysisProfile profile, IndexedFileRole role);
}
