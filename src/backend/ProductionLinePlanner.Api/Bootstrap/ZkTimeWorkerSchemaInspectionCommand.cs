using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Infrastructure.Attendance;

namespace ProductionLinePlanner.Api.Bootstrap;

/// <summary>Development-only metadata inspection for selecting an existing ZKTime active-service source.</summary>
public static class ZkTimeWorkerSchemaInspectionCommand
{
    private const string CommandName = "--zk-worker-schema-inspect";

    public static bool IsRequested(IEnumerable<string> args) => args.Contains(CommandName, StringComparer.Ordinal);

    public static async Task ExecuteAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("ZKTime worker schema inspection is available only in Development.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var attendanceDb = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var connection = attendanceDb.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            var userInfoColumns = await QueryAsync(connection, @"
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'USERINFO'
ORDER BY ORDINAL_POSITION;");
            var candidateObjects = await QueryAsync(connection, @"
SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
  AND (UPPER(TABLE_NAME) LIKE '%CURRENT%EMPLOYEE%'
       OR UPPER(TABLE_NAME) LIKE '%EMPLOYEE%IMPORT%'
       OR UPPER(TABLE_NAME) LIKE '%EMPLOY%'
       OR UPPER(TABLE_NAME) LIKE '%SERVICE%')
ORDER BY TABLE_SCHEMA, TABLE_NAME;");
            var currentEmployeesImportColumns = await QueryAsync(connection, @"
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'CurrentEmployeesImport'
ORDER BY ORDINAL_POSITION;");
            var currentEmployeeCounts = await QueryAsync(connection, @"
SELECT
    COUNT(*) AS ImportRows,
    COUNT(DISTINCT UPPER(LTRIM(RTRIM(EmployeeCode)))) AS DistinctCurrentEmployeeCodes,
    SUM(CASE WHEN NULLIF(LTRIM(RTRIM(EmployeeCode)), '') IS NULL THEN 1 ELSE 0 END) AS BlankEmployeeCodes
FROM dbo.CurrentEmployeesImport;");
            var currentEmployeeUserInfoOverlap = await QueryAsync(connection, @"
WITH CurrentCodes AS (
    SELECT DISTINCT UPPER(LTRIM(RTRIM(EmployeeCode))) AS EmployeeCode
    FROM dbo.CurrentEmployeesImport
    WHERE NULLIF(LTRIM(RTRIM(EmployeeCode)), '') IS NOT NULL
)
SELECT COUNT(DISTINCT U.USERID) AS MatchingUserInfoWorkers
FROM dbo.USERINFO AS U
INNER JOIN CurrentCodes AS C ON C.EmployeeCode = UPPER(LTRIM(RTRIM(U.BADGENUMBER)));");

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                UserInfoColumns = userInfoColumns,
                CandidateCurrentEmployeeObjects = candidateObjects,
                CurrentEmployeesImportColumns = currentEmployeesImportColumns,
                CurrentEmployeeCounts = currentEmployeeCounts,
                CurrentEmployeeUserInfoOverlap = currentEmployeeUserInfoOverlap
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<IReadOnlyCollection<IReadOnlyDictionary<string, string?>>> QueryAsync(
        System.Data.Common.DbConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index)?.ToString();
            }
            rows.Add(row);
        }
        return rows;
    }
}
