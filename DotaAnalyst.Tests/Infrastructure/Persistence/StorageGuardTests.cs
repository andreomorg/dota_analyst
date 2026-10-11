using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

public class StorageGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DatabaseError_BecomesStorageUnavailable()
    {
        var guard = new StorageGuard(new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<StorageUnavailableException>(
            () => guard.RunAsync("testing", () => throw new NpgsqlException("connection refused")));

        Assert.IsType<NpgsqlException>(exception.InnerException);
    }

    [Fact]
    public async Task AfterDatabaseError_DatabaseIsSkippedForAWhile()
    {
        var clock = new FixedTimeProvider(Now);
        var guard = new StorageGuard(clock);
        await Assert.ThrowsAsync<StorageUnavailableException>(
            () => guard.RunAsync("testing", () => throw new NpgsqlException("connection refused")));

        // Skipped: the action is not even run.
        var calls = 0;
        await Assert.ThrowsAsync<StorageUnavailableException>(() => guard.RunAsync("testing", () =>
        {
            calls++;
            return Task.CompletedTask;
        }));
        Assert.Equal(0, calls);

        // Tried again once the pause is over.
        clock.Now += StorageGuard.SkipAfterFailure;
        await guard.RunAsync("testing", () =>
        {
            calls++;
            return Task.CompletedTask;
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ErrorAnsweredByTheDatabase_DoesNotSkipIt()
    {
        // e.g. a duplicate key: PostgreSQL is up and answering, so other operations should still use it.
        var guard = new StorageGuard(new FixedTimeProvider(Now));
        var duplicateKey = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        await Assert.ThrowsAsync<StorageUnavailableException>(
            () => guard.RunAsync("testing", () => throw new DbUpdateException("save failed", duplicateKey)));

        Assert.True(await guard.RunAsync("testing", () => Task.FromResult(true)));
    }

    [Fact]
    public async Task BadSavedData_DoesNotSkipTheDatabase()
    {
        var guard = new StorageGuard(new FixedTimeProvider(Now));
        await Assert.ThrowsAsync<StorageUnavailableException>(
            () => guard.RunAsync("testing", () => throw new JsonException("bad row")));

        Assert.True(await guard.RunAsync("testing", () => Task.FromResult(true)));
    }

    [Fact]
    public async Task Cancellation_IsNotTranslated()
    {
        var guard = new StorageGuard(new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => guard.RunAsync("testing", () => throw new OperationCanceledException()));
    }
}
