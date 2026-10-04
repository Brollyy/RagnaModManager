using System.Globalization;
using Microsoft.Data.Sqlite;

namespace RagnaModManager.Core.Database;

internal sealed class SqliteConnection : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public SqliteConnection(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _connection.Open();
    }

    public void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public List<Dictionary<string, string?>> Query(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var rows = new List<Dictionary<string, string?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i)
                    ? null
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            }

            rows.Add(row);
        }

        return rows;
    }

    public static string Quote(string? value) => value is null ? "NULL" : "'" + value.Replace("'", "''") + "'";

    public void Dispose() => _connection.Dispose();
}
