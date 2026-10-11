using System.Data.Common;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DotaAnalyst.Infrastructure.Persistence;

// Every repository runs its database work through here (one instance for the whole app: Singleton). It:
// - turns database errors into StorageUnavailableException, the one exception the Application knows about
//   (like OpenDotaClient does with DataSourceUnavailableException);
// - works as a "circuit breaker": after a connection failure the database is skipped for a short while, so
//   while it is down each request fails at once instead of waiting for connection timeouts and retries.
public sealed class StorageGuard(TimeProvider timeProvider)
{
    public static readonly TimeSpan SkipAfterFailure = TimeSpan.FromSeconds(30);

    // UTC ticks until which the database is skipped. Read/written with Interlocked: many requests share it.
    private long skipUntilTicks;

    public async Task<T> RunAsync<T>(string operation, Func<Task<T>> action)
    {
        if (timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref skipUntilTicks))
        {
            throw new StorageUnavailableException($"Skipped {operation}: the database failed less than {SkipAfterFailure} ago.");
        }

        try
        {
            return await action();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // Only "can't reach the database" skips it. Bad data in one row, or an error the database itself
            // answered with (e.g. a duplicate key), says nothing about it being down.
            if (!IsBadData(ex) && !DatabaseAnswered(ex))
            {
                Interlocked.Exchange(ref skipUntilTicks, (timeProvider.GetUtcNow() + SkipAfterFailure).UtcTicks);
            }

            throw new StorageUnavailableException($"Database error while {operation}.", ex);
        }
    }

    public Task RunAsync(string operation, Func<Task> action) =>
        RunAsync(operation, async () =>
        {
            await action();
            return true;
        });

    // Connection/SQL errors, retries used up, timeouts, and saved data that can no longer be read.
    // Cancellation (OperationCanceledException) is not a failure and is never translated.
    public static bool IsStorageFailure(Exception ex) =>
        ex is DbException or DbUpdateException or RetryLimitExceededException or TimeoutException || IsBadData(ex);

    private static bool IsBadData(Exception ex) => ex is JsonException or InvalidDataException;

    // PostgresException = PostgreSQL received the command and answered with an error (EF may wrap it,
    // e.g. in DbUpdateException). Connection problems are other NpgsqlExceptions, timeouts or used-up retries.
    private static bool DatabaseAnswered(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException)
            {
                return true;
            }
        }

        return false;
    }
}
