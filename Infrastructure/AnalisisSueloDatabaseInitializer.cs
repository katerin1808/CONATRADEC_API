using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Verifica de forma idempotente la estructura adicional utilizada
    /// por el flujo completo del análisis de suelo.
    ///
    /// La columna incluirCalculosComplementarios se crea automáticamente
    /// únicamente cuando todavía no existe.
    ///
    /// Los elementos históricos clasificados como EXCESIVO se marcan como
    /// excluidos solamente durante esa primera creación. En los siguientes
    /// inicios de la API no se modifica ninguna decisión guardada por el
    /// usuario.
    /// </summary>
    public sealed class AnalisisSueloDatabaseInitializer
    {
        private readonly DBContext db;

        private readonly ILogger<
            AnalisisSueloDatabaseInitializer> logger;

        public AnalisisSueloDatabaseInitializer(
            DBContext db,
            ILogger<
                AnalisisSueloDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            /*
             * SQL Server compila todo el lote antes de ejecutar ALTER TABLE.
             * Por eso, el UPDATE que utiliza la columna nueva debe ejecutarse
             * mediante SQL dinámico después de crearla.
             */
            const string sql = """
DECLARE @ColumnaCreada BIT = 0;

IF OBJECT_ID(
       N'[dbo].[analisisSueloCalculoElementoQuimico]',
       N'U') IS NOT NULL
   AND COL_LENGTH(
       N'dbo.analisisSueloCalculoElementoQuimico',
       N'incluirCalculosComplementarios') IS NULL
BEGIN
    ALTER TABLE
        [dbo].[analisisSueloCalculoElementoQuimico]
    ADD
        [incluirCalculosComplementarios] BIT NOT NULL
        CONSTRAINT
            [DF_analisisCalculoElemento_incluirComplementarios]
        DEFAULT (1);

    SET @ColumnaCreada = 1;
END;

IF @ColumnaCreada = 1
BEGIN
    EXEC
    (
        N'
        UPDATE
            [dbo].[analisisSueloCalculoElementoQuimico]
        SET
            [incluirCalculosComplementarios] = 0
        WHERE
            UPPER
            (
                LTRIM
                (
                    RTRIM
                    (
                        ISNULL
                        (
                            [clasificacion],
                            SPACE(0)
                        )
                    )
                )
            ) = N''EXCESIVO'';
        '
    );
END;
""";

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);

                logger.LogInformation(
                    "Estructura adicional del análisis de suelo verificada correctamente.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible verificar la estructura adicional del análisis de suelo.");

                throw;
            }
        }
    }
}