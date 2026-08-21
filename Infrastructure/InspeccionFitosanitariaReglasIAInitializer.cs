using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Instala las reglas defensivas de base de datos que separan de forma
    /// inequívoca el análisis IA inicial de las reevaluaciones solicitadas por
    /// el técnico.
    ///
    /// Reglas:
    /// - ANALISIS_INICIAL solo puede registrarse cuando la fotografía todavía
    ///   no posee un resultado IA persistido.
    /// - Si una evaluación falla pero existe un resultado anterior válido, la
    ///   fotografía conserva PENDIENTE_DECISION_TECNICO y el error queda como
    ///   trazabilidad del último intento fallido.
    /// - Una reevaluación fallida no elimina ni reemplaza el resultado anterior.
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

                // Garantiza primero las tablas base del expediente por fotografía.
                // Esto permite que el mismo arranque funcione también sobre una
                // base recién creada y no solo sobre instalaciones existentes.
                await new InspeccionFitosanitariaDatabase(db)
                    .InicializarAsync(cancellationToken);

                await AsegurarEstructuraAsync(cancellationToken);
                inicializada = true;

                logger.LogInformation(
                    "Reglas de análisis y reevaluación IA verificadas correctamente.");
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

        private async Task AsegurarEstructuraAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
IF OBJECT_ID(N'dbo.diagnosticoIAImagen', N'U') IS NULL
BEGIN
    THROW 51020, N'No existe dbo.diagnosticoIAImagen.', 1;
END;

IF OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoIA', N'U') IS NULL
BEGIN
    THROW 51020, N'No existe dbo.diagnosticoIAImagenResultadoIA.', 1;
END;

IF OBJECT_ID(N'dbo.diagnosticoIAImagenRevisionIA', N'U') IS NULL
BEGIN
    THROW 51020, N'No existe dbo.diagnosticoIAImagenRevisionIA.', 1;
END;

/*
 * Defensa de consistencia para interrupciones abruptas: si el resultado IA
 * ya quedó guardado mientras la fotografía continúa en ANALIZANDO_IA, el
 * mismo commit consolida el estado pendiente de decisión técnica y completa
 * la revisión vigente. Este trigger también queda creado automáticamente en
 * instalaciones nuevas, sin depender de ejecutar primero una recuperación.
 */
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_diagnosticoIAImagenResultadoIA_consolidarEstado
ON dbo.diagnosticoIAImagenResultadoIA
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cambios TABLE
    (
        FotografiaId INT NOT NULL PRIMARY KEY
    );

    INSERT INTO @cambios (FotografiaId)
    SELECT DISTINCT
        foto.DiagnosticoIAImagenId
    FROM inserted resultado
    INNER JOIN dbo.diagnosticoIAImagen foto
        ON foto.DiagnosticoIAImagenId = resultado.DiagnosticoIAImagenId
    WHERE UPPER(ISNULL(foto.Estado, N'''')) = N''ANALIZANDO_IA''
      AND ISNULL(foto.Activo, 1) = 1
      AND ISNULL(foto.Descartada, 0) = 0;

    UPDATE foto
    SET Estado = N''PENDIENTE_DECISION_TECNICO'',
        FechaAnalisisIAUtc = COALESCE(
            foto.FechaAnalisisIAUtc,
            SYSUTCDATETIME()),
        ErrorProcesamiento = N''''
    FROM dbo.diagnosticoIAImagen foto
    INNER JOIN @cambios cambio
        ON cambio.FotografiaId = foto.DiagnosticoIAImagenId;

    ;WITH ultimaRevision AS
    (
        SELECT
            revision.DiagnosticoIAImagenRevisionIAId,
            ROW_NUMBER() OVER
            (
                PARTITION BY revision.DiagnosticoIAImagenId
                ORDER BY revision.FechaSolicitudUtc DESC,
                         revision.DiagnosticoIAImagenRevisionIAId DESC
            ) AS NumeroFila
        FROM dbo.diagnosticoIAImagenRevisionIA revision
        INNER JOIN @cambios cambio
            ON cambio.FotografiaId = revision.DiagnosticoIAImagenId
    )
    UPDATE revision
    SET Estado = N''COMPLETADA'',
        Error = N'''',
        FechaRespuestaUtc = COALESCE(
            revision.FechaRespuestaUtc,
            SYSUTCDATETIME())
    FROM dbo.diagnosticoIAImagenRevisionIA revision
    INNER JOIN ultimaRevision ultima
        ON ultima.DiagnosticoIAImagenRevisionIAId =
           revision.DiagnosticoIAImagenRevisionIAId
    WHERE ultima.NumeroFila = 1
      AND UPPER(ISNULL(revision.Estado, N'''')) IN
          (N''ANALIZANDO'', N''PENDIENTE'');
END;
');

/*
 * Corrige instalaciones que hayan quedado con ERROR_IA después de una
 * reevaluación fallida, siempre que todavía exista un resultado anterior.
 * El mensaje de ErrorProcesamiento se conserva para auditoría y diagnóstico.
 */
DECLARE @normalizadas TABLE
(
    FotografiaId INT NOT NULL PRIMARY KEY,
    UsuarioId INT NOT NULL
);

INSERT INTO @normalizadas (FotografiaId, UsuarioId)
SELECT
    foto.DiagnosticoIAImagenId,
    inspeccion.UsuarioSolicitanteId
FROM dbo.diagnosticoIAImagen foto
INNER JOIN dbo.diagnosticoIA inspeccion
    ON inspeccion.DiagnosticoIAId = foto.DiagnosticoIAId
WHERE UPPER(ISNULL(foto.Estado, N'')) = N'ERROR_IA'
  AND ISNULL(foto.Activo, 1) = 1
  AND ISNULL(foto.Descartada, 0) = 0
  AND EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagenResultadoIA resultado
      WHERE resultado.DiagnosticoIAImagenId =
            foto.DiagnosticoIAImagenId
  );

UPDATE foto
SET Estado = N'PENDIENTE_DECISION_TECNICO'
FROM dbo.diagnosticoIAImagen foto
INNER JOIN @normalizadas normalizada
    ON normalizada.FotografiaId = foto.DiagnosticoIAImagenId;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId,
    UsuarioId,
    EstadoAnterior,
    EstadoNuevo,
    Accion,
    Detalle,
    FechaUtc
)
SELECT
    normalizada.FotografiaId,
    normalizada.UsuarioId,
    N'ERROR_IA',
    N'PENDIENTE_DECISION_TECNICO',
    N'ESTADO_IA_NORMALIZADO_ARRANQUE',
    N'Se conservó el último resultado IA válido y se normalizó la fotografía para continuar con la decisión técnica.',
    SYSUTCDATETIME()
