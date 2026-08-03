using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Vincula cada resultado individual con la estructura oficial del Álbum
    /// Botánico. El técnico de campo no modifica el catálogo: el analizador
    /// selecciona una ficha existente o propone una nueva, y el aprobador
    /// decide si la propuesta se convierte en una ficha oficial.
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

            IActionResult? acceso = await ValidarClasificadorAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIAImagen? imagen = ObtenerImagen(
                diagnostico!,
                imagenId);

            if (imagen?.ResultadoIA == null)
                return ResultadoNoDisponible();

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
                    message =
                        "La ficha seleccionada no existe o está inactiva en el Álbum Botánico."
                });
            }

            bool esAprobador = string.Equals(
                diagnostico!.Estado,
                DiagnosticoIAFlujo.Estados.PendienteAprobacion,
                StringComparison.OrdinalIgnoreCase);

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
            resultado.EstadoClasificacionAlbum = esAprobador
                ? DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaPorAprobador
                : DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaPorAnalizador;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                esAprobador
                    ? "APROBADOR_SELECCIONA_FICHA_ALBUM"
                    : "ANALIZADOR_CLASIFICA_IMAGEN",
                $"Fotografía {imagen.Orden}: se vinculó con {registro.Categoria} → {registro.titulo}.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La fotografía quedó vinculada con una ficha activa del Álbum Botánico."
            });
        }

        [HttpPost("{diagnosticoId:int}/imagen/{imagenId:int}/proponer-nueva")]
        public async Task<IActionResult> ProponerNueva(
            int diagnosticoId,
            int imagenId,
            [FromBody] DiagnosticoIAClasificacionPropuestaRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                diagnosticoId,
                cancellationToken);

            IActionResult? acceso = await ValidarAnalizadorAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIAImagen? imagen = ObtenerImagen(
                diagnostico!,
                imagenId);

            if (imagen?.ResultadoIA == null)
                return ResultadoNoDisponible();

            CategoriaAlbumBotanico? categoria = await albumDb
                .CategoriasAlbumBotanico
                .AsNoTracking()
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
                    message =
                        "La categoría propuesta no existe o está inactiva."
                });
            }

            string titulo = Normalizar(request.Titulo, 200);
            string motivo = Normalizar(request.Motivo, 1000);

            if (titulo.Length < 3 || motivo.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Indique un nombre válido y explique por qué no corresponde a una ficha existente."
                });
            }

            DiagnosticoIAImagenResultadoIA resultado = imagen.ResultadoIA;
            resultado.CategoriaAlbumBotanicoIdSugerida =
                categoria.categoriaAlbumBotanicoId;
            resultado.AlbumBotanicoCafeIdSugerido = null;
            resultado.CategoriaAlbumSugerida =
                Normalizar(categoria.nombreCategoria, 150);
            resultado.ClasificacionAlbumSugerida = titulo;
            resultado.NombreCientificoSugerido =
                Normalizar(request.NombreCientifico, 200);
            resultado.MotivoClasificacionAlbum = motivo;
            resultado.CoincideCatalogoAlbum = false;
            resultado.RequiereDecisionClasificacion = true;
            resultado.CategoriaAlbumBotanicoIdSeleccionada = null;
            resultado.AlbumBotanicoCafeIdSeleccionado = null;
            resultado.CategoriaAlbumSeleccionada = string.Empty;
            resultado.ClasificacionAlbumSeleccionada = string.Empty;
            resultado.EstadoClasificacionAlbum =
                DiagnosticoIAFlujo.ClasificacionAlbum.PropuestaAnalizador;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                "ANALIZADOR_PROPONE_FICHA_ALBUM",
                $"Fotografía {imagen.Orden}: se propuso crear {categoria.nombreCategoria} → {titulo}. La propuesta requiere decisión del aprobador.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La propuesta fue guardada. El aprobador decidirá si crea la ficha oficial."
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

            IActionResult? acceso = await ValidarAprobadorAsync(
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
                            "No tiene permiso para crear fichas en el Álbum Botánico."
                    });
            }

            DiagnosticoIAImagen? imagen = ObtenerImagen(
                diagnostico!,
                imagenId);

            if (imagen?.ResultadoIA == null)
                return ResultadoNoDisponible();

            if (!DiagnosticoIAFlujo.ClasificacionAlbum.EstaPropuesta(
                    imagen.ResultadoIA.EstadoClasificacionAlbum))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La fotografía no contiene una propuesta pendiente del analizador."
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
                    message =
                        "La categoría seleccionada no existe o está inactiva."
                });
            }

            string titulo = Normalizar(request.Titulo, 200);
            string descripcion = Normalizar(request.Descripcion, 4000);

            if (titulo.Length < 3 || descripcion.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El título y la descripción de la nueva ficha son obligatorios."
                });
            }

            bool duplicado = await albumDb.AlbumesBotanicosCafe
                .AnyAsync(item =>
                    item.categoriaAlbumBotanicoId ==
                        categoria.categoriaAlbumBotanicoId &&
                    item.activo &&
                    item.titulo == titulo,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una ficha activa con ese nombre en la categoría seleccionada. Use la ficha existente."
                });
            }

            var registro = new AlbumBotanicoCafe
            {
                categoriaAlbumBotanicoId =
                    categoria.categoriaAlbumBotanicoId,
                titulo = titulo,
                nombreCientifico =
                    Normalizar(request.NombreCientifico, 200),
                descripcion = descripcion,
                sintomas = Normalizar(request.Sintomas, 4000),
                observaciones =
                    "Ficha creada desde una inspección fitosanitaria aprobada.",
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
                DiagnosticoIAFlujo.ClasificacionAlbum.CreadaPorAprobador;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                "APROBADOR_CREA_FICHA_ALBUM",
                $"Fotografía {imagen.Orden}: el aprobador autorizó y creó {categoria.nombreCategoria} → {titulo}.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La ficha fue creada en el Álbum Botánico y vinculada con la fotografía.",
                data = new
                {
                    registro.albumBotanicoCafeId,
                    categoria.categoriaAlbumBotanicoId
                }
            });
        }

        private async Task<IActionResult?> ValidarClasificadorAsync(
            DiagnosticoIA? diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (diagnostico == null)
                return NoEncontrado();

            if (EsEstadoAnalizador(diagnostico.Estado))
            {
                return await ValidarPermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);
            }

            if (string.Equals(
                    diagnostico.Estado,
                    DiagnosticoIAFlujo.Estados.PendienteAprobacion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return await ValidarPermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);
            }

            return Conflict(new
            {
                success = false,
                message =
                    "La clasificación no puede modificarse en el estado actual de la inspección."
            });
        }

        private async Task<IActionResult?> ValidarAnalizadorAsync(
            DiagnosticoIA? diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (diagnostico == null)
                return NoEncontrado();

            if (!EsEstadoAnalizador(diagnostico.Estado))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Las propuestas de clasificación solo pueden registrarse durante el análisis humano."
                });
            }

            return await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);
        }

        private async Task<IActionResult?> ValidarAprobadorAsync(
            DiagnosticoIA? diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (diagnostico == null)
                return NoEncontrado();

            if (!string.Equals(
                    diagnostico.Estado,
                    DiagnosticoIAFlujo.Estados.PendienteAprobacion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Una nueva ficha solo puede autorizarse mientras el caso está pendiente de aprobación."
                });
            }

            return await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
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

        private static bool EsEstadoAnalizador(string? estado) =>
            estado is
                DiagnosticoIAFlujo.Estados.PendienteAnalizador or
                DiagnosticoIAFlujo.Estados.EnAnalisisHumano or
                DiagnosticoIAFlujo.Estados.DevueltoCorreccion;

        private async Task<DiagnosticoIA?> CargarDiagnosticoAsync(
            int diagnosticoId,
            CancellationToken cancellationToken) =>
            await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == diagnosticoId &&
                    item.Activo,
                    cancellationToken);

        private static DiagnosticoIAImagen? ObtenerImagen(
            DiagnosticoIA diagnostico,
            int imagenId) =>
            diagnostico.Imagenes.FirstOrDefault(item =>
                item.DiagnosticoIAImagenId == imagenId);

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "La solicitud no existe."
            });

        private IActionResult ResultadoNoDisponible() =>
            NotFound(new
            {
                success = false,
                message =
                    "La fotografía no contiene un resultado individual de Gemini."
            });

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

    public sealed class DiagnosticoIAClasificacionPropuestaRequest
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? NombreCientifico { get; set; }
        public string Motivo { get; set; } = string.Empty;
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
