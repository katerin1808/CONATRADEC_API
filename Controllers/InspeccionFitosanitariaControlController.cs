using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaControlController : ControllerBase
    {
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly PermisoApiService permisos;

        public InspeccionFitosanitariaControlController(
            InspeccionFitosanitariaControlDatabaseInitializer control,
            PermisoApiService permisos)
        {
            this.control = control;
            this.permisos = permisos;
        }

        /// <summary>
        /// Finaliza la etapa del técnico y habilita la inspección para el
        /// analizador. No representa el cierre definitivo del expediente.
        /// </summary>
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
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró la inspección indicada."
                });
            }

            if (registro.CerradaDefinitiva || registro.CerradaTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La inspección ya está cerrada definitivamente."
                });
            }

            if (registro.EtapaTecnicaFinalizada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La etapa técnica ya fue finalizada y la inspección se encuentra en revisión."
                });
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
                    new { success = false, message = permiso.Mensaje });
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

            bool cerrada = await control.CerrarEtapaTecnicaAsync(
                id,
                usuarioId.Value,
                cancellationToken);

            if (!cerrada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La inspección cambió mientras se intentaba finalizar la etapa técnica. Actualice e intente nuevamente."
                });
            }

            return Ok(new
            {
                success = true,
                message =
                    "La etapa técnica fue finalizada y la inspección quedó disponible para el analizador.",
                data = new
                {
                    inspeccionId = id,
                    cerradaTecnico = true,
                    estado.TotalActivas,
                    estado.TotalEnviadasRevision,
                    estado.TotalDescartadas
                }
            });
        }

        /// <summary>
        /// Realiza el cierre global e irreversible después de que todas las
        /// fotografías terminaron el análisis, la aprobación o el descarte.
        /// </summary>
        [HttpPost("{id:int}/cerrar-definitivo")]
        public async Task<IActionResult> CerrarDefinitivamente(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró la inspección indicada."
                });
            }

            if (registro.CerradaDefinitiva || registro.CerradaTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La inspección ya está cerrada definitivamente."
                });
            }

            if (!registro.EtapaTecnicaFinalizada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Primero debe finalizarse la etapa técnica y completarse el flujo de revisión."
                });
            }

            bool esTecnicoPropietario =
                registro.UsuarioSolicitanteId == usuarioId.Value;

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                esTecnicoPropietario
                    ? DiagnosticoIAFlujo.InterfazSolicitud
                    : DiagnosticoIAFlujo.InterfazAprobador,
                esTecnicoPropietario
                    ? TipoPermisoApi.Agregar
                    : TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new { success = false, message = permiso.Mensaje });
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

            bool cerrada = await control.CerrarDefinitivamenteAsync(
                id,
                usuarioId.Value,
                cancellationToken);

            if (!cerrada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La inspección cambió mientras se intentaba cerrar. Actualice e intente nuevamente."
                });
            }

            return Ok(new
            {
                success = true,
                message =
                    "La inspección fue cerrada definitivamente y quedó en modo de solo lectura.",
                data = new
                {
                    inspeccionId = id,
                    cerradaDefinitiva = true,
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
    }
}
