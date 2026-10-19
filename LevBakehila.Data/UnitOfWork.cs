using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LevBakehila.Data;

public class UnitOfWork : IUnitOfWork
{
    // PostgreSQL error codes: unique violation, deadlock detected, serialization failure
    private static readonly string[] RaceConditionSqlStates =
    {
        PostgresErrorCodes.UniqueViolation,
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure
    };

    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // RowVersion / Version changed since we read the row => someone else won the race
            throw new ConcurrencyConflictException("הנתונים עודכנו על ידי משתמשת אחרת באותו רגע. יש לרענן ולנסות שוב.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && RaceConditionSqlStates.Contains(pgEx.SqlState))
        {
            throw new ConcurrencyConflictException("הפעולה התנגשה בפעולה מקבילה אחרת. יש לרענן ולנסות שוב.", ex);
        }
    }

    private sealed class EfTransaction : ITransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) => _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) => _transaction.RollbackAsync(cancellationToken);

        // Disposing a transaction that was not committed rolls it back
        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
