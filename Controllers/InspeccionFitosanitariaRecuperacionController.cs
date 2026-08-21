using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Recuperación explícita de fotografías cuyo análisis IA fue interrumpido.
    ///
    /// Este endpoint nunca vuelve a invocar al proveedor de IA. Si el resultado
    /// ya quedó persistido, únicamente consolida el estado de la fotografía. Si
    /// no existe un resultado y el intento lleva un tiempo prudencial sin
    /// finalizar, lo marca como error recuperable para permitir un nuevo intento.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaRecuperacionController :
        ControllerBase
    {
        private static readonly TimeSpan TiempoMinimoInterrupcion =
            TimeSpan.FromMinutes(10);

        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly ILogger<InspeccionFitosanitariaRecuperacionController>
            logger;

        public InspeccionFitosanitariaRecuperacionController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control,
            ILogger<InspeccionFitosanitariaRecuperacionController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.control = control;
            this.logger = logger;
            database = new InspeccionFitosanitariaDatabase(db);
        }

        [HttpPost("{id:int}/recuperar-analisis-ia")]
        public async Task<IActionResult> RecuperarAnalisisIA(
            int id,
            [FromBody] RecuperarAnalisisIARequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);

            // La recuperación actual no depende de permisos DDL. La defensa
            // adicional se instala cuando la cuenta de la API puede modificar
            // el esquema; si no puede, el endpoint continúa funcionando.
            try
            {
                await AsegurarTriggerConsistenciaAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No fue posible instalar la defensa automática de consistencia IA. La recuperación explícita continuará disponible.");
            }

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NotFound(Error("La inspección no existe o ya no está activa."));

            if (registro.UsuarioSolicitanteId != usuarioId.Value)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    Error(
                        "Solo el técnico que creó la inspección puede recuperar un análisis IA interrumpido."));
            }

            if (registro.EtapaTecnicaFinalizada || registro.CerradaDefinitiva)
            {
                return Conflict(Error(
                    "La etapa técnica ya está finalizada o el expediente está cerrado. No se permite recuperar análisis desde esta etapa."));
            }

            int[] fotografiaIds = (request.FotografiaIds ?? [])
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            if (fotografiaIds.Length == 0)
            {
                return BadRequest(Error(
                    "Debe indicar al menos una fotografía para recuperar."));
            }

            DiagnosticoIA? inspeccion = await db.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

            if (inspeccion == null)
                return NotFound(Error("La inspección no existe."));

            Dictionary<int, List<HistorialFotoRegistro>> historiales =
                await database.ObtenerHistorialInspeccionAsync(
                    id,
                    cancellationToken);

            var data = new InspeccionOperacionMasivaDto
            {
                TotalSolicitadas = fotografiaIds.Length
            };

            await using var transaccion = await db.Database
                .BeginTransactionAsync(cancellationToken);

            try
            {
                foreach (int fotografiaId in fotografiaIds)
                {
                    DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                        .FirstOrDefault(item =>
                            item.DiagnosticoIAImagenId == fotografiaId);

                    if (imagen == null)
                    {
                        AgregarError(
                            data,
                            fotografiaId,
                            "La fotografía no pertenece a la inspección.");
                        continue;
                    }

                    FotoMetadatos? meta = await database.ObtenerFotoAsync(
                        fotografiaId,
                        cancellationToken);

                    if (meta == null || !meta.Activo || meta.Descartada)
                    {
                        AgregarError(
                            data,
                            fotografiaId,
                            "La fotografía ya no está disponible para recuperación.");
                        continue;
                    }

                    if (!string.Equals(
                            meta.Estado,
                            InspeccionFitosanitariaFlujo.FotoEstados.AnalizandoIA,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AgregarError(
                            data,
                            fotografiaId,
                            "La fotografía ya no se encuentra en estado ANALIZANDO_IA. Actualice el expediente antes de reintentar.");
                        continue;
                    }

                    bool tieneResultadoPersistido = imagen.ResultadoIA != null;

                    if (tieneResultadoPersistido)
                    {
                        const string mensaje =
                            "Se recuperó un resultado IA ya persistido. No se realizó una nueva llamada al proveedor.";

                        await database.CambiarEstadoFotoAsync(
                            fotografiaId,
                            usuarioId.Value,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteDecisionTecnico,
                            "ANALISIS_IA_RECUPERADO",
                            mensaje,
                            fechaAnalisisIAUtc:
                                meta.FechaAnalisisIAUtc ?? DateTime.UtcNow,
                            error: string.Empty,
                            modeloIA: meta.ModeloIAUtilizado,
                            cancellationToken: cancellationToken);

                        await CompletarUltimaRevisionAsync(
                            fotografiaId,
                            "COMPLETADA",
                            string.Empty,
                            cancellationToken);

                        data.Resultados.Add(new InspeccionOperacionItemDto
                        {
                            FotografiaId = fotografiaId,
                            Exitoso = true,
                            Estado = InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteDecisionTecnico,
                            Mensaje = mensaje
                        });
                        data.TotalExitosas++;
                        continue;
                    }

                    DateTime? inicio = ObtenerUltimoInicioIA(
                        historiales.GetValueOrDefault(fotografiaId));

                    if (!inicio.HasValue ||
                        DateTime.UtcNow - inicio.Value < TiempoMinimoInterrupcion)
                    {
                        AgregarError(
                            data,
                            fotografiaId,
                            "El análisis todavía puede estar ejecutándose. Espere unos minutos y actualice antes de forzar su recuperación.");
                        continue;
                    }

                    const string errorInterrumpido =
                        "El análisis IA fue interrumpido antes de guardar un resultado válido. Puede volver a ejecutar el análisis sin perder la fotografía.";

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId.Value,
                        InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                        "ANALISIS_IA_INTERRUMPIDO_RECUPERADO",
                        errorInterrumpido,
                        error: errorInterrumpido,
                        modeloIA: meta.ModeloIAUtilizado,
                        cancellationToken: cancellationToken);

                    await CompletarUltimaRevisionAsync(
                        fotografiaId,
                        "ERROR",
                        errorInterrumpido,
                        cancellationToken);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado = InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                        Mensaje = errorInterrumpido
                    });
                    data.TotalExitosas++;
                }

                await RecalcularEstadoInspeccionAsync(
                    inspeccion,
                    registro.CerradaDefinitiva,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "No fue posible recuperar el análisis IA de la inspección {InspeccionId}.",
                    id);
                throw;
            }

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = data.TotalConError == 0
                    ? "La recuperación del análisis IA se completó correctamente."
                    : data.TotalExitosas > 0
                        ? "La recuperación se completó parcialmente. Revise el detalle de cada fotografía."
                        : "No había fotografías recuperables en este momento.",
                data
            });
        }


        /// <summary>
        /// Instala una defensa adicional en la base de datos. Cuando un resultado
        /// IA ya quedó persistido y la fotografía aún conserva ANALIZANDO_IA, el
        /// mismo commit que guarda el resultado consolida el estado pendiente de
        /// decisión técnica y la revisión vigente. El historial funcional sigue
        /// siendo registrado por el flujo normal; así evitamos duplicarlo cuando
        /// el proceso concluye correctamente y, al mismo tiempo, un cierre abrupto
        /// no deja un resultado visible con estado de procesamiento.
        /// </summary>
        private async Task AsegurarTriggerConsistenciaAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
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
    WHERE UPPER(ISNULL(foto.Estado, N'BORRADOR')) = N'ANALIZANDO_IA'
      AND ISNULL(foto.Activo, 1) = 1
      AND ISNULL(foto.Descartada, 0) = 0;

    UPDATE foto
    SET Estado = N'PENDIENTE_DECISION_TECNICO',
        FechaAnalisisIAUtc = COALESCE(foto.FechaAnalisisIAUtc, SYSUTCDATETIME()),
        ErrorProcesamiento = N''
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
    SET Estado = N'COMPLETADA',
        Error = N'',
        FechaRespuestaUtc = COALESCE(FechaRespuestaUtc, SYSUTCDATETIME())
    FROM dbo.diagnosticoIAImagenRevisionIA revision
    INNER JOIN ultimaRevision ultima
        ON ultima.DiagnosticoIAImagenRevisionIAId =
           revision.DiagnosticoIAImagenRevisionIAId
    WHERE ultima.NumeroFila = 1
      AND UPPER(ISNULL(revision.Estado, N'')) IN
          (N'ANALIZANDO', N'PENDIENTE');
