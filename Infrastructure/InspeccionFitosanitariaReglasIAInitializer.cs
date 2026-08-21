using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Limpia artefactos temporales de una versión anterior de las reglas IA.
    ///
    /// Las reglas de negocio del análisis inicial y de las reevaluaciones se
    /// aplican exclusivamente desde el backend. No se crean ni se mantienen
    /// triggers para este flujo.
    /// </summary>
    public sealed class InspeccionFitosanitariaReglasIAInitializer
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;

        private readonly DiagnosticoIADbContext db;
        private readonly ILogger<InspeccionFitosanitariaReglasIAInitializer>
            logger;

        public InspeccionFitosanitariaReglasIAInitializer(
            DiagnosticoIADbContext db,
            ILogger<InspeccionFitosanitariaReglasIAInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            if (inicializada)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (inicializada)
                    return;

                await EliminarTriggersLegadosAsync(cancellationToken);
                inicializada = true;

                logger.LogInformation(
                    "Reglas IA verificadas en backend. No se utilizan triggers para análisis inicial ni reevaluaciones.");
            }
            catch
            {
                inicializada = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        /// <summary>
        /// Retira únicamente los tres triggers que fueron introducidos por la
        /// corrección temporal anterior. La operación es idempotente y no toca
        /// otros objetos de la base de datos.
        /// </summary>
        private async Task EliminarTriggersLegadosAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
DROP TRIGGER IF EXISTS dbo.TR_diagnosticoIAImagenResultadoIA_consolidarEstado;
DROP TRIGGER IF EXISTS dbo.TR_diagnosticoIAImagenRevisionIA_bloquearInicialDuplicado;
DROP TRIGGER IF EXISTS dbo.TR_diagnosticoIAImagenHistorialV2_conservarResultadoAnterior;
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
    }
}