FROM @normalizadas normalizada;

/*
 * La revisión se crea antes de llamar al proveedor. Por ello este trigger
 * impide que una llamada directa a /procesar-fotografias vuelva a registrar
 * ANALISIS_INICIAL cuando ya existe un resultado. La llamada al proveedor no
 * llega a ejecutarse.
 */
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_diagnosticoIAImagenRevisionIA_bloquearInicialDuplicado
ON dbo.diagnosticoIAImagenRevisionIA
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted revision
        INNER JOIN dbo.diagnosticoIAImagenResultadoIA resultado
            ON resultado.DiagnosticoIAImagenId =
               revision.DiagnosticoIAImagenId
        WHERE UPPER(ISNULL(revision.TipoRevision, N'''')) =
              N''ANALISIS_INICIAL''
    )
    BEGIN
        THROW 51021,
            N''La fotografía ya cuenta con un análisis IA. Para analizarla nuevamente debe solicitar una nueva evaluación IA.'',
            1;
    END;
END;
');

/*
 * CambiarEstadoFotoAsync registra primero el ERROR_IA y su historial. Este
 * trigger escucha ese historial: si la fotografía ya poseía un resultado IA,
 * restaura PENDIENTE_DECISION_TECNICO y agrega inmediatamente una segunda
 * entrada de auditoría explicando que se conservó el resultado anterior.
 *
 * De esta manera la trazabilidad queda en orden:
 * ANALIZANDO_IA -> ERROR_IA -> PENDIENTE_DECISION_TECNICO.
 */
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_diagnosticoIAImagenHistorialV2_conservarResultadoAnterior
ON dbo.diagnosticoIAImagenHistorialV2
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF TRIGGER_NESTLEVEL() > 1
        RETURN;

    DECLARE @cambios TABLE
    (
        FotografiaId INT NOT NULL PRIMARY KEY,
        UsuarioId INT NOT NULL
    );

    INSERT INTO @cambios (FotografiaId, UsuarioId)
    SELECT
        historial.DiagnosticoIAImagenId,
        MAX(historial.UsuarioId)
    FROM inserted historial
    INNER JOIN dbo.diagnosticoIAImagen foto
        ON foto.DiagnosticoIAImagenId =
           historial.DiagnosticoIAImagenId
    WHERE UPPER(ISNULL(historial.EstadoNuevo, N'''')) = N''ERROR_IA''
      AND ISNULL(foto.Activo, 1) = 1
      AND ISNULL(foto.Descartada, 0) = 0
      AND EXISTS
      (
          SELECT 1
          FROM dbo.diagnosticoIAImagenResultadoIA resultado
          WHERE resultado.DiagnosticoIAImagenId =
                historial.DiagnosticoIAImagenId
      )
    GROUP BY historial.DiagnosticoIAImagenId;

    UPDATE foto
    SET Estado = N''PENDIENTE_DECISION_TECNICO''
    FROM dbo.diagnosticoIAImagen foto
    INNER JOIN @cambios cambio
        ON cambio.FotografiaId = foto.DiagnosticoIAImagenId;

    INSERT INTO dbo.diagnosticoIAImagenHistorialV2
    (
        DiagnosticoIAImagenId,
        UsuarioId,
        EstadoAnterior,
        EstadoNuevo,
        Accion,
        Detalle,
        FechaUtc
    )
    SELECT
        cambio.FotografiaId,
        cambio.UsuarioId,
        N''ERROR_IA'',
        N''PENDIENTE_DECISION_TECNICO'',
        N''REEVALUACION_IA_ERROR_RESULTADO_ANTERIOR_CONSERVADO'',
        N''La evaluación más reciente falló, pero se conservó el último resultado IA válido. El técnico puede enviarlo al analizador o solicitar otra reevaluación disponible.'',
        SYSUTCDATETIME()
    FROM @cambios cambio;
END;
');
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
    }
}
