using Microsoft.Extensions.Options;

namespace CodeMeridian.Tooling.Configuration;

public sealed class ToolCliDefaults
{
    public string DefaultCodeMeridianUrl { get; set; } = "http://localhost:5100";
}

public sealed class ConfigureToolCliDefaults : IConfigureOptions<ToolCliDefaults>
{
    public void Configure(ToolCliDefaults options)
    {
        options.DefaultCodeMeridianUrl = "http://localhost:5100";
    }
}
