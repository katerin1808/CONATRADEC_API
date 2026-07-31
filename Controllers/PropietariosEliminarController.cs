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
    /// Completa el CRUD de propietarios con eliminación lógica.
    ///
    /// Los endpoints de lectura, creación y actualización continúan en
    /// ParametrizacionAccesoController. Este controlador agrega únicamente
    /// DELETE para evitar duplicar rutas existentes.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/parametrizacion-acceso/propietarios")]
    public sealed class PropietariosEliminarController :
        ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public PropietariosEliminarController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpDelete("{propietarioId:int}")]
        public async Task<IActionResult> Eliminar(
            int propietarioId,
            CancellationToken cancellationToken = default)
        {
            if (propietarioId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del propietario no es válido."
                });
            }

            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    ParametrizacionAccesoDatabaseInitializer
                        .Propietarios,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            Propietario? propietario =
                await db.Propietarios
                    .FirstOrDefaultAsync(
                        x =>
                            x.propietarioId ==
                                propietarioId &&
                            x.activo,
                        cancellationToken);

            if (propietario == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el propietario o ya fue eliminado."
                });
            }

            bool tieneTerrenosVinculados =
                await db.PropietarioTerrenos
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.propietarioId ==
                                propietarioId &&
                            x.activo,
                        cancellationToken);

            if (tieneTerrenosVinculados)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el propietario porque tiene " +
                        "terrenos vinculados. Reasigne los terrenos a otro " +
                        "propietario antes de continuar."
                });
            }

            propietario.activo = false;
            propietario.fechaActualizacionUtc =
                DateTime.UtcNow;
            propietario.usuarioActualizacionId =
                ObtenerUsuarioId();

            await db.SaveChangesAsync(
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Propietario eliminado correctamente."
            });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                    valor,
                    out int usuarioId) &&
                usuarioId > 0
                    ? usuarioId
                    : null;
        }
    }
}
