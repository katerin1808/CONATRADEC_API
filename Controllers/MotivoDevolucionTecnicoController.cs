using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Catálogo administrable de causas por las que el analizador devuelve una
    /// fotografía al técnico. Los registros históricos conservan una copia del
    /// motivo, por lo que desactivar o editar el catálogo no altera auditorías.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion/motivos-devolucion-tecnico")]
    public sealed partial class MotivoDevolucionTecnicoController : ControllerBase
    {
        private const string InterfazConfiguracion =
            "diagnosticoIAConfiguracionPage";

        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDevolucionDatabase database;

        public MotivoDevolucionTecnicoController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos)
        {
            this.permisos = permisos;
            database = new InspeccionFitosanitariaDevolucionDatabase(db);
        }

        /// <summary>
        /// Selector operativo del analizador. Solo devuelve motivos activos.
        /// </summary>
        [HttpGet("activos")]
        public async Task<IActionResult> ListarActivos(
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return Ok(await database.ListarMotivosAsync(
                false,
                null,
                cancellationToken));
        }

        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] bool incluirInactivos = false,
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return Ok(await database.ListarMotivosAsync(
                incluirInactivos,
                buscar,
                cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            MotivoDevolucionTecnicoRespuesta? item =
                await database.ObtenerMotivoAsync(id, cancellationToken);

            return item == null
                ? NotFound(Error("El motivo indicado no existe."))
                : Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] MotivoDevolucionTecnicoCrearRequest? request,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del motivo."));

            Normalizar(request);
            IActionResult? validacion = Validar(request);
            if (validacion != null)
                return validacion;

            List<MotivoDevolucionTecnicoRespuesta> existentes =
                await database.ListarMotivosAsync(true, null, cancellationToken);

            MotivoDevolucionTecnicoRespuesta? mismoCodigo = existentes
                .FirstOrDefault(item => string.Equals(
                    item.Codigo,
                    request.Codigo,
                    StringComparison.OrdinalIgnoreCase));

            if (mismoCodigo?.Activo == true)
                return Conflict(Error("Ya existe un motivo activo con ese código."));

            if (ExisteNombreActivo(existentes, request.Nombre, mismoCodigo?.MotivoDevolucionTecnicoId))
                return Conflict(Error("Ya existe un motivo activo con ese nombre."));

            int usuarioId = ObtenerUsuarioIdRequerido();

            if (mismoCodigo != null)
            {
                await database.ActualizarMotivoAsync(
                    mismoCodigo.MotivoDevolucionTecnicoId,
                    request,
                    usuarioId,
                    cancellationToken);
                await database.CambiarEstadoMotivoAsync(
                    mismoCodigo.MotivoDevolucionTecnicoId,
                    true,
                    usuarioId,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Motivo reactivado correctamente.",
                    data = await database.ObtenerMotivoAsync(
                        mismoCodigo.MotivoDevolucionTecnicoId,
                        cancellationToken)
                });
            }

            int id = await database.CrearMotivoAsync(
                request,
                usuarioId,
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    success = true,
                    message = "Motivo creado correctamente.",
                    data = await database.ObtenerMotivoAsync(id, cancellationToken)
                });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] MotivoDevolucionTecnicoActualizarRequest? request,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del motivo."));

            MotivoDevolucionTecnicoRespuesta? actual =
                await database.ObtenerMotivoAsync(id, cancellationToken);

            if (actual == null)
                return NotFound(Error("El motivo indicado no existe."));

            if (!actual.Activo)
                return Conflict(Error("Recupere el motivo antes de editarlo."));

            Normalizar(request);
            IActionResult? validacion = Validar(request);
            if (validacion != null)
                return validacion;

            if (!string.Equals(
                    actual.Codigo,
                    request.Codigo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(Error(
                    "El código no puede modificarse porque puede estar asociado a devoluciones históricas."));
            }

            List<MotivoDevolucionTecnicoRespuesta> existentes =
                await database.ListarMotivosAsync(true, null, cancellationToken);

            if (ExisteNombreActivo(existentes, request.Nombre, id))
                return Conflict(Error("Ya existe otro motivo activo con ese nombre."));

            await database.ActualizarMotivoAsync(
                id,
                request,
                ObtenerUsuarioIdRequerido(),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivo actualizado correctamente.",
                data = await database.ObtenerMotivoAsync(id, cancellationToken)
            });
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            MotivoDevolucionTecnicoRespuesta? item =
                await database.ObtenerMotivoAsync(id, cancellationToken);

            if (item == null || !item.Activo)
                return NotFound(Error("El motivo no existe o ya está inactivo."));

            await database.CambiarEstadoMotivoAsync(
                id,
                false,
                ObtenerUsuarioIdRequerido(),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivo desactivado correctamente."
            });
        }

        [HttpPut("{id:int}/recuperar")]
        public async Task<IActionResult> Recuperar(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            MotivoDevolucionTecnicoRespuesta? item =
                await database.ObtenerMotivoAsync(id, cancellationToken);

            if (item == null)
                return NotFound(Error("El motivo indicado no existe."));

            if (item.Activo)
            {
                return Ok(new
                {
                    success = true,
                    message = "El motivo ya se encuentra activo."
                });
            }

            List<MotivoDevolucionTecnicoRespuesta> existentes =
                await database.ListarMotivosAsync(true, null, cancellationToken);

            if (ExisteNombreActivo(existentes, item.Nombre, item.MotivoDevolucionTecnicoId))
                return Conflict(Error("Existe otro motivo activo con el mismo nombre."));

            await database.CambiarEstadoMotivoAsync(
                id,
                true,
                ObtenerUsuarioIdRequerido(),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivo recuperado correctamente."
            });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                ObtenerUsuarioId(),
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

        private IActionResult? Validar(
            MotivoDevolucionTecnicoGuardarRequest request)
        {
            if (!CodigoRegex().IsMatch(request.Codigo))
            {
                return BadRequest(Error(
                    "El código debe contener entre 3 y 60 caracteres: letras mayúsculas, números o guion bajo."));
            }

            if (request.Nombre.Length is < 3 or > 140)
                return BadRequest(Error("El nombre debe contener entre 3 y 140 caracteres."));

            if (request.Descripcion.Length > 700)
                return BadRequest(Error("La descripción no puede superar 700 caracteres."));

            if (request.InstruccionSugerida.Length is < 8 or > 2000)
            {
                return BadRequest(Error(
                    "La instrucción sugerida debe contener entre 8 y 2000 caracteres."));
            }

            if (request.Orden is < 1 or > 999)
                return BadRequest(Error("El orden debe estar entre 1 y 999."));

            if (request.RequiereNuevaFotografia ==
                request.PermiteCorregirMetadatos)
            {
                return BadRequest(Error(
                    "Seleccione exactamente una forma de resolución: solicitar una nueva fotografía o permitir que el técnico corrija los metadatos de la evidencia actual."));
            }

            return null;
        }

        private static void Normalizar(
            MotivoDevolucionTecnicoGuardarRequest request)
        {
            request.Codigo = (request.Codigo ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_');
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Descripcion = (request.Descripcion ?? string.Empty).Trim();
            request.InstruccionSugerida =
                (request.InstruccionSugerida ?? string.Empty).Trim();
        }

        private static bool ExisteNombreActivo(
            IEnumerable<MotivoDevolucionTecnicoRespuesta> items,
            string nombre,
            int? excluirId) =>
            items.Any(item =>
                item.Activo &&
                item.MotivoDevolucionTecnicoId != excluirId &&
                string.Equals(
                    item.Nombre,
                    nombre,
                    StringComparison.OrdinalIgnoreCase));

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

        private int ObtenerUsuarioIdRequerido() =>
            ObtenerUsuarioId() ?? throw new UnauthorizedAccessException(
                "No se pudo identificar al usuario autenticado.");

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };

        [GeneratedRegex("^[A-Z0-9_]{3,60}$")]
        private static partial Regex CodigoRegex();
    }
}
