using CodeMeridian.Core.Extensions;

namespace CodeMeridian.Api.Endpoints;

public static class ExtensionEndpoints
{
    public static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/extensions")
            .WithTags("Extensions")
            .WithOpenApi();

        group.MapGet("/", GetAll)
            .WithName("GetExtensions")
            .WithSummary("List all registered extension agents");

        group.MapPost("/register", Register)
            .WithName("RegisterExtension")
            .WithSummary("Register an external agent as a CodeMeridian extension");

        group.MapDelete("/{name}", Unregister)
            .WithName("UnregisterExtension")
            .WithSummary("Remove an extension registration");

        return app;
    }

    private static IResult GetAll(IExtensionRegistry registry) =>
        Results.Ok(registry.GetAll());

    private static IResult Register(AgentExtension extension, IExtensionRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(extension.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(extension.Name)] = ["Name must not be empty."]
            });

        registry.Register(extension);
        return Results.Created($"/api/v1/extensions/{extension.Name}", extension);
    }

    private static IResult Unregister(string name, IExtensionRegistry registry)
    {
        registry.Unregister(name);
        return Results.NoContent();
    }
}
