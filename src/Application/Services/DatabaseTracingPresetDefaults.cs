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
            TableSources = ["SqlText"]
        }
    ];
}
