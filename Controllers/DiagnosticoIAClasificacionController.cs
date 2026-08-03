using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Resuelve la clasificación oficial de cada fotografía contra el mismo
    /// catálogo utilizado por el Álbum Botánico. Gemini solo propone; el
    /// técnico confirma una ficha existente o crea una nueva de forma expresa.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia-clasificacion")]
    public sealed class DiagnosticoIAClasificacionController : ControllerBase
    {
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext albumDb;
        private readonly PermisoApiService permisos;

        public DiagnosticoIAClasificacionController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext albumDb,
            PermisoApiService permisos)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.albumDb = albumDb;
            this.permisos = permisos;
        }

        [HttpPost("{diagnosticoId:int}/imagen/{imagenId:int}/usar-existente")]
        public async Task<IActionResult> UsarExistente(
            int diagnosticoId,
            int imagenId,
            [FromBody] DiagnosticoIAClasificacionExistenteRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                diagnosticoId,
                cancellationToken);

            IActionResult? acceso = await ValidarTecnicoAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIAImagen? imagen = diagnostico!.Imagenes
                .FirstOrDefault(item => item.DiagnosticoIAImagenId == imagenId);

            if (imagen?.ResultadoIA == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La fotografía no contiene un resultado de Gemini que pueda clasificarse."
                });
            }

            var registro = await albumDb.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(item =>
                    item.albumBotanicoCafeId == request.AlbumBotanicoCafeId &&
                    item.activo &&
                    item.Categoria.activo)
                .Select(item => new
                {
                    item.albumBotanicoCafeId,
                    item.categoriaAlbumBotanicoId,
                    item.titulo,
                    Categoria = item.Categoria.nombreCategoria
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (registro == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La clasificación seleccionada no existe o está inactiva en el Álbum Botánico."
                });
            }

            DiagnosticoIAImagenResultadoIA resultado = imagen.ResultadoIA;
            resultado.CategoriaAlbumBotanicoIdSeleccionada =
                registro.categoriaAlbumBotanicoId;
            resultado.AlbumBotanicoCafeIdSeleccionado =
                registro.albumBotanicoCafeId;
            resultado.CategoriaAlbumSeleccionada =
                Normalizar(registro.Categoria, 150);
            resultado.ClasificacionAlbumSeleccionada =
                Normalizar(registro.titulo, 200);
            resultado.RequiereDecisionClasificacion = false;
            resultado.EstadoClasificacionAlbum =
                DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaPorTecnico;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                "TECNICO_CLASIFICA_IMAGEN",
                $"Fotografía {imagen.Orden}: el técnico seleccionó {registro.Categoria} → {registro.titulo}.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "La fotografía quedó vinculada con una clasificación existente del Álbum Botánico."
            });
        }

        [HttpPost("{diagnosticoId:int}/imagen/{imagenId:int}/crear-clasificacion")]
        public async Task<IActionResult> CrearClasificacion(
            int diagnosticoId,
            int imagenId,
            [FromBody] DiagnosticoIAClasificacionCrearRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                diagnosticoId,
                cancellationToken);

            IActionResult? acceso = await ValidarTecnicoAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ResultadoPermisoApi permisoAlbum = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permisoAlbum.Permitido)
            {
                return StatusCode(
                    permisoAlbum.CodigoEstado,
                    new
                    {
                        success = false,
                        message =
                            "No tiene permiso para crear fichas en el Álbum Botánico. Seleccione una clasificación existente o solicite apoyo a un administrador."
                    });
            }

            DiagnosticoIAImagen? imagen = diagnostico!.Imagenes
                .FirstOrDefault(item => item.DiagnosticoIAImagenId == imagenId);

            if (imagen?.ResultadoIA == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La fotografía no contiene un resultado de Gemini que pueda clasificarse."
                });
            }

            string titulo = Normalizar(request.Titulo, 200);
            string descripcion = Normalizar(request.Descripcion, 4000);

            if (titulo.Length < 3 || descripcion.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Indique un título y una descripción suficientes para la nueva ficha del álbum."
                });
            }

            CategoriaAlbumBotanico? categoria = await albumDb
                .CategoriasAlbumBotanico
                .FirstOrDefaultAsync(item =>
                    item.categoriaAlbumBotanicoId ==
                        request.CategoriaAlbumBotanicoId &&
                    item.activo,
                    cancellationToken);

            if (categoria == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría seleccionada no existe o está inactiva."
                });
            }

            bool duplicado = await albumDb.AlbumesBotanicosCafe
                .AnyAsync(item =>
                    item.categoriaAlbumBotanicoId ==
                        categoria.categoriaAlbumBotanicoId &&
                    item.titulo == titulo &&
                    item.activo,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Ya existe una ficha activa con ese título dentro de la categoría seleccionada."
                });
            }

            var registro = new AlbumBotanicoCafe
            {
                categoriaAlbumBotanicoId = categoria.categoriaAlbumBotanicoId,
                titulo = titulo,
                nombreCientifico = Normalizar(request.NombreCientifico, 200),
                descripcion = descripcion,
                sintomas = Normalizar(request.Sintomas, 4000),
                observaciones =
                    "Ficha creada desde una inspección fitosanitaria después de la confirmación expresa del técnico.",
                activo = true,
                fechaCreacion = DateTime.Now
            };

            albumDb.AlbumesBotanicosCafe.Add(registro);
            await albumDb.SaveChangesAsync(cancellationToken);

            DiagnosticoIAImagenResultadoIA resultado = imagen.ResultadoIA;
            resultado.CategoriaAlbumBotanicoIdSeleccionada =
                categoria.categoriaAlbumBotanicoId;
            resultado.AlbumBotanicoCafeIdSeleccionado =
                registro.albumBotanicoCafeId;
            resultado.CategoriaAlbumSeleccionada =
                Normalizar(categoria.nombreCategoria, 150);
            resultado.ClasificacionAlbumSeleccionada = titulo;
            resultado.RequiereDecisionClasificacion = false;
            resultado.EstadoClasificacionAlbum =
                DiagnosticoIAFlujo.ClasificacionAlbum.CreadaDesdeInspeccion;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                "TECNICO_CREA_CLASIFICACION_ALBUM",
                $"Fotografía {imagen.Orden}: el técnico creó y seleccionó {categoria.nombreCategoria} → {titulo} en el Álbum Botánico.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "La nueva ficha fue creada en el Álbum Botánico y vinculada con la fotografía.",
                data = new
                {
                    registro.albumBotanicoCafeId,
                    categoria.categoriaAlbumBotanicoId
                }
            });
        }

        private async Task<DiagnosticoIA?> CargarDiagnosticoAsync(
            int diagnosticoId,
            CancellationToken cancellationToken) =>
            await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == diagnosticoId && item.Activo,
                    cancellationToken);

        private async Task<IActionResult?> ValidarTecnicoAsync(
            DiagnosticoIA? diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La solicitud no existe."
                });
            }

            if (!usuarioId.HasValue ||
                diagnostico.UsuarioSolicitanteId != usuarioId.Value)
            {
                return Forbid();
            }

            if (!string.Equals(
                    diagnostico.Estado,
                    DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new
                {
                    success = false,
                    message = "Las clasificaciones solo pueden resolverse mientras la solicitud esté pendiente de la decisión del técnico."
                });
            }

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            return permiso.Permitido
                ? null
                : StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
        }

        private static void AgregarHistorial(
            DiagnosticoIA diagnostico,
            int usuarioId,
            string accion,
            string detalle)
        {
            diagnostico.Historial.Add(
                new DiagnosticoIAHistorial
                {
                    UsuarioId = usuarioId,
                    EstadoAnterior = diagnostico.Estado,
                    EstadoNuevo = diagnostico.Estado,
                    Accion = Normalizar(accion, 80),
                    Detalle = Normalizar(detalle, 2000),
                    FechaUtc = DateTime.UtcNow
                });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
        }

        private static string Normalizar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }

    public sealed class DiagnosticoIAClasificacionExistenteRequest
    {
        public int AlbumBotanicoCafeId { get; set; }
    }

    public sealed class DiagnosticoIAClasificacionCrearRequest
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? NombreCientifico { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string? Sintomas { get; set; }
    }
}
