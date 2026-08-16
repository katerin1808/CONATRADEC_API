using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consulta paginada de Usuarios inactivos utilizada por la ventana
    /// "Usuarios inactivos" de Android y Windows.
    ///
    /// El endpoint histórico de CatalogosEliminadosController se conserva
    /// intacto para los demás catálogos y para compatibilidad.
    /// </summary>
    [ApiController]
    [Route("api/administracion/usuarios/inactivos")]
    public sealed class UsuariosInactivosPaginadosController : ControllerBase
    {
        private const string InterfazUsuarios = "userPage";
        private const string CatalogoUsuario = "usuario";

        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;

        public UsuariosInactivosPaginadosController(
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet]
        public async Task<ActionResult> Buscar(
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId = null,
            CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    InterfazUsuarios,
                    TipoPermisoApi.Leer,
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

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Usuario> consulta =
                db.Usuarios
                    .AsNoTracking()
                    .Where(item => !item.activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(item =>
                    item.nombreUsuario.Contains(texto) ||
                    item.nombreCompletoUsuario.Contains(texto) ||
                    item.correoUsuario.Contains(texto) ||
                    (item.identificacionUsuario != null &&
                     item.identificacionUsuario.Contains(texto)) ||
                    (item.telefonoUsuario != null &&
                     item.telefonoUsuario.Contains(texto)) ||
                    item.Rol.nombreRol.Contains(texto));
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros / (double)tamanoPagina);

            pagina = Math.Min(
                Math.Max(1, pagina),
                Math.Max(1, totalPaginas));

            /*
             * Orden natural ejecutado en SQL Server ANTES de Skip/Take.
             * Evita secuencias visuales como 128, 129, 13, 130 cuando el
             * nombre termina en un bloque numérico.
             */
            var conPosicionNumerica =
                consulta.Select(item => new
                {
                    Usuario = item,
                    PosicionNumero = EF.Functions.PatIndex(
                        "% [0-9]%",
                        item.nombreCompletoUsuario)
                });

            var conSufijo =
                conPosicionNumerica.Select(item => new
                {
                    item.Usuario,
                    Prefijo =
                        item.PosicionNumero > 0
                            ? item.Usuario.nombreCompletoUsuario
                                .Substring(
                                    0,
                                    Convert.ToInt32(item.PosicionNumero) - 1)
                                .Trim()
                            : item.Usuario.nombreCompletoUsuario,
                    Sufijo =
                        item.PosicionNumero > 0
                            ? item.Usuario.nombreCompletoUsuario
                                .Substring(
                                    Convert.ToInt32(item.PosicionNumero))
                                .Trim()
                            : string.Empty
                });

            var consultaOrdenable =
                conSufijo.Select(item => new
                {
                    item.Usuario,
                    item.Prefijo,
                    item.Sufijo,
                    EsSufijoNumerico =
                        item.Sufijo.Length > 0 &&
                        item.Sufijo.Length <= 18 &&
                        EF.Functions.PatIndex(
                            "%[^0-9]%",
                            item.Sufijo) == 0
                });

            List<UsuarioInactivoItemDto> items =
                await consultaOrdenable
                    .OrderBy(item =>
                        item.EsSufijoNumerico
                            ? item.Prefijo
                            : item.Usuario.nombreCompletoUsuario)
                    .ThenBy(item =>
                        item.EsSufijoNumerico ? 0 : 1)
                    .ThenBy(item =>
                        item.EsSufijoNumerico
                            ? Convert.ToInt64(item.Sufijo)
                            : 0L)
                    .ThenBy(item =>
                        item.Usuario.nombreCompletoUsuario)
                    .ThenBy(item => item.Usuario.UsuarioId)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item => new UsuarioInactivoItemDto
                    {
                        Id = item.Usuario.UsuarioId,
                        Catalogo = CatalogoUsuario,
                        Titulo = item.Usuario.nombreCompletoUsuario,
                        Subtitulo =
                            item.Usuario.nombreUsuario +
                            " · " +
                            item.Usuario.correoUsuario,
                        Detalle =
                            "Rol: " + item.Usuario.Rol.nombreRol,
                        Codigo =
                            item.Usuario.identificacionUsuario ??
                            string.Empty,
                        Activo = false
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Usuarios inactivos obtenidos correctamente.",
                data = new UsuarioInactivoPaginaDto
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                }
            });
        }

        private static string NormalizarBusqueda(string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length <= 150
                ? texto
                : texto[..150];
        }

        private sealed class UsuarioInactivoPaginaDto
        {
            public List<UsuarioInactivoItemDto> Items { get; set; } = new();
            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }
        }

        private sealed class UsuarioInactivoItemDto
        {
            public int Id { get; set; }
            public string Catalogo { get; set; } = string.Empty;
            public string Titulo { get; set; } = string.Empty;
            public string Subtitulo { get; set; } = string.Empty;
            public string Detalle { get; set; } = string.Empty;
            public string Codigo { get; set; } = string.Empty;
            public bool Activo { get; set; }
        }
    }
}
