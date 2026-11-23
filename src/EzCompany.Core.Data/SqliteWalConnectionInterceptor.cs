using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EzCompany.Core.Data;

/// <summary>
/// Enforces SQLite WAL mode, foreign key constraints, and concurrent busy timeout on every opened connection.
/// </summary>
public sealed class SqliteWalConnectionInterceptor : DbConnectionInterceptor
{
    private const string PragmasSql = """
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;
        PRAGMA busy_timeout=5000;
        PRAGMA synchronous=NORMAL;
        """;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = PragmasSql;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = PragmasSql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
