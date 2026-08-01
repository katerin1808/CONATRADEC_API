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
    /// Permite recuperar propietarios eliminados lógicamente.
    ///
    /// La eliminación de propietarios conserva el registro y solamente cambia
    /// activo a false. Este endpoint realiza la operación inversa y mantiene el
    /// historial, las relaciones antiguas y la identificación original.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/parametrizacion-acceso/propietarios")]
    public sealed class PropietariosRecuperarController :
        ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public PropietariosRecuperarController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpPost("{propietarioId:int}/recuperar")]
        public async Task<IActionResult> Recuperar(
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

            /*
             * Recuperar cambia el estado del propietario, por lo que utiliza el
             * permiso Actualizar del módulo Propietarios.
             */
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    ParametrizacionAccesoDatabaseInitializer
                        .Propietarios,
                    TipoPermisoApi.Actualizar,
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
                        item =>
                            item.propietarioId ==
                                propietarioId,
                        cancellationToken);

            if (propietario == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el propietario."
                });
            }

            if (propietario.activo)
            {
                return Ok(new
                {
                    success = true,
                    message =
                        "El propietario ya se encuentra activo."
                });
            }

            propietario.activo = true;
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
                    "Propietario recuperado correctamente."
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
