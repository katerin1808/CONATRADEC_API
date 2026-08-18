using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API auditada de consulta de bitácora.
    ///
    /// Se mantiene separada del controlador histórico para no romper clientes
    /// anteriores. Esta versión usa autenticación real, permisos del usuario
    /// autenticado y un corte temporal de consulta para estabilizar la
    /// paginación aunque sigan entrando nuevos registros de auditoría.
    /// </summary>
    [ApiController]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("api/bitacora/v2")]
    public sealed class BitacoraV2Controller : ControllerBase
    {
        private const string InterfazBitacora = "bitacoraPage";
        private const int TamanoBusquedaMaximo = 200;

        private readonly BitacoraDbContext bitacoraDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public BitacoraV2Controller(
            BitacoraDbContext bitacoraDb,
            DBContext db,
            PermisoApiService permisos)
        {
            this.bitacoraDb = bitacoraDb;
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] DateTime? fechaDesdeUtc,
            [FromQuery] DateTime? fechaHastaUtc,
            [FromQuery] int? usuarioId,
            [FromQuery] string? accion,
            [FromQuery] string? modulo,
            [FromQuery] bool? exitoso,
            [FromQuery] string? buscar,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 25,
            [FromQuery] DateTime? corteConsultaUtc = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DateTime? desde = NormalizarUtcNullable(fechaDesdeUtc);
            DateTime? hasta = NormalizarUtcNullable(fechaHastaUtc);

            if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            {
                return BadRequest(Error(
                    "La fecha inicial no puede ser posterior a la fecha final."));
            }

            string textoBusqueda = (buscar ?? string.Empty).Trim();
            if (textoBusqueda.Length > TamanoBusquedaMaximo)
            {
                return BadRequest(Error(
                    $"La búsqueda no puede superar {TamanoBusquedaMaximo} caracteres."));
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime corte = corteConsultaUtc.HasValue
                ? NormalizarUtc(corteConsultaUtc.Value)
                : ahoraUtc;

            if (corte > ahoraUtc)
                corte = ahoraUtc;

            IQueryable<Bitacora> query = bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(item => item.fechaHoraUtc <= corte);

            if (desde.HasValue)
                query = query.Where(item => item.fechaHoraUtc >= desde.Value);

            if (hasta.HasValue)
                query = query.Where(item => item.fechaHoraUtc <= hasta.Value);

            if (usuarioId.HasValue)
                query = query.Where(item => item.usuarioId == usuarioId.Value);

            if (!string.IsNullOrWhiteSpace(accion))
            {
                string valor = accion.Trim();
                query = query.Where(item => item.accion == valor);
            }

            if (!string.IsNullOrWhiteSpace(modulo))
            {
                string valor = modulo.Trim();
                query = query.Where(item => item.modulo == valor);
            }

            if (exitoso.HasValue)
                query = query.Where(item => item.exitoso == exitoso.Value);

            if (!string.IsNullOrWhiteSpace(textoBusqueda))
            {
                query = query.Where(item =>
                    item.usuarioNombre.Contains(textoBusqueda) ||
                    item.descripcion.Contains(textoBusqueda) ||
                    item.endpoint.Contains(textoBusqueda) ||
                    item.paginaOrigen.Contains(textoBusqueda) ||
                    item.correlationId.Contains(textoBusqueda));
            }

            int total = await query.CountAsync(cancellationToken);
            int totalPaginas = total == 0
                ? 1
                : (int)Math.Ceiling(total / (double)tamanoPagina);

            pagina = Math.Min(pagina, totalPaginas);

            List<BitacoraListadoDto> items = await query
                .OrderByDescending(item => item.fechaHoraUtc)
                .ThenByDescending(item => item.bitacoraId)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(item => new BitacoraListadoDto
                {
                    BitacoraId = item.bitacoraId,
                    FechaHoraUtc = item.fechaHoraUtc,
                    UsuarioId = item.usuarioId,
                    UsuarioNombre = item.usuarioNombre,
                    RolNombre = item.rolNombre,
                    Modulo = item.modulo,
                    Accion = item.accion,
                    MetodoHttp = item.metodoHttp,
                    Endpoint = item.endpoint,
                    PaginaOrigen = item.paginaOrigen,
                    Descripcion = item.descripcion,
                    CodigoEstado = item.codigoEstado,
                    Exitoso = item.exitoso,
                    DuracionMs = item.duracionMs,
                    CantidadCambios = item.detalles.Count
                })
                .ToListAsync(cancellationToken);

            return Ok(new BitacoraPaginadaV2Dto
            {
                Items = items,
                Pagina = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = total,
                TotalPaginas = totalPaginas,
                CorteConsultaUtc = corte
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Obtener(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            BitacoraDetalleDto? item = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(registro => registro.bitacoraId == id)
                .Select(registro => new BitacoraDetalleDto
                {
                    BitacoraId = registro.bitacoraId,
                    FechaHoraUtc = registro.fechaHoraUtc,
                    UsuarioId = registro.usuarioId,
                    UsuarioNombre = registro.usuarioNombre,
                    RolNombre = registro.rolNombre,
                    Modulo = registro.modulo,
                    Accion = registro.accion,
                    MetodoHttp = registro.metodoHttp,
                    Endpoint = registro.endpoint,
                    PaginaOrigen = registro.paginaOrigen,
                    Descripcion = registro.descripcion,
                    Parametros = registro.parametros,
                    DireccionIp = registro.direccionIp,
                    Dispositivo = registro.dispositivo,
                    Plataforma = registro.plataforma,
                    VersionApp = registro.versionApp,
                    CorrelationId = registro.correlationId,
                    CodigoEstado = registro.codigoEstado,
                    Exitoso = registro.exitoso,
                    DuracionMs = registro.duracionMs,
                    Error = registro.error,
                    CantidadCambios = registro.detalles.Count,
                    Cambios = registro.detalles
                        .OrderBy(cambio => cambio.bitacoraDetalleId)
                        .Select(cambio => new BitacoraCambioDto
                        {
                            BitacoraDetalleId = cambio.bitacoraDetalleId,
                            FechaHoraUtc = cambio.fechaHoraUtc,
                            Entidad = cambio.entidad,
                            EntidadId = cambio.entidadId,
                            Operacion = cambio.operacion,
                            ValoresAnteriores = cambio.valoresAnteriores,
                            ValoresNuevos = cambio.valoresNuevos,
                            PropiedadesModificadas = cambio.propiedadesModificadas
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return item == null
                ? NotFound(Error("El registro de bitácora no existe."))
                : Ok(item);
        }

        [HttpGet("catalogos")]
        public async Task<IActionResult> Catalogos(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            List<string> acciones = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(item => item.accion != "")
                .Select(item => item.accion)
                .Distinct()
                .OrderBy(item => item)
                .ToListAsync(cancellationToken);

            List<string> modulos = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(item => item.modulo != "")
                .Select(item => item.modulo)
                .Distinct()
                .OrderBy(item => item)
                .ToListAsync(cancellationToken);

            var usuariosSistema = await db.Usuarios
                .AsNoTracking()
                .Select(item => new
                {
                    item.UsuarioId,
                    item.nombreCompletoUsuario,
                    item.nombreUsuario
                })
                .ToListAsync(cancellationToken);

            var usuariosHistoricos = await bitacoraDb.Bitacoras
                .AsNoTracking()
                .Where(item => item.usuarioId.HasValue && item.usuarioId.Value > 0)
                .Select(item => new
                {
                    UsuarioId = item.usuarioId!.Value,
                    item.usuarioNombre
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            var nombresPorId = new Dictionary<int, string>();

            foreach (var usuario in usuariosSistema)
            {
                string nombre = !string.IsNullOrWhiteSpace(
                        usuario.nombreCompletoUsuario)
                    ? usuario.nombreCompletoUsuario.Trim()
                    : (usuario.nombreUsuario ?? string.Empty).Trim();

                nombresPorId[usuario.UsuarioId] = string.IsNullOrWhiteSpace(nombre)
                    ? $"Usuario {usuario.UsuarioId}"
                    : nombre;
            }

            foreach (var grupo in usuariosHistoricos
                         .GroupBy(item => item.UsuarioId))
            {
                if (nombresPorId.ContainsKey(grupo.Key))
                    continue;

                string nombre = grupo
                    .Select(item => (item.usuarioNombre ?? string.Empty).Trim())
                    .FirstOrDefault(valor => !string.IsNullOrWhiteSpace(valor))
                    ?? string.Empty;

                nombresPorId[grupo.Key] = string.IsNullOrWhiteSpace(nombre)
                    ? $"Usuario {grupo.Key}"
                    : nombre;
            }

            List<BitacoraUsuarioFiltroDto> usuarios = nombresPorId
                .Select(item => new BitacoraUsuarioFiltroDto
                {
                    UsuarioId = item.Key,
                    Nombre = item.Value
                })
                .OrderBy(item => item.Nombre)
                .ThenBy(item => item.UsuarioId)
                .ToList();

            return Ok(new BitacoraCatalogosDto
            {
                Acciones = acciones,
                Modulos = modulos,
                Usuarios = usuarios
            });
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                InterfazBitacora,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    Error(resultado.Mensaje));
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

        private static DateTime? NormalizarUtcNullable(DateTime? valor) =>
            valor.HasValue
                ? NormalizarUtc(valor.Value)
                : null;

        private static DateTime NormalizarUtc(DateTime valor) =>
            valor.Kind switch
            {
                DateTimeKind.Utc => valor,
                DateTimeKind.Local => valor.ToUniversalTime(),
                _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
            };

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
