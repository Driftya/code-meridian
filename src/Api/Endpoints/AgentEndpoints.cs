using CodeMeridian.Core.Agents;

namespace CodeMeridian.Api.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agent")
            .WithTags("Agent")
            .WithOpenApi();

        group.MapPost("/ask", AskAsync)
            .WithName("Ask")
            .WithSummary("Send a query to the CodeMeridian orchestrator");

        return app;
    }

    private static async Task<IResult> AskAsync(
        AgentRequest request,
        IAgent orchestrator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Query)] = ["Query must not be empty."]
            });

        var context = new AgentContext { ProjectContext = request.ProjectContext };
        var response = await orchestrator.ProcessAsync(request, context, cancellationToken);

        return response.IsSuccess
            ? Results.Ok(response)
            : Results.Problem(response.ErrorMessage, statusCode: 500);
    }
}