END;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task CompletarUltimaRevisionAsync(
            int fotografiaId,
            string estado,
            string error,
            CancellationToken cancellationToken)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE dbo.diagnosticoIAImagenRevisionIA
SET Estado = {estado},
    Error = {error},
    FechaRespuestaUtc = COALESCE(FechaRespuestaUtc, SYSUTCDATETIME())
WHERE DiagnosticoIAImagenRevisionIAId =
(
    SELECT TOP(1) DiagnosticoIAImagenRevisionIAId
    FROM dbo.diagnosticoIAImagenRevisionIA
    WHERE DiagnosticoIAImagenId = {fotografiaId}
    ORDER BY FechaSolicitudUtc DESC,
             DiagnosticoIAImagenRevisionIAId DESC
);
""", cancellationToken);
        }

        private async Task RecalcularEstadoInspeccionAsync(
            DiagnosticoIA inspeccion,
            bool cerradaDefinitiva,
            CancellationToken cancellationToken)
        {
            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                inspeccion.DiagnosticoIAId,
                cancellationToken);

            string estadoNuevo = InspeccionFitosanitariaFlujo
                .CalcularEstadoInspeccion(
                    fotos.Where(item => item.Activo)
                        .Select(item => item.Estado),
                    cerradaDefinitiva);

            if (string.Equals(
                    inspeccion.Estado,
                    estadoNuevo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string anterior = inspeccion.Estado;
            inspeccion.Estado = estadoNuevo;
            inspeccion.Historial.Add(new DiagnosticoIAHistorial
            {
                UsuarioId = inspeccion.UsuarioSolicitanteId,
                EstadoAnterior = Limitar(anterior, 40),
                EstadoNuevo = Limitar(estadoNuevo, 40),
                Accion = "ESTADO_INSPECCION_RECUPERADO",
                Detalle =
                    "El estado general fue recalculado después de recuperar un análisis IA interrumpido.",
                FechaUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
        }

        private static DateTime? ObtenerUltimoInicioIA(
            IReadOnlyCollection<HistorialFotoRegistro>? historial)
        {
            HistorialFotoRegistro? inicio = (historial ?? [])
                .Where(item => string.Equals(
                    item.Accion,
                    InspeccionFitosanitariaFlujo.Acciones.AnalisisIAIniciado,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.FechaUtc)
                .FirstOrDefault();

            if (inicio == null)
                return null;

            return inicio.FechaUtc.Kind == DateTimeKind.Utc
                ? inicio.FechaUtc
                : DateTime.SpecifyKind(inicio.FechaUtc, DateTimeKind.Utc);
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private static void AgregarError(
            InspeccionOperacionMasivaDto data,
            int fotografiaId,
            string mensaje)
        {
            data.Resultados.Add(new InspeccionOperacionItemDto
            {
                FotografiaId = fotografiaId,
                Exitoso = false,
                Estado = string.Empty,
                Mensaje = mensaje
            });
            data.TotalConError++;
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }

    public sealed class RecuperarAnalisisIARequest
    {
        public List<int> FotografiaIds { get; set; } = [];
    }
}
