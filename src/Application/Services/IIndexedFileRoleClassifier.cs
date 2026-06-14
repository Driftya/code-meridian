using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public interface IIndexedFileRoleClassifier
{
    IndexedFileRole Classify(string relativePath);
}
