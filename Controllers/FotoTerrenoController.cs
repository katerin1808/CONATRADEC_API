using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.FotoTerrenoDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints utilizados por la aplicación móvil y Windows.
    /// Conserva las rutas existentes, pero agrega autenticación, permisos y
    /// metadatos sin romper los clientes anteriores.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/fotoTerreno")]
    public class FotoTerrenoController : ControllerBase
    {
        private const string PermisoFotos =
            "fotosTerrenoPage";

        private const string PermisoTerrenos =
            "terrenoPage";

        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly PermisoApiService permisos;

        public FotoTerrenoController(
            DBContext db,
            ImageService imageService,
            PermisoApiService permisos)
        {
            this.db = db;
            this.imageService = imageService;
            this.permisos = permisos;
        }

        [HttpPost("subir")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(85 * 1024 * 1024)]
        public async Task<IActionResult> SubirFotos(
            [FromForm] FotoTerrenoCrearDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoCompatibilidadAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            Terreno? terreno = await db.Terreno
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item =>
                        item.terrenoId == dto.terrenoId &&
                        item.activo,
                    cancellationToken);

            if (terreno == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El terreno no existe o está inactivo.",
                    mensaje =
                        "El terreno no existe o está inactivo."
                });
            }

            if (dto.fotos == null || dto.fotos.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe subir al menos una fotografía.",
                    mensaje =
                        "Debe subir al menos una fotografía."
                });
            }

            List<IFormFile> archivos = dto.fotos
                .Where(item => item is not null && item.Length > 0)
                .Take(10)
                .ToList();

            if (archivos.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se encontró ninguna fotografía válida.",
                    mensaje =
                        "No se encontró ninguna fotografía válida."
                });
            }

            bool existePortada = await db.FotoTerreno
                .AnyAsync(
                    item =>
                        item.terrenoId == dto.terrenoId &&
                        item.activo &&
                        item.esPortada,
                    cancellationToken);

            bool primeraComoPortada =
                dto.establecerComoPortada ||
                !existePortada;

            if (dto.establecerComoPortada)
            {
                await QuitarPortadasAsync(
                    dto.terrenoId,
                    fotoExceptuadaId: null,
                    cancellationToken);
            }

            var entidades = new List<FotoTerreno>();
            var rutasNuevas = new List<string>();

            try
            {
                for (int indice = 0;
                     indice < archivos.Count;
                     indice++)
                {
                    IFormFile archivo = archivos[indice];

                    string rutaRelativa =
                        await imageService.GuardarImagenWebpAsync(
                            archivo,
                            "terrenos",
                            1600,
                            1600,
                            72);

                    rutasNuevas.Add(rutaRelativa);

                    string urlCompleta =
                        ConstruirUrlPublica(rutaRelativa);

                    var entidad = new FotoTerreno
                    {
                        terrenoId = dto.terrenoId,
                        urlFotoTerreno = urlCompleta,
                        tituloFotoTerreno =
                            NormalizarTexto(dto.titulo, 150),
                        descripcionFotoTerreno =
                            NormalizarTexto(dto.descripcion, 600),
                        nombreArchivoOriginal =
                            NormalizarTexto(archivo.FileName, 255),
                        fechaRegistroUtc = DateTime.UtcNow,
                        fechaCaptura = dto.fechaCaptura?.Date,
                        esPortada =
                            primeraComoPortada && indice == 0,
                        activo = true
                    };

                    entidades.Add(entidad);
                    db.FotoTerreno.Add(entidad);
                }

                await db.SaveChangesAsync(cancellationToken);

                List<FotoTerrenoListarDto> respuesta =
                    entidades
                        .Select(CrearDto)
                        .ToList();

                return Ok(new
                {
                    success = true,
                    message = entidades.Count == 1
                        ? "Fotografía guardada correctamente."
                        : "Fotografías guardadas correctamente.",
                    mensaje = entidades.Count == 1
                        ? "Fotografía guardada correctamente."
                        : "Fotografías guardadas correctamente.",
                    fotos = respuesta
                });
            }
            catch
            {
                foreach (string ruta in rutasNuevas)
                    EliminarImagenSinInterrumpir(ruta);

                throw;
            }
        }

        [HttpGet("por-terreno/{terrenoId:int}")]
        public async Task<IActionResult> ObtenerPorTerreno(
            int terrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoCompatibilidadAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<FotoTerreno> entidades =
                await db.FotoTerreno
                    .AsNoTracking()
                    .Where(item =>
                        item.terrenoId == terrenoId &&
                        item.activo)
                    .OrderByDescending(item => item.esPortada)
                    .ThenByDescending(item => item.fechaCaptura)
                    .ThenByDescending(item => item.fechaRegistroUtc)
                    .ToListAsync(cancellationToken);

            List<FotoTerrenoListarDto> fotos =
                entidades
                    .Select(CrearDto)
                    .ToList();

            return Ok(fotos);
        }

        [HttpPut("editar/{fotoTerrenoId:int}")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> EditarFoto(
            int fotoTerrenoId,
            [FromForm] FotoTerrenoEditarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoCompatibilidadAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? fotoTerreno =
                await db.FotoTerreno
                    .FirstOrDefaultAsync(
                        item =>
                            item.fotoTerrenoId == fotoTerrenoId &&
                            item.activo,
                        cancellationToken);

            if (fotoTerreno == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La fotografía no existe o está inactiva.",
                    mensaje =
                        "La fotografía no existe o está inactiva."
                });
            }

            string? rutaAnterior =
                fotoTerreno.urlFotoTerreno;

            string? rutaNueva = null;

            try
            {
                if (dto.foto is not null &&
                    dto.foto.Length > 0)
                {
                    rutaNueva =
                        await imageService.GuardarImagenWebpAsync(
                            dto.foto,
                            "terrenos",
                            1600,
                            1600,
                            72);

                    fotoTerreno.urlFotoTerreno =
                        ConstruirUrlPublica(rutaNueva);

                    fotoTerreno.nombreArchivoOriginal =
                        NormalizarTexto(
                            dto.foto.FileName,
                            255);
                }

                if (dto.titulo is not null)
                {
                    fotoTerreno.tituloFotoTerreno =
                        NormalizarTexto(dto.titulo, 150);
                }

                if (dto.descripcion is not null)
                {
                    fotoTerreno.descripcionFotoTerreno =
                        NormalizarTexto(dto.descripcion, 600);
                }

                if (dto.fechaCaptura.HasValue)
                {
                    fotoTerreno.fechaCaptura =
                        dto.fechaCaptura.Value.Date;
                }

                if (dto.establecerComoPortada == true)
                {
                    await QuitarPortadasAsync(
                        fotoTerreno.terrenoId,
                        fotoTerreno.fotoTerrenoId,
                        cancellationToken);

                    fotoTerreno.esPortada = true;
                }

                await db.SaveChangesAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(rutaNueva) &&
                    !string.IsNullOrWhiteSpace(rutaAnterior))
                {
                    EliminarImagenSinInterrumpir(rutaAnterior);
                }

                return Ok(new
                {
                    success = true,
                    message =
                        "Fotografía actualizada correctamente.",
                    mensaje =
                        "Fotografía actualizada correctamente.",
                    foto = CrearDto(fotoTerreno)
                });
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(rutaNueva))
                    EliminarImagenSinInterrumpir(rutaNueva);

                throw;
            }
        }

        [HttpDelete("eliminar/{fotoTerrenoId:int}")]
        public async Task<IActionResult> EliminarFoto(
            int fotoTerrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoCompatibilidadAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? fotoTerreno =
                await db.FotoTerreno
                    .FirstOrDefaultAsync(
                        item =>
                            item.fotoTerrenoId == fotoTerrenoId &&
                            item.activo,
                        cancellationToken);

            if (fotoTerreno == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La fotografía no existe o ya está inactiva.",
                    mensaje =
                        "La fotografía no existe o ya está inactiva."
                });
            }

            bool eraPortada = fotoTerreno.esPortada;

            fotoTerreno.activo = false;
            fotoTerreno.esPortada = false;

            if (eraPortada)
            {
                FotoTerreno? siguiente =
                    await db.FotoTerreno
                        .Where(item =>
                            item.terrenoId ==
                                fotoTerreno.terrenoId &&
                            item.fotoTerrenoId !=
                                fotoTerreno.fotoTerrenoId &&
                            item.activo)
                        .OrderByDescending(item =>
                            item.fechaCaptura)
                        .ThenByDescending(item =>
                            item.fechaRegistroUtc)
                        .FirstOrDefaultAsync(
                            cancellationToken);

                if (siguiente != null)
                    siguiente.esPortada = true;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Fotografía desactivada correctamente.",
                mensaje =
                    "Fotografía desactivada correctamente."
            });
        }

        private async Task<IActionResult?>
            ValidarAccesoCompatibilidadAsync(
                TipoPermisoApi tipoPermiso,
                CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    PermisoFotos,
                    tipoPermiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            if (resultado.CodigoEstado ==
                StatusCodes.Status401Unauthorized)
            {
                return StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje,
                        mensaje = resultado.Mensaje
                    });
            }

            // Compatibilidad con versiones de MAUI que todavía relacionan
            // la galería con el permiso general de terrenos.
            ResultadoPermisoApi compatibilidad =
                await permisos.ValidarAsync(
                    usuarioId,
                    PermisoTerrenos,
                    tipoPermiso,
                    cancellationToken);

            if (compatibilidad.Permitido)
                return null;

            return StatusCode(
                compatibilidad.CodigoEstado,
                new
                {
                    success = false,
                    message = compatibilidad.Mensaje,
                    mensaje = compatibilidad.Mensaje
                });
        }

        private async Task QuitarPortadasAsync(
            int terrenoId,
            int? fotoExceptuadaId,
            CancellationToken cancellationToken)
        {
            List<FotoTerreno> portadas =
                await db.FotoTerreno
                    .Where(item =>
                        item.terrenoId == terrenoId &&
                        item.activo &&
                        item.esPortada &&
                        (!fotoExceptuadaId.HasValue ||
                         item.fotoTerrenoId !=
                            fotoExceptuadaId.Value))
                    .ToListAsync(cancellationToken);

            foreach (FotoTerreno portada in portadas)
                portada.esPortada = false;
        }

        private void EliminarImagenSinInterrumpir(
            string? ruta)
        {
            try
            {
                string? rutaLocal =
                    ExtraerRutaLocalTerreno(ruta);

                if (rutaLocal is not null)
                    imageService.EliminarImagen(rutaLocal);
            }
            catch
            {
                // La operación principal ya puede haberse confirmado en BD.
                // La limpieza física no debe invalidar esa transacción.
            }
        }

        private string ConstruirUrlVisible(string? url)
        {
            string? rutaLocal =
                ExtraerRutaLocalTerreno(url);

            return rutaLocal is null
                ? url ?? string.Empty
                : $"{Request.Scheme}://{Request.Host}" +
                  $"{Request.PathBase}{rutaLocal}";
        }

        private static string? ExtraerRutaLocalTerreno(
            string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string ruta = url.Trim();

            if (Uri.TryCreate(
                    ruta,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                ruta = uri.AbsolutePath;
            }

            ruta = Uri.UnescapeDataString(ruta)
                .Replace('\\', '/');

            const string prefijo =
                "resources/uploads/terrenos/";

            int posicion = ruta.IndexOf(
                prefijo,
                StringComparison.OrdinalIgnoreCase);

            if (posicion < 0)
                return null;

            string rutaLocal =
                "/" + ruta[posicion..].TrimStart('/');

            return rutaLocal
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segmento => segmento == "..")
                    ? null
                    : rutaLocal;
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int id)
                ? id
                : null;
        }

        private string ConstruirUrlPublica(
            string rutaRelativa) =>
            $"{Request.Scheme}://{Request.Host}" +
            $"{Request.PathBase}{rutaRelativa}";

        private FotoTerrenoListarDto CrearDto(
            FotoTerreno item) =>
            new()
            {
                fotoTerrenoId = item.fotoTerrenoId,
                terrenoId = item.terrenoId,
                urlFotoTerreno =
                    ConstruirUrlVisible(item.urlFotoTerreno),
                titulo = item.tituloFotoTerreno,
                descripcion = item.descripcionFotoTerreno,
                nombreArchivoOriginal =
                    item.nombreArchivoOriginal,
                fechaRegistroUtc = item.fechaRegistroUtc,
                fechaCaptura = item.fechaCaptura,
                esPortada = item.esPortada,
                activo = item.activo
            };

        private static string NormalizarTexto(
            string? valor,
            int longitudMaxima)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length <= longitudMaxima
                ? texto
                : texto[..longitudMaxima];
        }
    }
}
