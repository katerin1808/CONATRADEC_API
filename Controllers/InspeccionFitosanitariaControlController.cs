using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Control explícito de la etapa técnica y del cierre definitivo. El cierre
    /// final está reservado al aprobador y nunca puede ejecutarlo el técnico
    /// propietario por el solo hecho de haber creado la inspección.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaControlController : ControllerBase
    {
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly PermisoApiService permisos;

        public InspeccionFitosanitariaControlController(
            DiagnosticoIADbContext db,
            InspeccionFitosanitariaControlDatabaseInitializer control,
            PermisoApiService permisos)
        {
            this.control = control;
            this.permisos = permisos;
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(db);
        }

        [HttpPost("{id:int}/finalizar-etapa-tecnica")]
        public async Task<IActionResult> CerrarEtapaTecnica(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (registro.CerradaDefinitiva)
            {
                return Conflict(Error(
                    "La inspección ya está cerrada definitivamente."));
            }

            if (registro.EtapaTecnicaFinalizada)
            {
                return Conflict(Error(
                    "La etapa técnica ya fue finalizada y la inspección se encuentra en revisión."));
            }

            if (registro.UsuarioSolicitanteId != usuarioId.Value)
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

            InspeccionFitosanitariaEstadoEtapaTecnica estado =
                await control.ObtenerEstadoEtapaTecnicaAsync(
                    id,
                    cancellationToken);

            if (!estado.ListaParaCerrar)
            {
                string detalle = estado.TotalActivas == 0
                    ? "La inspección no contiene fotografías activas."
                    : estado.TotalEnviadasRevision == 0
                        ? "Debe enviar al menos una fotografía a revisión."
                        : estado.TotalProcesando > 0
                            ? $"Hay {estado.TotalProcesando} fotografía(s) procesándose."
                            : $"Todavía existen {estado.TotalNoPreparadas} fotografía(s) que deben enviarse a revisión o descartarse.";

                return Conflict(new
                {
                    success = false,
                    message =
                        "No puede finalizar la etapa técnica todavía. " + detalle,
                    data = new
                    {
                        estado.TotalActivas,
                        estado.TotalEnviadasRevision,
                        estado.TotalDescartadas,
                        estado.TotalProcesando,
                        estado.TotalNoPreparadas
                    }
                });
            }

            bool finalizada = await control.CerrarEtapaTecnicaAsync(
                id,
                usuarioId.Value,
                cancellationToken);

            if (!finalizada)
            {
                return Conflict(Error(
                    "La inspección cambió mientras se intentaba finalizar la etapa técnica. Actualice e intente nuevamente."));
            }

            return Ok(new
            {
                success = true,
                message =
                    "La etapa técnica fue finalizada y la inspección quedó disponible para el analizador.",
                data = new
                {
                    inspeccionId = id,
                    etapaTecnicaFinalizada = true,
                    fechaFinEtapaTecnicaUtc = DateTime.UtcNow,
                    estado.TotalActivas,
                    estado.TotalEnviadasRevision,
                    estado.TotalDescartadas
                }
            });
        }

        /// <summary>
        /// Cierre irreversible reservado al aprobador asignado. El expediente
        /// solo se sella cuando todas las fotografías activas ya tienen un
        /// estado final independiente.
        /// </summary>
        [HttpPost("{id:int}/cerrar-definitivo")]
        public async Task<IActionResult> CerrarDefinitivamente(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(
                        "Solo un aprobador autorizado puede cerrar definitivamente una inspección."));
            }

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (registro.CerradaDefinitiva)
            {
                return Conflict(Error(
                    "La inspección ya está cerrada definitivamente."));
            }

            if (!registro.EtapaTecnicaFinalizada)
            {
                return Conflict(Error(
                    "Primero debe finalizarse la etapa técnica y completarse el flujo de revisión."));
            }

            InspeccionFitosanitariaEstadoCierre estado =
                await control.ObtenerEstadoCierreAsync(
                    id,
                    cancellationToken);

            if (!estado.TodasFinalizadas)
            {
                string detalle = estado.TotalActivas == 0
                    ? "La inspección no contiene fotografías activas."
                    : estado.TotalProcesando > 0
                        ? $"Hay {estado.TotalProcesando} fotografía(s) procesándose y {estado.TotalPendientes} pendiente(s)."
                        : $"Todavía existen {estado.TotalPendientes} fotografía(s) sin finalizar.";

                return Conflict(new
                {
                    success = false,
                    message =
                        "No puede cerrar definitivamente la inspección hasta que todas las fotografías finalicen su proceso independiente. " +
                        detalle,
                    data = new
                    {
                        estado.TotalActivas,
                        estado.TotalFinalizadas,
                        estado.TotalProcesando,
                        estado.TotalPendientes
                    }
                });
            }

            ResultadoAsignacionFlujo asignacion =
                await asignaciones.TomarAprobadorAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken);

            if (!asignacion.Exitoso)
                return Conflict(Error(asignacion.Mensaje));

            bool cerrada = await control.CerrarDefinitivamenteAsync(
                id,
                usuarioId.Value,
                cancellationToken);

            if (!cerrada)
            {
                return Conflict(Error(
                    "La inspección cambió mientras se intentaba cerrar. Actualice e intente nuevamente."));
            }

            return Ok(new
            {
                success = true,
                message =
                    "La inspección fue cerrada definitivamente y quedó en modo de solo lectura. Las fotografías autorizadas todavía pueden copiarse al Álbum Botánico sin alterar el expediente.",
                data = new
                {
                    inspeccionId = id,
                    cerradaDefinitiva = true,
                    fechaCierreDefinitivoUtc = DateTime.UtcNow,
                    estado.TotalActivas,
                    estado.TotalFinalizadas
                }
            });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                            User.FindFirstValue("usuarioId") ??
                            User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private IActionResult NoEncontrado() =>
            NotFound(Error("No se encontró la inspección indicada."));

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
