using Microsoft.Data.Sqlite;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// Builds the smallest SQLite database that OpenTDReport_4's constructor will open: it reads
/// ScoreType, DbMetadata and ResultParameter and nothing else. That is enough to exercise
/// producer detection without shipping a real (multi-GB) tdReport.
/// </summary>
internal sealed class SyntheticTdReport : IDisposable
{
    public string Path { get; }

    private SyntheticTdReport(string path) => Path = path;

    /// <summary>A report as TDPortal writes it: assembly versions in DbMetadata, search parameters present.</summary>
    public static SyntheticTdReport TDPortal(string codesetVersion = "9.9.9.99") =>
        Create(
            dbMetadata: new[]
            {
                ("GenerateBatchedTargetPufDbHT", codesetVersion),
                ("GenerateReportHT", "1.0.0.0"),
                ("pufdb_version", "1.0"),
                ("reporting_version", "1.4"),
            },
            resultParameterRows: 1);

    /// <summary>A report as ProSight PD writes it: only reporting_version, and no search parameters.</summary>
    public static SyntheticTdReport ProSightPD() =>
        Create(
            dbMetadata: new[] { ("reporting_version", "1.4") },
            resultParameterRows: 0);

    public static SyntheticTdReport Create((string Key, string Value)[] dbMetadata, int resultParameterRows)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          $"synthetic-{Guid.NewGuid():N}.tdReport");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        Execute(connection, """
            CREATE TABLE ScoreType (
                Id INTEGER PRIMARY KEY, Name TEXT, Description TEXT, KeyWord TEXT, Format TEXT,
                BadValueRange REAL, GoodValueRange REAL, iSLogScale INTEGER, ScoreDirection INTEGER);
            CREATE TABLE DbMetadata (MetadataKey TEXT PRIMARY KEY, Value TEXT);
            CREATE TABLE ResultParameter (
                ResultSetId INTEGER, GroupName TEXT, SearchName TEXT, Name TEXT, Value TEXT);
            """);

        foreach (var (key, value) in dbMetadata)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO DbMetadata (MetadataKey, Value) VALUES ($k, $v)";
            insert.Parameters.AddWithValue("$k", key);
            insert.Parameters.AddWithValue("$v", value);
            insert.ExecuteNonQuery();
        }

        for (var i = 0; i < resultParameterRows; i++)
        {
            Execute(connection, """
                INSERT INTO ResultParameter (ResultSetId, GroupName, SearchName, Name, Value)
                VALUES (1, 'Search', 'Absolute Mass', 'FdrCutoff', '0.01')
                """);
        }

        return new SyntheticTdReport(path);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // release the file handle before deleting
        if (File.Exists(Path))
            File.Delete(Path);
    }
}
