using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/bitacora")]
    public sealed class BitacoraController : ControllerBase
    {
        private readonly BitacoraDbContext bitacoraDb;
        private readonly DBContext db;

        public BitacoraController(
            BitacoraDbContext bitacoraDb,
            DBContext db)
        {
            this.bitacoraDb = bitacoraDb;
            this.db = db;
        }

        [HttpGet]
        public async Task<ActionResult<BitacoraPaginadaDto>> Listar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] DateTime? fechaDesdeUtc,
            [FromQuery] DateTime? fechaHastaUtc,
            [FromQuery] int? usuarioId,
            [FromQuery] string? accion,
            [FromQuery] string? modulo,
            [FromQuery] bool? exitoso,
            [FromQuery] string? buscar,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 25,
            CancellationToken cancellationToken = default)
        {
            ActionResult? resultadoAcceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (resultadoAcceso != null)
                return resultadoAcceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

            IQueryable<Bitacora> query = bitacoraDb.Bitacoras
                .AsNoTracking();

            if (fechaDesdeUtc.HasValue)
                query = query.Where(x => x.fechaHoraUtc >= fechaDesdeUtc.Value);

            if (fechaHastaUtc.HasValue)
                query = query.Where(x => x.fechaHoraUtc <= fechaHastaUtc.Value);

            if (usuarioId.HasValue)
                query = query.Where(x => x.usuarioId == usuarioId.Value);

            if (!string.IsNullOrWhiteSpace(accion))
            {
                string valor = accion.Trim();
                query = query.Where(x => x.accion == valor);
            }

            if (!string.IsNullOrWhiteSpace(modulo))
            {
                string valor = modulo.Trim();
                query = query.Where(x => x.modulo == valor);
            }

            if (exitoso.HasValue)
                query = query.Where(x => x.exitoso == exitoso.Value);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();
                query = query.Where(x =>
                    x.usuarioNombre.Contains(texto) ||
                    x.descripcion.Contains(texto) ||
                    x.endpoint.Contains(texto) ||
                    x.paginaOrigen.Contains(texto) ||
                    x.correlationId.Contains(texto));
            }

            int total = await query.CountAsync(cancellationToken);

            List<BitacoraListadoDto> items = await query
                .OrderByDescending(x => x.fechaHoraUtc)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(x => new BitacoraListadoDto
                {
                    BitacoraId = x.bitacoraId,
                    FechaHoraUtc = x.fechaHoraUtc,
                    UsuarioId = x.usuarioId,
                    UsuarioNombre = x.usuarioNombre,
                    RolNombre = x.rolNombre,
                    Modulo = x.modulo,
                    Accion = x.accion,
                    MetodoHttp = x.metodoHttp,
                    Endpoint = x.endpoint,
                    PaginaOrigen = x.paginaOrigen,
                    Descripcion = x.descripcion,
                    CodigoEstado = x.codigoEstado,
                    Exitoso = x.exitoso,
                    DuracionMs = x.duracionMs,
                    CantidadCambios = x.detalles.Count
                })
                .ToListAsync(cancellationToken);

            int totalPaginas = total == 0
                ? 1
                : (int)Math.Ceiling(total / (double)tamanoPagina);

            return Ok(new BitacoraPaginadaDto
            {
                Items = items,
                Pagina = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = total,
                TotalPaginas = totalPaginas
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BitacoraDetalleDto>> Obtener(
            Guid id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? resultadoAcceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (resultadoAcceso != null)
                return resultadoAcceso;

            BitacoraDetalleDto? item = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(x => x.bitacoraId == id)
                .Select(x => new BitacoraDetalleDto
                {
                    BitacoraId = x.bitacoraId,
                    FechaHoraUtc = x.fechaHoraUtc,
                    UsuarioId = x.usuarioId,
                    UsuarioNombre = x.usuarioNombre,
                    RolNombre = x.rolNombre,
                    Modulo = x.modulo,
                    Accion = x.accion,
                    MetodoHttp = x.metodoHttp,
                    Endpoint = x.endpoint,
                    PaginaOrigen = x.paginaOrigen,
                    Descripcion = x.descripcion,
                    Parametros = x.parametros,
                    DireccionIp = x.direccionIp,
                    Dispositivo = x.dispositivo,
                    Plataforma = x.plataforma,
                    VersionApp = x.versionApp,
                    CorrelationId = x.correlationId,
                    CodigoEstado = x.codigoEstado,
                    Exitoso = x.exitoso,
                    DuracionMs = x.duracionMs,
                    Error = x.error,
                    CantidadCambios = x.detalles.Count,
                    Cambios = x.detalles
                        .OrderBy(y => y.bitacoraDetalleId)
                        .Select(y => new BitacoraCambioDto
                        {
                            BitacoraDetalleId = y.bitacoraDetalleId,
                            FechaHoraUtc = y.fechaHoraUtc,
                            Entidad = y.entidad,
                            EntidadId = y.entidadId,
                            Operacion = y.operacion,
                            ValoresAnteriores = y.valoresAnteriores,
                            ValoresNuevos = y.valoresNuevos,
                            PropiedadesModificadas = y.propiedadesModificadas
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (item == null)
            {
                return NotFound(new
                {
                    mensaje = "El registro de bitácora no existe."
                });
            }

            return Ok(item);
        }

        [HttpGet("catalogos")]
        public async Task<ActionResult<BitacoraCatalogosDto>> Catalogos(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? resultadoAcceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (resultadoAcceso != null)
                return resultadoAcceso;

            List<string> acciones = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(x => x.accion != "")
                .Select(x => x.accion)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);

            List<string> modulos = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(x => x.modulo != "")
                .Select(x => x.modulo)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);

            List<BitacoraUsuarioFiltroDto> usuarios = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(x => x.usuarioId != null || x.usuarioNombre != "")
                .GroupBy(x => new { x.usuarioId, x.usuarioNombre })
                .Select(x => new BitacoraUsuarioFiltroDto
                {
                    UsuarioId = x.Key.usuarioId,
                    Nombre = x.Key.usuarioNombre
                })
                .OrderBy(x => x.Nombre)
                .ToListAsync(cancellationToken);

            return Ok(new BitacoraCatalogosDto
            {
                Acciones = acciones,
                Modulos = modulos,
                Usuarios = usuarios
            });
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                return StatusCode(
                    StatusCodes.Status401Unauthorized,
                    new
                    {
                        mensaje =
                            "Debe enviar el encabezado X-Usuario-Id " +
                            "con el identificador del usuario que realiza " +
                            "la consulta."
                    });
            }

            bool usuarioActivo = await db.Usuarios
                .AsNoTracking()
                .AnyAsync(
                    x => x.UsuarioId == usuarioId.Value && x.activo,
                    cancellationToken);

            if (!usuarioActivo)
            {
                return StatusCode(
                    StatusCodes.Status401Unauthorized,
                    new
                    {
                        mensaje =
                            "El usuario enviado en X-Usuario-Id no existe " +
                            "o está inactivo."
                    });
            }

            bool tienePermiso = await (
                from usuario in db.Usuarios.AsNoTracking()
                join permiso in db.RolInterfaz.AsNoTracking()
                    on usuario.rolId equals permiso.rolId
                join interfaz in db.Interfaz.AsNoTracking()
                    on permiso.interfazId equals interfaz.interfazId
                where usuario.UsuarioId == usuarioId.Value
                      && usuario.activo
                      && interfaz.activo
                      && interfaz.nombreInterfaz == "bitacoraPage"
                      && permiso.leer == true
                select usuario.UsuarioId)
                .AnyAsync(cancellationToken);

            if (!tienePermiso)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        mensaje =
                            "El usuario no tiene permiso de lectura para " +
                            "consultar la bitácora."
                    });
            }

            return null;
        }
    }
}