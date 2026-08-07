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
    /// Envía al aprobador fotografías que ya cuentan con una revisión humana.
    /// El envío puede realizarse de forma individual o por selección, sin obligar
    /// a esperar a que todas las fotografías de la inspección estén terminadas.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/revision-fitosanitaria")]
    public sealed class InspeccionFitosanitariaEnvioAprobadorController :
        ControllerBase
    {
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaDevolucionDatabase revisionDatabase;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly ILogger<InspeccionFitosanitariaEnvioAprobadorController>
            logger;

        public InspeccionFitosanitariaEnvioAprobadorController(
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control,
            ILogger<InspeccionFitosanitariaEnvioAprobadorController> logger)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.control = control;
            this.logger = logger;

            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
            revisionDatabase =
                new InspeccionFitosanitariaDevolucionDatabase(diagnosticoDb);
            asignaciones =
                new InspeccionFitosanitariaAsignacionDatabase(diagnosticoDb);
        }

        [HttpPost("{id:int}/enviar-aprobador")]
        public async Task<IActionResult> EnviarAprobador(
            int id,
            [FromBody] InspeccionFotosSeleccionadasRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
                return BadRequest(Error("La inspección indicada no es válida."));

            List<int> fotografiaIds = request?.FotografiaIds?
                .Where(item => item > 0)
                .Distinct()
                .ToList() ?? [];

            if (fotografiaIds.Count == 0)
            {
                return BadRequest(Error(
                    "Seleccione al menos una fotografía revisada para enviar al aprobador."));
            }

            if (fotografiaIds.Count > 100)
            {
                return BadRequest(Error(
                    "Puede enviar como máximo 100 fotografías por operación."));
            }

            await InicializarFlujoAsync(cancellationToken);

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NotFound(Error("No se encontró la inspección indicada."));

            if (registro.CerradaDefinitiva)
            {
                return Conflict(Error(
                    "La inspección está cerrada definitivamente y no admite nuevos envíos."));
            }

            if (!registro.EtapaTecnicaFinalizada)
            {
                return Conflict(Error(
                    "El técnico debe finalizar su etapa antes de que una fotografía pueda enviarse al aprobador."));
            }

            ContextoRevisionAnalizadorDto contextoActual =
                await revisionDatabase.ObtenerContextoAsync(
                    id,
                    cancellationToken);

            if (contextoActual.Resumen.EtapaAnalizadorFinalizada)
            {
                return Conflict(Error(
                    "La etapa del analizador ya fue finalizada. Solo una devolución del aprobador puede reabrirla."));
            }

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                ResultadoAsignacionFlujo asignacionAnalizador =
                    await asignaciones.TomarAnalizadorAsync(
                        id,
                        usuarioId!.Value,
                        cancellationToken);

                if (!asignacionAnalizador.Exitoso)
                {
                    await transaccion.RollbackAsync(cancellationToken);
                    return Conflict(Error(asignacionAnalizador.Mensaje));
                }

                DiagnosticoIA? inspeccion = await diagnosticoDb.Diagnosticos
                    .FirstOrDefaultAsync(
                        item =>
                            item.DiagnosticoIAId == id &&
                            item.Activo,
                        cancellationToken);

                if (inspeccion == null)
                {
                    await transaccion.RollbackAsync(cancellationToken);
                    return NotFound(Error("No se encontró la inspección indicada."));
                }

                List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                    id,
                    cancellationToken);

                var preparadas = new List<
                    (FotoMetadatos Foto, AnalisisHumanoRegistro Analisis)>();

                foreach (int fotografiaId in fotografiaIds)
                {
                    FotoMetadatos? foto = fotos.FirstOrDefault(item =>
                        item.FotografiaId == fotografiaId);

                    if (foto == null || foto.DiagnosticoId != id)
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return BadRequest(Error(
                            $"La fotografía #{fotografiaId} no pertenece a la inspección."));
                    }

                    if (!foto.Activo || foto.Descartada)
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return Conflict(Error(
                            $"La fotografía #{fotografiaId} ya no está disponible para el flujo de revisión."));
                    }

                    if (!string.Equals(
                            foto.Estado,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .EnAnalisisHumano,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return Conflict(Error(
                            $"La fotografía #{fotografiaId} debe estar revisada y pendiente de envío antes de pasar al aprobador."));
                    }

                    AnalisisHumanoRegistro? analisis =
                        await database.ObtenerUltimoAnalisisHumanoAsync(
                            fotografiaId,
                            cancellationToken);

                    if (analisis == null ||
                        string.IsNullOrWhiteSpace(analisis.Diagnostico))
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return Conflict(Error(
                            $"La fotografía #{fotografiaId} no tiene una clasificación humana lista para enviar."));
                    }

                    if (!string.Equals(
                            analisis.EstadoRegistro,
                            "BORRADOR",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return Conflict(Error(
                            $"La revisión humana de la fotografía #{fotografiaId} ya fue enviada o no se encuentra en estado de borrador."));
                    }

                    if (analisis.UsuarioAnalizadorId != usuarioId.Value)
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return Conflict(Error(
                            $"La fotografía #{fotografiaId} fue revisada por otro analizador y no puede enviarse con esta sesión."));
                    }

                    preparadas.Add((foto, analisis));
                }

                DateTime ahora = DateTime.UtcNow;

                foreach ((FotoMetadatos foto, AnalisisHumanoRegistro analisis)
                         in preparadas)
                {
                    await database.GuardarAnalisisHumanoAsync(
                        foto.FotografiaId,
                        usuarioId.Value,
                        analisis.CalidadEvaluacion,
                        analisis.EstadoGeneral,
                        analisis.CategoriaPrincipal,
                        analisis.CategoriasSecundariasJson,
                        analisis.Diagnostico,
                        analisis.TipoDiagnostico,
                        analisis.Severidad,
                        analisis.NivelCerteza,
                        analisis.Observaciones,
                        enviar: true,
                        cancellationToken: cancellationToken);

                    await database.CambiarEstadoFotoAsync(
                        foto.FotografiaId,
                        usuarioId.Value,
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteAprobacion,
                        InspeccionFitosanitariaFlujo.Acciones
                            .AnalisisHumanoEnviado,
                        "El analizador envió la fotografía al aprobador.",
                        fechaAnalisisHumanoUtc: ahora,
                        cancellationToken: cancellationToken);
                }

                inspeccion.Estado =
                    InspeccionFitosanitariaFlujo.InspeccionEstados
                        .PendienteAprobacion;

                await diagnosticoDb.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "Error al enviar fotografías al aprobador para la inspección {InspeccionId}.",
                    id);
                throw;
            }

            await IntentarFinalizarEtapaAnalizadorAsync(
                id,
                usuarioId!.Value,
                cancellationToken);

            ContextoRevisionAnalizadorDto contexto =
                await revisionDatabase.ObtenerContextoAsync(
                    id,
                    cancellationToken);

            string mensaje = fotografiaIds.Count == 1
                ? "La fotografía fue enviada al aprobador correctamente."
                : $"Las {fotografiaIds.Count} fotografías fueron enviadas al aprobador correctamente.";

            return Ok(new
            {
                success = true,
                message = mensaje,
                data = contexto
            });
        }

        private async Task IntentarFinalizarEtapaAnalizadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                inspeccionId,
                cancellationToken);

            bool quedanFotografiasAnalizador = fotos.Any(item =>
                item.Activo &&
                !item.Descartada &&
                NormalizarEstado(item.Estado) is
                    InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador or
                    InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano or
                    InspeccionFitosanitariaFlujo.FotoEstados.DevueltaAnalizador);

            if (quedanFotografiasAnalizador)
                return;

            ContextoRevisionAnalizadorDto contexto =
                await revisionDatabase.ObtenerContextoAsync(
                    inspeccionId,
                    cancellationToken);

            if (contexto.Resumen.EtapaAnalizadorFinalizada ||
                !contexto.Resumen.PuedeFinalizarRevision)
            {
                return;
            }

            (bool exitoso, string mensaje) =
                await revisionDatabase.FinalizarAnalizadorAsync(
                    inspeccionId,
                    usuarioId,
                    cancellationToken);

            if (!exitoso)
            {
                logger.LogWarning(
                    "Las fotografías fueron enviadas, pero no fue posible marcar automáticamente como finalizada la etapa del analizador para la inspección {InspeccionId}: {Mensaje}",
                    inspeccionId,
                    mensaje);
            }
        }

        private async Task InicializarFlujoAsync(
            CancellationToken cancellationToken)
        {
            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await revisionDatabase.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            if (permiso.Permitido)
                return null;

            return StatusCode(
                permiso.CodigoEstado,
                new
                {
                    success = false,
                    message = permiso.Mensaje
                });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static string NormalizarEstado(string? estado) =>
            (estado ?? string.Empty).Trim().ToUpperInvariant();

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}