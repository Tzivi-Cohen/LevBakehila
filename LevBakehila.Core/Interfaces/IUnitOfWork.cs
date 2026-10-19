namespace LevBakehila.Core.Interfaces;

// Lets the Service layer use transactions and SaveChanges without knowing Entity Framework.
// The implementation (Data layer) translates DbUpdateConcurrencyException to ConcurrencyConflictException.
public interface IUnitOfWork
{
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
