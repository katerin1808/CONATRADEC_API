using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Listado resumido y paginado de análisis de suelo.
    ///
    /// Solo devuelve la página solicitada. Los indicadores de balance,
    /// enmienda y fertilización mixta se consultan después de conocer los
    /// identificadores visibles, evitando subconsultas pesadas por cada fila.
    /// </summary>
    [ApiController]
    [Route("api/analisis-listado")]
    public sealed class AnalisisListadoOptimizadoController : ControllerBase
    {
        private readonly DBContext db;

        public AnalisisListadoOptimizadoController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("paginado")]
        public async Task<ActionResult> ListarPaginado(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] bool soloPropios = true,
            [FromQuery] int? usuarioId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 6,
            CancellationToken cancellationToken = default)
        {
            ResultadoSesion resultadoSesion =
                await ObtenerSesionAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (!resultadoSesion.Permitido)
            {
                return StatusCode(
                    resultadoSesion.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultadoSesion.Mensaje
                    });
            }

            SesionAnalisis sesion = resultadoSesion.Sesion!;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 4, 30);

            var consulta =
                from calculo in db.AnalisisSueloCalculos.AsNoTracking()
                join analisis in db.AnalisisSuelos.AsNoTracking()
                    on calculo.analisisSueloId equals
                       analisis.analisisSueloId
                join terreno in db.Terreno.AsNoTracking()
                    on calculo.terrenoId equals terreno.terrenoId
                join usuario in db.Usuarios.AsNoTracking()
                    on calculo.usuarioId equals (int?)usuario.UsuarioId
                    into usuariosJoin
                from usuario in usuariosJoin.DefaultIfEmpty()
                where calculo.activo && analisis.activo
                select new
                {
                    calculo.analisisSueloCalculoId,
                    analisis.analisisSueloId,
                    analisis.identificadorAnalisisSuelo,
                    analisis.laboratorioAnalasisSuelo,
                    analisis.fechaAnalisisSuelo,
                    analisis.fechaCreacionAnalisisSuelo,
                    calculo.fechaCalculo,
                    calculo.terrenoId,
                    terreno.codigoTerreno,
                    terreno.nombrePropietarioTerreno,
                    calculo.tipoCultivoId,
                    calculo.tipoAnalisisSueloId,
                    calculo.cantidadQuintalesOro,
                    calculo.tamanoFinca,
                    calculo.phAnalisisSuelo,
                    calculo.usuarioId,
                    NombreUsuario = usuario == null
                        ? string.Empty
                        : usuario.nombreCompletoUsuario,
                    NombreCuenta = usuario == null
                        ? string.Empty
                        : usuario.nombreUsuario
                };

            if (!sesion.EsAdministrador || soloPropios)
            {
                consulta = consulta.Where(x =>
                    x.usuarioId == sesion.UsuarioId);
            }
            else if (usuarioId.HasValue && usuarioId.Value > 0)
            {
                consulta = consulta.Where(x =>
                    x.usuarioId == usuarioId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                consulta = consulta.Where(x =>
                    x.identificadorAnalisisSuelo.Contains(texto) ||
                    x.laboratorioAnalasisSuelo.Contains(texto) ||
                    x.codigoTerreno.Contains(texto) ||
                    x.nombrePropietarioTerreno.Contains(texto) ||
                    x.NombreUsuario.Contains(texto) ||
                    x.NombreCuenta.Contains(texto));
            }

            if (fechaDesde.HasValue)
            {
                DateTime desde = fechaDesde.Value.Date;

                consulta = consulta.Where(x =>
                    x.fechaCreacionAnalisisSuelo >= desde);
            }

            if (fechaHasta.HasValue)
            {
                DateTime hastaExclusiva =
                    fechaHasta.Value.Date.AddDays(1);

                consulta = consulta.Where(x =>
                    x.fechaCreacionAnalisisSuelo < hastaExclusiva);
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            var filas = await consulta
                .OrderByDescending(x =>
                    x.fechaCreacionAnalisisSuelo)
                .ThenByDescending(x => x.fechaCalculo)
                .ThenByDescending(x =>
                    x.analisisSueloCalculoId)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            int[] idsCalculos = filas
                .Select(x => x.analisisSueloCalculoId)
                .Distinct()
                .ToArray();

            HashSet<int> conFormula = new();
            HashSet<int> conEnmienda = new();
            HashSet<int> conMixta = new();

            if (idsCalculos.Length > 0)
            {
                List<IndicadorCalculo> indicadores =
                    await db.formulaNutricional
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            x.analisisSueloCalculoId.HasValue &&
                            idsCalculos.Contains(
                                x.analisisSueloCalculoId.Value))
                        .Select(x => new IndicadorCalculo
                        {
                            AnalisisSueloCalculoId =
                                x.analisisSueloCalculoId!.Value,
                            Tipo = 1
                        })
                        .Concat(
                            db.enmiendaCalcarea
                                .AsNoTracking()
                                .Where(x =>
                                    x.activo &&
                                    x.analisisSueloCalculoId.HasValue &&
                                    idsCalculos.Contains(
                                        x.analisisSueloCalculoId.Value))
                                .Select(x => new IndicadorCalculo
                                {
                                    AnalisisSueloCalculoId =
                                        x.analisisSueloCalculoId!.Value,
                                    Tipo = 2
                                }))
                        .Concat(
                            db.fertilizacionMixta
                                .AsNoTracking()
                                .Where(x =>
                                    x.activo &&
                                    idsCalculos.Contains(
                                        x.analisisSueloCalculoId))
                                .Select(x => new IndicadorCalculo
                                {
                                    AnalisisSueloCalculoId =
                                        x.analisisSueloCalculoId,
                                    Tipo = 3
                                }))
                        .ToListAsync(cancellationToken);

                conFormula = indicadores
                    .Where(x => x.Tipo == 1)
                    .Select(x => x.AnalisisSueloCalculoId)
                    .ToHashSet();

                conEnmienda = indicadores
                    .Where(x => x.Tipo == 2)
                    .Select(x => x.AnalisisSueloCalculoId)
                    .ToHashSet();

                conMixta = indicadores
                    .Where(x => x.Tipo == 3)
                    .Select(x => x.AnalisisSueloCalculoId)
                    .ToHashSet();
            }

            var items = filas.Select(x => new
            {
                analisisSueloCalculoId =
                    x.analisisSueloCalculoId,
                analisisSueloId = x.analisisSueloId,
                identificadorAnalisisSuelo =
                    x.identificadorAnalisisSuelo,
                laboratorioAnalasisSuelo =
                    x.laboratorioAnalasisSuelo,
                fechaAnalisisSuelo =
                    x.fechaAnalisisSuelo.ToString(
                        "yyyy-MM-dd"),
                fechaCreacionAnalisisSuelo =
                    x.fechaCreacionAnalisisSuelo.ToString("O"),
                fechaCalculo =
                    x.fechaCalculo.ToString("O"),
                terrenoId = x.terrenoId,
                codigoTerreno = x.codigoTerreno,
                nombreCliente =
                    x.nombrePropietarioTerreno,
                nombreTerreno = x.codigoTerreno,
                tipoCultivoId = x.tipoCultivoId,
                tipoAnalisisSueloId =
                    x.tipoAnalisisSueloId,
                cantidadQuintalesOro =
                    x.cantidadQuintalesOro,
                tamanoFinca = x.tamanoFinca,
                phAnalisisSuelo = x.phAnalisisSuelo,
                usuarioId = x.usuarioId,
                nombreUsuario =
                    string.IsNullOrWhiteSpace(
                        x.NombreUsuario)
                        ? x.NombreCuenta
                        : x.NombreUsuario,
                tieneFormulaNutricional =
                    conFormula.Contains(
                        x.analisisSueloCalculoId),
                tieneEnmiendaCalcarea =
                    conEnmienda.Contains(
                        x.analisisSueloCalculoId),
                tieneFertilizacionMixta =
                    conMixta.Contains(
                        x.analisisSueloCalculoId)
            }).ToList();

            int totalPaginas = totalRegistros == 0
                ? 1
                : (int)Math.Ceiling(
                    totalRegistros /
                    (double)tamanoPagina);

            return Ok(new
            {
                success = true,
                message =
                    "Análisis obtenidos correctamente.",
                data = new
                {
                    items,
                    pagina,
                    tamanoPagina,
                    totalRegistros,
                    totalPaginas,
                    tieneMas = pagina < totalPaginas,
                    esAdministrador =
                        sesion.EsAdministrador,
                    usuarios = Array.Empty<object>()
                }
            });
        }

        /// <summary>
        /// Catálogo para el filtro administrativo.
        /// Se solicita después de renderizar la primera página y no forma
        /// parte de la carga inicial.
        /// </summary>
        [HttpGet("usuarios")]
        public async Task<ActionResult> ListarUsuarios(
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ResultadoSesion resultadoSesion =
                await ObtenerSesionAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (!resultadoSesion.Permitido)
            {
                return StatusCode(
                    resultadoSesion.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultadoSesion.Mensaje
                    });
            }

            SesionAnalisis sesion = resultadoSesion.Sesion!;

            if (!sesion.EsAdministrador)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        message =
                            "Solo un administrador puede filtrar por usuario."
                    });
            }

            var usuariosBase = await (
                from calculo in
                    db.AnalisisSueloCalculos.AsNoTracking()
                join analisis in
                    db.AnalisisSuelos.AsNoTracking()
                    on calculo.analisisSueloId equals
                       analisis.analisisSueloId
                join usuario in db.Usuarios.AsNoTracking()
                    on calculo.usuarioId equals
                       (int?)usuario.UsuarioId
                where
                    calculo.activo &&
                    analisis.activo &&
                    usuario.activo
                select new
                {
                    usuario.UsuarioId,
                    usuario.nombreCompletoUsuario,
                    usuario.nombreUsuario
                })
                .Distinct()
                .OrderBy(x => x.nombreCompletoUsuario)
                .ThenBy(x => x.nombreUsuario)
                .ToListAsync(cancellationToken);

            var usuarios = usuariosBase
                .Select(x => new
                {
                    usuarioId = (int?)x.UsuarioId,
                    nombreCompleto =
                        string.IsNullOrWhiteSpace(
                            x.nombreCompletoUsuario)
                            ? x.nombreUsuario
                            : x.nombreCompletoUsuario
                })
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Usuarios obtenidos correctamente.",
                data = usuarios
            });
        }

        private async Task<ResultadoSesion> ObtenerSesionAsync(
            int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            if (!usuarioSesionId.HasValue ||
                usuarioSesionId.Value <= 0)
            {
                return ResultadoSesion.Denegado(
                    StatusCodes.Status401Unauthorized,
                    "No se encontró el usuario autenticado.");
            }

            var usuarioSesion = await db.Usuarios
                .AsNoTracking()
                .Where(x =>
                    x.UsuarioId == usuarioSesionId.Value &&
                    x.activo)
                .Select(x => new
                {
                    x.UsuarioId,
                    Rol = x.Rol.nombreRol
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (usuarioSesion == null)
            {
                return ResultadoSesion.Denegado(
                    StatusCodes.Status401Unauthorized,
                    "El usuario autenticado no existe o está inactivo.");
            }

            bool puedeLeer = await (
                from usuario in db.Usuarios.AsNoTracking()
                join permiso in db.RolInterfaz.AsNoTracking()
                    on usuario.rolId equals permiso.rolId
                join interfaz in db.Interfaz.AsNoTracking()
                    on permiso.interfazId equals
                       interfaz.interfazId
                where
                    usuario.UsuarioId ==
                        usuarioSesion.UsuarioId &&
                    usuario.activo &&
                    interfaz.activo &&
                    interfaz.nombreInterfaz ==
                        "MainPage" &&
                    permiso.leer == true
                select permiso.rolInterfazId)
                .AnyAsync(cancellationToken);

            if (!puedeLeer)
            {
                return ResultadoSesion.Denegado(
                    StatusCodes.Status403Forbidden,
                    "No tiene permiso para consultar análisis de suelo.");
            }

            bool esAdministrador =
                !string.IsNullOrWhiteSpace(
                    usuarioSesion.Rol) &&
                usuarioSesion.Rol.Contains(
                    "ADMIN",
                    StringComparison.OrdinalIgnoreCase);

            return ResultadoSesion.Ok(
                new SesionAnalisis
                {
                    UsuarioId = usuarioSesion.UsuarioId,
                    EsAdministrador = esAdministrador
                });
        }

        private sealed class IndicadorCalculo
        {
            public int AnalisisSueloCalculoId { get; set; }
            public int Tipo { get; set; }
        }

        private sealed class SesionAnalisis
        {
            public int UsuarioId { get; init; }
            public bool EsAdministrador { get; init; }
        }

        private sealed class ResultadoSesion
        {
            public bool Permitido { get; init; }
            public int CodigoEstado { get; init; }
            public string Mensaje { get; init; } =
                string.Empty;
            public SesionAnalisis? Sesion { get; init; }

            public static ResultadoSesion Ok(
                SesionAnalisis sesion) =>
                new()
                {
                    Permitido = true,
                    CodigoEstado =
                        StatusCodes.Status200OK,
                    Sesion = sesion
                };

            public static ResultadoSesion Denegado(
                int codigoEstado,
                string mensaje) =>
                new()
                {
                    Permitido = false,
                    CodigoEstado = codigoEstado,
                    Mensaje = mensaje
                };
        }
    }
}
