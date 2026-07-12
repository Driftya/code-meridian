namespace CodeMeridian.Application.ClientExtensions;

public interface IClientExtensionService
{
    ClientExtensionContract GetContract();
    IReadOnlyList<ClientExtensionExample> ListExamples();
    ClientExtensionExample? GetExample(string exampleId);
}
