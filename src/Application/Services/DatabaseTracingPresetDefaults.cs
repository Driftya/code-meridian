namespace CodeMeridian.Application.Services;

internal static class DatabaseTracingPresetDefaults
{
    public static List<DatabaseTracingPresetOptions> Create() =>
    [
        new()
        {
            Id = "ef-core",
            Strategy = "EfCore",
            Provider = "EFCore",
            Languages = ["CSharp"],
            ReadMethods =
            [
                "ToList",
                "ToListAsync",
                "First",
                "FirstAsync",
                "FirstOrDefault",
                "FirstOrDefaultAsync",
                "Single",
                "SingleAsync",
                "SingleOrDefault",
                "SingleOrDefaultAsync",
                "Any",
                "AnyAsync",
                "Count",
                "CountAsync",
                "LongCount",
                "LongCountAsync",
                "Find",
                "FindAsync",
                "FromSql",
                "FromSqlRaw",
                "FromSqlInterpolated"
            ],
            WriteMethods =
            [
                "Add",
                "AddAsync",
                "AddRange",
                "AddRangeAsync",
                "Update",
                "UpdateRange",
                "Remove",
                "RemoveRange",
                "ExecuteUpdate",
                "ExecuteUpdateAsync",
                "ExecuteDelete",
                "ExecuteDeleteAsync",
                "ExecuteSql",
                "ExecuteSqlRaw",
                "ExecuteSqlRawAsync",
                "ExecuteSqlInterpolated",
                "ExecuteSqlInterpolatedAsync"
            ],
            ReceiverTextHints = ["context", "db", "database"],
            TableSources = ["GenericTypeArgument", "ReceiverMemberName", "SqlText"]
        },
        new()
        {
            Id = "dapper",
            Strategy = "Dapper",
            Provider = "Dapper",
            Languages = ["CSharp"],
            ReadMethods =
            [
                "Query",
                "QueryAsync",
                "QueryFirst",
                "QueryFirstAsync",
                "QueryFirstOrDefault",
                "QueryFirstOrDefaultAsync",
                "QuerySingle",
                "QuerySingleAsync",
                "QuerySingleOrDefault",
                "QuerySingleOrDefaultAsync",
                "QueryMultiple",
                "QueryMultipleAsync",
                "ExecuteScalar",
                "ExecuteScalarAsync"
            ],
            WriteMethods = ["Execute", "ExecuteAsync"],
            ReceiverTextHints = ["connection", "dbConnection", "sqlConnection", "npgsqlConnection"],
            TableSources = ["SqlText"]
        },
        new()
        {
            Id = "raw-sql",
            Strategy = "RawSql",
            Provider = "RawSql",
            Languages = ["CSharp"],
            ReadMethods =
            [
                "ExecuteReader",
                "ExecuteReaderAsync",
                "ExecuteScalar",
                "ExecuteScalarAsync"
            ],
            WriteMethods = ["ExecuteNonQuery", "ExecuteNonQueryAsync"],
            ReceiverTextHints = ["command", "dbCommand", "sqlCommand"],
            CommandCreationTypeHints =
            [
                "DbCommand",
                "SqlCommand",
                "NpgsqlCommand",
                "MySqlCommand",
                "SqliteCommand",
                "OracleCommand"
            ],
            StatementTextProperties = ["CommandText"],
            TableSources = ["SqlText"]
        },
        new()
        {
            Id = "prisma",
            Strategy = "Prisma",
            Provider = "Prisma",
            Languages = ["TypeScript"],
            ReadMethods =
            [
                "findUnique",
                "findUniqueOrThrow",
                "findFirst",
                "findFirstOrThrow",
                "findMany",
                "aggregate",
                "count",
                "groupBy"
            ],
            WriteMethods =
            [
                "create",
                "createMany",
                "update",
                "updateMany",
                "upsert",
                "delete",
                "deleteMany"
            ],
            ReceiverTextHints = ["prisma", "tx"],
            ImportModuleHints = ["@prisma/client"],
            TableSources = ["ReceiverMemberName"]
        },
        new()
        {
            Id = "knex",
            Strategy = "Knex",
            Provider = "Knex",
            Languages = ["TypeScript"],
            ReadMethods = ["select", "first", "pluck"],
            WriteMethods = ["insert", "update", "del", "delete", "truncate"],
            ReceiverTextHints = ["knex", "db"],
            ImportModuleHints = ["knex"],
            TableSources = ["FirstArgumentString", "FromMethodArgument", "IntoMethodArgument"]
        },
        new()
        {
            Id = "neo4j-cypher",
            Strategy = "Cypher",
            Provider = "Neo4j",
            Languages = ["CSharp", "TypeScript"],
            ReadMethods =
            [
                "Run",
                "RunAsync",
                "ExecuteRead",
                "ExecuteReadAsync",
                "ExecuteWrite",
                "ExecuteWriteAsync",
                "ExecuteQuery"
            ],
            WriteMethods =
            [
                "Run",
                "RunAsync",
                "ExecuteRead",
                "ExecuteReadAsync",
                "ExecuteWrite",
                "ExecuteWriteAsync",
                "ExecuteQuery"
            ],
            StatementArgumentIndexes = [0],
            ReceiverTextHints = ["session", "tx", "driver"],
            ImportModuleHints = ["neo4j-driver"],
            TableSources = ["CypherText"]
        }
    ];
}
