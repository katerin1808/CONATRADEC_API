using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints administrativos consumidos por el portal web.
    /// </summary>
    [ApiController]
    [Route("api/dispositivos-conectados")]
    public sealed class DispositivosConectadosController : ControllerBase
    {
        private readonly DispositivosConexionDbContext dispositivosDb;
        private readonly DBContext db;

        public DispositivosConectadosController(
            DispositivosConexionDbContext dispositivosDb,
            DBContext db)
        {
            this.dispositivosDb = dispositivosDb;
            this.db = db;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult<DispositivosConexionResumenDto>>
            Resumen(
                [FromHeader(Name = "X-Usuario-Id")]
                    int? usuarioSesionId,
                [FromQuery] int minutosActivo = 2,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            minutosActivo = Math.Clamp(minutosActivo, 1, 15);
            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime corteUtc = ahoraUtc.AddMinutes(-minutosActivo);
            DateTime corte24Horas = ahoraUtc.AddHours(-24);

            List<DispositivoConexion> conectados =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .Where(x =>
                        x.Activo &&
                        x.ConectadoReportado &&
                        x.UltimoLatidoUtc >= corteUtc)
                    .ToListAsync(cancellationToken);

            int totalRegistrados =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .CountAsync(x => x.Activo, cancellationToken);

            int totalSesiones =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .Where(x => x.Activo)
                    .SumAsync(
                        x => (int?)x.CantidadSesiones,
                        cancellationToken) ?? 0;

            int activos24Horas =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Activo &&
                            x.UltimoLatidoUtc >= corte24Horas,
                        cancellationToken);

            DateTime? ultimoLatido =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .Where(x => x.Activo)
                    .MaxAsync(
                        x => (DateTime?)x.UltimoLatidoUtc,
                        cancellationToken);

            int android = conectados.Count(x =>
                EsPlataforma(x.Plataforma, "Android"));

            int windows = conectados.Count(x =>
                EsPlataforma(x.Plataforma, "Windows") ||
                EsPlataforma(x.Plataforma, "WinUI"));

            return Ok(new DispositivosConexionResumenDto
            {
                TotalConectados = conectados.Count,
                UsuariosConectados = conectados
                    .Select(x => x.UsuarioId)
                    .Distinct()
                    .Count(),
                AndroidConectados = android,
                WindowsConectados = windows,
                OtrosConectados = Math.Max(
                    0,
                    conectados.Count - android - windows),
                TotalDispositivosRegistrados = totalRegistrados,
                TotalSesionesRegistradas = totalSesiones,
                DispositivosActivosUltimas24Horas = activos24Horas,
                MinutosTolerancia = minutosActivo,
                FechaConsultaUtc = ahoraUtc,
                UltimoLatidoRecibidoUtc = ultimoLatido
            });
        }

        [HttpGet]
        public async Task<ActionResult<DispositivosConexionPaginadaDto>>
            Listar(
                [FromHeader(Name = "X-Usuario-Id")]
                    int? usuarioSesionId,
                [FromQuery] bool? conectado,
                [FromQuery] int? usuarioId,
                [FromQuery] string? plataforma,
                [FromQuery] string? versionApp,
                [FromQuery] string? buscar,
                [FromQuery] int minutosActivo = 2,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 25,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);
            minutosActivo = Math.Clamp(minutosActivo, 1, 15);

            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime corteUtc = ahoraUtc.AddMinutes(-minutosActivo);

            IQueryable<DispositivoConexion> query =
                dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .Where(x => x.Activo);

            if (conectado == true)
            {
                query = query.Where(x =>
                    x.ConectadoReportado &&
                    x.UltimoLatidoUtc >= corteUtc);
            }
            else if (conectado == false)
            {
                query = query.Where(x =>
                    !x.ConectadoReportado ||
                    x.UltimoLatidoUtc < corteUtc);
            }

            if (usuarioId.HasValue)
                query = query.Where(x => x.UsuarioId == usuarioId.Value);

            if (!string.IsNullOrWhiteSpace(plataforma))
            {
                string valor = plataforma.Trim();
                query = query.Where(x => x.Plataforma == valor);
            }

            if (!string.IsNullOrWhiteSpace(versionApp))
            {
                string valor = versionApp.Trim();
                query = query.Where(x => x.VersionApp == valor);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();
                query = query.Where(x =>
                    x.UsuarioNombre.Contains(texto) ||
                    x.CorreoUsuario.Contains(texto) ||
                    x.NombreDispositivo.Contains(texto) ||
                    x.Fabricante.Contains(texto) ||
                    x.Modelo.Contains(texto) ||
                    x.DireccionIp.Contains(texto) ||
                    x.InstalacionId.Contains(texto));
            }

            int total = await query.CountAsync(cancellationToken);

            List<DispositivoConexion> entidades = await query
                .OrderByDescending(x =>
                    x.ConectadoReportado &&
                    x.UltimoLatidoUtc >= corteUtc)
                .ThenByDescending(x => x.UltimoLatidoUtc)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            List<DispositivoConexionListadoDto> items = entidades
                .Select(x => Mapear(x, ahoraUtc, corteUtc))
                .ToList();

            return Ok(new DispositivosConexionPaginadaDto
            {
                Items = items,
                Pagina = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = total,
                TotalPaginas = total == 0
                    ? 1
                    : (int)Math.Ceiling(total / (double)tamanoPagina),
                MinutosTolerancia = minutosActivo,
                FechaConsultaUtc = ahoraUtc
            });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<DispositivoConexionListadoDto>>
            Obtener(
                int id,
                [FromHeader(Name = "X-Usuario-Id")]
                    int? usuarioSesionId,
                [FromQuery] int minutosActivo = 2,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            minutosActivo = Math.Clamp(minutosActivo, 1, 15);
            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime corteUtc = ahoraUtc.AddMinutes(-minutosActivo);

            DispositivoConexion? entidad =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.DispositivoConexionId == id &&
                            x.Activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "El dispositivo solicitado no existe."
                });
            }

            return Ok(Mapear(entidad, ahoraUtc, corteUtc));
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
                            "Debe enviar el encabezado X-Usuario-Id."
                    });
            }

            bool usuarioActivo = await db.Usuarios
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UsuarioId == usuarioId.Value &&
                        x.activo,
                    cancellationToken);

            if (!usuarioActivo)
            {
                return StatusCode(
                    StatusCodes.Status401Unauthorized,
                    new
                    {
                        mensaje =
                            "El usuario no existe o está inactivo."
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
                      && interfaz.nombreInterfaz ==
                         DispositivosConexionDatabaseInitializer
                             .CodigoInterfaz
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
                            "El usuario no tiene permiso para consultar " +
                            "los dispositivos conectados."
                    });
            }

            return null;
        }

        private static DispositivoConexionListadoDto Mapear(
            DispositivoConexion x,
            DateTime ahoraUtc,
            DateTime corteUtc)
        {
            bool conectado =
                x.ConectadoReportado &&
                x.UltimoLatidoUtc >= corteUtc;

            double segundos = Math.Max(
                0,
                (ahoraUtc - x.UltimoLatidoUtc).TotalSeconds);

            return new DispositivoConexionListadoDto
            {
                DispositivoConexionId = x.DispositivoConexionId,
                InstalacionId = x.InstalacionId,
                SesionId = x.SesionId,
                UsuarioId = x.UsuarioId,
                UsuarioNombre = x.UsuarioNombre,
                CorreoUsuario = x.CorreoUsuario,
                RolNombre = x.RolNombre,
                Plataforma = x.Plataforma,
                TipoDispositivo = x.TipoDispositivo,
                Fabricante = x.Fabricante,
                Modelo = x.Modelo,
                NombreDispositivo = x.NombreDispositivo,
                SistemaOperativo = x.SistemaOperativo,
                VersionSistema = x.VersionSistema,
                VersionApp = x.VersionApp,
                BuildApp = x.BuildApp,
                Idioma = x.Idioma,
                TipoConexion = x.TipoConexion,
                PaginaActual = x.PaginaActual,
                DireccionIp = x.DireccionIp,
                FechaRegistroUtc = x.FechaRegistroUtc,
                FechaInicioSesionUtc = x.FechaInicioSesionUtc,
                UltimoLatidoUtc = x.UltimoLatidoUtc,
                FechaDesconexionUtc = x.FechaDesconexionUtc,
                Conectado = conectado,
                SegundosDesdeUltimoLatido = (int)Math.Min(
                    int.MaxValue,
                    segundos),
                CantidadSesiones = x.CantidadSesiones
            };
        }

        private static bool EsPlataforma(
            string? plataforma,
            string esperada)
        {
            return string.Equals(
                plataforma?.Trim(),
                esperada,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
