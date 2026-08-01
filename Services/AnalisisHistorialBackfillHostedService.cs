namespace CONATRADEC_API.Services;

/// <summary>
/// Congela progresivamente los análisis creados antes de instalar el historial.
///
/// Se ejecuta en lotes pequeños para no bloquear el inicio de la API. Una vez
/// capturado un cálculo, las ejecuciones posteriores lo omiten.
/// </summary>
public sealed class AnalisisHistorialBackfillHostedService : BackgroundService
{
    private const int TamanoLote = 25;

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<AnalisisHistorialBackfillHostedService> logger;

    public AnalisisHistorialBackfillHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnalisisHistorialBackfillHostedService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(5),
                stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await using AsyncServiceScope scope =
                    scopeFactory.CreateAsyncScope();

                AnalisisReporteHistoricoService historial =
                    scope.ServiceProvider
                        .GetRequiredService<AnalisisReporteHistoricoService>();

                AnalisisEdicionLockService editLock =
                    scope.ServiceProvider
                        .GetRequiredService<AnalisisEdicionLockService>();

                AnalisisEdicionDatabaseLockService databaseLock =
                    scope.ServiceProvider
                        .GetRequiredService<AnalisisEdicionDatabaseLockService>();

                List<int> pendientes =
                    await historial.ObtenerCalculosSinSnapshotAsync(
                        TamanoLote,
                        stoppingToken);

                if (pendientes.Count == 0)
                {
                    logger.LogInformation(
                        "La congelación inicial del historial de análisis finalizó.");
                    return;
                }

                foreach (int calculoId in pendientes)
                {
                    if (stoppingToken.IsCancellationRequested)
                        return;

                    try
                    {
                        await using IAsyncDisposable localReleaser =
                            await editLock.AdquirirAsync(
                                calculoId,
                                stoppingToken);

                        await using IAsyncDisposable databaseReleaser =
                            await databaseLock.AdquirirAsync(
                                calculoId,
                                stoppingToken);

                        AnalisisControlHistorialDto? control =
                            await historial.ObtenerControlAsync(
                                calculoId,
                                stoppingToken);

                        if (control == null ||
                            await historial.ExisteVersionAsync(
                                calculoId,
                                control.VersionRegistro,
                                stoppingToken))
                        {
                            continue;
                        }

                        await historial.CapturarSiFaltaAsync(
                            calculoId,
                            control.VersionRegistro,
                            control.VersionRegistro == 1
                                ? "MIGRACION_INICIAL"
                                : "RECUPERACION_SNAPSHOT",
                            usuarioId: null,
                            control.OrigenRegistro,
                            control.FechaCreacionClienteUtc,
                            solicitud: null,
                            stoppingToken);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "No fue posible congelar el análisis {CalculoId}. Se reintentará en la próxima ejecución.",
                            calculoId);
                    }
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(500),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Cierre normal de la aplicación.
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "El proceso de congelación inicial del historial se detuvo inesperadamente.");
        }
    }
}
