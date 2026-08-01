using System.Collections.Concurrent;

namespace CONATRADEC_API.Services;

/// <summary>
/// Serializa en una misma instancia de la API las ediciones de un análisis.
/// Evita que dos solicitudes con la misma versión atraviesen la validación al
/// mismo tiempo y terminen sobrescribiéndose silenciosamente.
/// </summary>
public sealed class AnalisisEdicionLockService
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> locks = new();

    public async ValueTask<IAsyncDisposable> AdquirirAsync(
        int analisisSueloCalculoId,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim semaphore = locks.GetOrAdd(
            analisisSueloCalculoId,
            _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);

        return new Releaser(
            analisisSueloCalculoId,
            semaphore,
            locks);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly int id;
        private readonly SemaphoreSlim semaphore;
        private readonly ConcurrentDictionary<int, SemaphoreSlim> locks;
        private int liberado;

        public Releaser(
            int id,
            SemaphoreSlim semaphore,
            ConcurrentDictionary<int, SemaphoreSlim> locks)
        {
            this.id = id;
            this.semaphore = semaphore;
            this.locks = locks;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref liberado, 1) != 0)
                return ValueTask.CompletedTask;

            semaphore.Release();

            if (semaphore.CurrentCount == 1)
                locks.TryRemove(new KeyValuePair<int, SemaphoreSlim>(id, semaphore));

            return ValueTask.CompletedTask;
        }
    }
}
