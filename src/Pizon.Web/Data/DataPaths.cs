using Microsoft.Data.Sqlite;

namespace Pizon.Web.Data;

/// <summary>
/// Works out where the SQLite file lives, so data-protection keys can sit beside it.
/// </summary>
public static class DataPaths
{
    /// <summary>
    /// Gets the directory of the database file in a SQLite connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="contentRoot">The app's content root, for relative paths.</param>
    /// <returns>The absolute directory.</returns>
    public static string DirectoryOf(string connectionString, string contentRoot)
    {
        var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(source) || source == ":memory:")
        {
            return Path.Combine(contentRoot, "App_Data");
        }

        var full = Path.IsPathRooted(source) ? source : Path.Combine(contentRoot, source);
        return Path.GetDirectoryName(Path.GetFullPath(full)) ?? Path.Combine(contentRoot, "App_Data");
    }
}
