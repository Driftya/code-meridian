using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpDatabaseTracingExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-csharp-db-tracing-tests",
        Guid.NewGuid().ToString("N"));

    public CSharpDatabaseTracingExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task IndexAsync_ExtractsEfCoreReadAndWriteOperations()
    {
        var file = WriteFile(
            "src/OrdersRepository.cs",
            """
            namespace Demo;

            public sealed class OrdersRepository
            {
                public async Task LoadAsync(AppDbContext context, Order order)
                {
                    await context.Orders.Where(x => x.Id == order.Id).ToListAsync();
                    context.Orders.Add(order);
                }
            }
            """);

        var requests = await IndexAsync([file]);

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("type").GetString() == "ExternalConcept"
            && HasNodeProperty(request.Body, "externalKind", "DatabaseOperation")
            && HasNodeProperty(request.Body, "provider", "EFCore"));

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("type").GetString() == "DatabaseTable"
            && request.Body.GetProperty("name").GetString() == "Orders");

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "Reads");

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "Writes");
    }

    [Fact]
    public async Task IndexAsync_ExtractsDapperTablesFromSqlText()
    {
        var file = WriteFile(
            "src/OrdersReader.cs",
            """
            namespace Demo;

            public sealed class OrdersReader
            {
                public async Task LoadAsync(IDbConnection connection)
                {
                    const string Sql = "select * from dbo.Orders where Id = @id";
                    await connection.QueryAsync<Order>(Sql, new { id = 7 });
                }
            }
            """);

        var requests = await IndexAsync([file]);

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("type").GetString() == "DatabaseTable"
            && request.Body.GetProperty("name").GetString() == "dbo.Orders");

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && HasNodeProperty(request.Body, "provider", "Dapper"));
    }

    [Fact]
    public async Task IndexAsync_ExtractsRawSqlCommandTextAssignments()
    {
        var file = WriteFile(
            "src/OrdersCommand.cs",
            """
            namespace Demo;

            public sealed class OrdersCommand
            {
                public async Task ExecuteAsync(SqlConnection connection)
                {
                    using var command = new SqlCommand();
                    command.CommandText = "delete from Orders where Id = @id";
                    await command.ExecuteNonQueryAsync();
                }
            }
            """);

        var requests = await IndexAsync([file]);

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && HasNodeProperty(request.Body, "provider", "RawSql"));

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "Writes");
    }

    [Fact]
    public async Task IndexAsync_ExtractsNeo4jCypherLabelsAndRelationshipTypes()
    {
        var file = WriteFile(
            "src/OrdersGraphReader.cs",
            """
            namespace Demo;

            public sealed class OrdersGraphReader
            {
                public async Task LoadAsync(IAsyncQueryRunner session)
                {
                    const string Query = "MATCH (o:Order)-[:PLACED]->(c:Customer) RETURN o";
                    await session.RunAsync(Query);
                }
            }
            """);

        var requests = await IndexAsync([file]);

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && HasNodeProperty(request.Body, "provider", "Neo4j"));

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("type").GetString() == "DatabaseTable"
            && request.Body.GetProperty("name").GetString() == "Order");

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("type").GetString() == "DatabaseTable"
            && request.Body.GetProperty("name").GetString() == "PLACED");

        requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "Reads");
    }

    private async Task<List<(string Method, string Path, JsonElement Body)>> IndexAsync(FileInfo[] files)
    {
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var classifier = new ConfiguredIndexedFileRoleClassifier(Options.Create(new CodebaseIndexingOptions()));
        var sut = new CSharpIndexer(client, classifier, Options.Create(new DatabaseTracingOptions()), NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync(files, "CodeMeridian", _root);
        return handler.Requests;
    }

    private FileInfo WriteFile(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonElement Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var path = NormalizePath(request.RequestUri!.AbsolutePath);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    Requests.Add((request.Method.Method, path, item.Clone()));
            }
            else
            {
                Requests.Add((request.Method.Method, path, doc.RootElement.Clone()));
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }

        private static string NormalizePath(string path) =>
            path.EndsWith("/bulk", StringComparison.Ordinal) ? path[..^"/bulk".Length] : path;
    }

    private static bool HasNodeProperty(JsonElement body, string name, string expectedValue)
    {
        if (!body.TryGetProperty("properties", out var properties))
            return false;

        return properties.TryGetProperty(name, out var propertyValue)
            && string.Equals(propertyValue.GetString(), expectedValue, StringComparison.Ordinal);
    }
}
