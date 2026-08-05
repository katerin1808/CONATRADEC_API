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
        /// Realiza el cierre global e irreversible. El cierre no representa el
        /// envío al analizador: cada fotografía ya debió recorrer y finalizar
        /// su expediente independiente antes de llegar a este punto.
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

            if (registro.CerradaTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La inspección ya está cerrada definitivamente."
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
                        "No puede cerrar la inspección hasta que todas las fotografías finalicen su proceso independiente. " +
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
                    cerradaTecnico = true,
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
