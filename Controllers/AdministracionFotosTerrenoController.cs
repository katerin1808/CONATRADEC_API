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
    /// Administración web de fotografías vinculadas con terrenos.
    /// El controlador móvil conserva sus rutas en FotoTerrenoController.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/fotos-terreno")]
    public sealed class AdministracionFotosTerrenoController :
        ControllerBase
    {
        private const string CodigoPermiso =
            "fotosTerrenoPage";

        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly PermisoApiService permisos;
        private readonly IWebHostEnvironment environment;

        public AdministracionFotosTerrenoController(
            DBContext db,
            ImageService imageService,
            PermisoApiService permisos,
            IWebHostEnvironment environment)
        {
            this.db = db;
            this.imageService = imageService;
            this.permisos = permisos;
            this.environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] string? buscar = null,
            [FromQuery] int? terrenoId = null,
            [FromQuery] int? propietarioId = null,
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            [FromQuery] bool incluirInactivas = false,
            [FromQuery] string? problema = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 16,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 8, 60);

            string texto =
                (buscar ?? string.Empty).Trim();

            string filtroProblema =
                (problema ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            IQueryable<FotoTerreno> query =
                db.FotoTerreno
                    .AsNoTracking()
                    .Include(item => item.Terreno!)
                    .ThenInclude(item =>
                        item.RelacionesPropietario)
                    .ThenInclude(item =>
                        item.Propietario);

            if (!incluirInactivas)
                query = query.Where(item => item.activo);

            if (terrenoId.HasValue)
            {
                query = query.Where(item =>
                    item.terrenoId == terrenoId.Value);
            }

            if (propietarioId.HasValue)
            {
                query = query.Where(item =>
                    item.Terreno != null &&
                    item.Terreno.RelacionesPropietario.Any(
                        relacion =>
                            relacion.activo &&
                            relacion.propietarioId ==
                                propietarioId.Value));
            }

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(item =>
                    item.tituloFotoTerreno.Contains(texto) ||
                    item.descripcionFotoTerreno.Contains(texto) ||
                    item.nombreArchivoOriginal.Contains(texto) ||
                    item.urlFotoTerreno.Contains(texto) ||
                    (item.Terreno != null &&
                     (item.Terreno.codigoTerreno.Contains(texto) ||
                      item.Terreno.direccionTerreno.Contains(texto) ||
                      item.Terreno.RelacionesPropietario.Any(
                          relacion =>
                              relacion.activo &&
                              (relacion.Propietario.nombreCompleto
                                   .Contains(texto) ||
                               relacion.Propietario.identificacion
                                   .Contains(texto))))));
            }

            if (fechaDesde.HasValue)
            {
                DateTime desde = fechaDesde.Value.Date;

                query = query.Where(item =>
                    (item.fechaCaptura ??
                     item.fechaRegistroUtc) >= desde);
            }

            if (fechaHasta.HasValue)
            {
                DateTime hastaExclusiva =
                    fechaHasta.Value.Date.AddDays(1);

                query = query.Where(item =>
                    (item.fechaCaptura ??
                     item.fechaRegistroUtc) < hastaExclusiva);
            }

            if (filtroProblema == "HUERFANAS")
            {
                query = query.Where(item =>
                    item.Terreno == null);
            }
            else if (filtroProblema == "TERRENO_INACTIVO")
            {
                query = query.Where(item =>
                    item.Terreno != null &&
                    !item.Terreno.activo);
            }

            FotoTerrenoAdminResumenDto resumen =
                await ConstruirResumenAsync(
                    query,
                    cancellationToken);

            bool filtrarEnMemoria =
                filtroProblema is
                    "ARCHIVO_FALTANTE" or
                    "CUALQUIER_PROBLEMA";

            List<FotoTerrenoAdminItemDto> items;
            int totalRegistros;

            if (filtrarEnMemoria)
            {
                List<FotoTerrenoAdminItemDto> candidatos =
                    await Proyectar(query)
                        .OrderByDescending(item => item.EsPortada)
                        .ThenByDescending(item => item.FechaCaptura)
                        .ThenByDescending(item => item.FechaRegistroUtc)
                        .ToListAsync(cancellationToken);

                CompletarEstadoArchivos(candidatos);
                resumen.ArchivosFaltantes =
                    candidatos.Count(item => !item.ArchivoExiste);

                IEnumerable<FotoTerrenoAdminItemDto> filtrados =
                    filtroProblema == "ARCHIVO_FALTANTE"
                        ? candidatos.Where(item =>
                            !item.ArchivoExiste)
                        : candidatos.Where(item =>
                            !item.ArchivoExiste ||
                            item.EsHuerfana ||
                            !item.TerrenoActivo);

                List<FotoTerrenoAdminItemDto> materializados =
                    filtrados.ToList();

                totalRegistros = materializados.Count;
                resumen.Total = totalRegistros;
                resumen.Activas = materializados.Count(item => item.Activo);
                resumen.Inactivas = materializados.Count(item => !item.Activo);
                resumen.Portadas = materializados.Count(item =>
                    item.Activo && item.EsPortada);
                resumen.Huerfanas = materializados.Count(item =>
                    item.EsHuerfana);

                items = materializados
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .ToList();
            }
            else
            {
                totalRegistros =
                    await query.CountAsync(
                        cancellationToken);

                items = await Proyectar(query)
                    .OrderByDescending(item => item.EsPortada)
                    .ThenByDescending(item => item.FechaCaptura)
                    .ThenByDescending(item => item.FechaRegistroUtc)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .ToListAsync(cancellationToken);

                CompletarEstadoArchivos(items);
            }

            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros /
                    (double)tamanoPagina);

            return Ok(new FotoTerrenoAdminPaginaDto
            {
                Items = items,
                Pagina = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = totalPaginas,
                TienePaginaAnterior = pagina > 1,
                TienePaginaSiguiente =
                    totalPaginas > 0 &&
                    pagina < totalPaginas,
                Resumen = resumen
            });
        }

        [HttpGet("terrenos")]
        public async Task<IActionResult> BuscarTerrenos(
            [FromQuery] string? buscar = null,
            [FromQuery] int limite = 30,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            limite = Math.Clamp(limite, 5, 60);
            string texto =
                (buscar ?? string.Empty).Trim();

            IQueryable<Terreno> query =
                db.Terreno
                    .AsNoTracking()
                    .Where(item => item.activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(item =>
                    item.codigoTerreno.Contains(texto) ||
                    item.direccionTerreno.Contains(texto) ||
                    item.RelacionesPropietario.Any(
                        relacion =>
                            relacion.activo &&
                            (relacion.Propietario.nombreCompleto
                                 .Contains(texto) ||
                             relacion.Propietario.identificacion
                                 .Contains(texto))));
            }

            List<TerrenoFotoSelectorDto> terrenos =
                await query
                    .OrderBy(item => item.codigoTerreno)
                    .Take(limite)
                    .Select(item =>
                        new TerrenoFotoSelectorDto
                        {
                            TerrenoId = item.terrenoId,
                            CodigoTerreno =
                                item.codigoTerreno,
                            Direccion =
                                item.direccionTerreno,
                            PropietarioId =
                                item.RelacionesPropietario
                                    .Where(relacion =>
                                        relacion.activo)
                                    .Select(relacion =>
                                        (int?)relacion.propietarioId)
                                    .FirstOrDefault(),
                            Propietario =
                                item.RelacionesPropietario
                                    .Where(relacion =>
                                        relacion.activo)
                                    .Select(relacion =>
                                        relacion.Propietario
                                            .nombreCompleto)
                                    .FirstOrDefault() ??
                                string.Empty,
                            CantidadFotosActivas =
                                item.FotosTerreno.Count(foto =>
                                    foto.activo)
                        })
                    .ToListAsync(cancellationToken);

            return Ok(terrenos);
        }

        [HttpPost("subir")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> Subir(
            [FromForm] FotoTerrenoAdminSubirDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid ||
                dto.Foto is null ||
                dto.Foto.Length <= 0)
            {
                return BadRequest(Error(
                    "Seleccione una fotografía válida."));
            }

            bool terrenoActivo = await db.Terreno
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.terrenoId == dto.TerrenoId &&
                        item.activo,
                    cancellationToken);

            if (!terrenoActivo)
            {
                return BadRequest(Error(
                    "El terreno seleccionado no existe o está inactivo."));
            }

            string? rutaNueva = null;

            try
            {
                rutaNueva =
                    await imageService.GuardarImagenWebpAsync(
                        dto.Foto,
                        "terrenos",
                        1600,
                        1600,
                        72);

                bool existePortada = await db.FotoTerreno
                    .AnyAsync(
                        item =>
                            item.terrenoId == dto.TerrenoId &&
                            item.activo &&
                            item.esPortada,
                        cancellationToken);

                if (dto.EstablecerComoPortada)
                {
                    await QuitarPortadasAsync(
                        dto.TerrenoId,
                        fotoExceptuadaId: null,
                        cancellationToken);
                }

                var entidad = new FotoTerreno
                {
                    terrenoId = dto.TerrenoId,
                    urlFotoTerreno =
                        ConstruirUrlPublica(rutaNueva),
                    tituloFotoTerreno =
                        NormalizarTexto(dto.Titulo, 150),
                    descripcionFotoTerreno =
                        NormalizarTexto(dto.Descripcion, 600),
                    nombreArchivoOriginal =
                        NormalizarTexto(
                            dto.Foto.FileName,
                            255),
                    fechaRegistroUtc = DateTime.UtcNow,
                    fechaCaptura = dto.FechaCaptura?.Date,
                    esPortada =
                        dto.EstablecerComoPortada ||
                        !existePortada,
                    activo = true
                };

                db.FotoTerreno.Add(entidad);
                await db.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Fotografía guardada correctamente.",
                    data = entidad.fotoTerrenoId
                });
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(rutaNueva))
                    EliminarImagenSinInterrumpir(rutaNueva);

                throw;
            }
        }

        [HttpPut("{fotoTerrenoId:int}")]
        public async Task<IActionResult> ActualizarMetadatos(
            int fotoTerrenoId,
            [FromBody] FotoTerrenoAdminGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? entidad = await db.FotoTerreno
                .FirstOrDefaultAsync(
                    item =>
                        item.fotoTerrenoId == fotoTerrenoId,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error(
                    "La fotografía no existe."));

            entidad.tituloFotoTerreno =
                NormalizarTexto(dto.Titulo, 150);
            entidad.descripcionFotoTerreno =
                NormalizarTexto(dto.Descripcion, 600);
            entidad.fechaCaptura =
                dto.FechaCaptura?.Date;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Información de la fotografía actualizada correctamente."));
        }

        [HttpPost("{fotoTerrenoId:int}/portada")]
        public async Task<IActionResult> EstablecerPortada(
            int fotoTerrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? entidad = await db.FotoTerreno
                .FirstOrDefaultAsync(
                    item =>
                        item.fotoTerrenoId == fotoTerrenoId &&
                        item.activo,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "La fotografía no existe o está inactiva."));
            }

            await QuitarPortadasAsync(
                entidad.terrenoId,
                entidad.fotoTerrenoId,
                cancellationToken);

            entidad.esPortada = true;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Fotografía establecida como portada."));
        }

        [HttpDelete("{fotoTerrenoId:int}")]
        public async Task<IActionResult> Desactivar(
            int fotoTerrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? entidad = await db.FotoTerreno
                .FirstOrDefaultAsync(
                    item =>
                        item.fotoTerrenoId == fotoTerrenoId &&
                        item.activo,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "La fotografía no existe o ya está inactiva."));
            }

            bool eraPortada = entidad.esPortada;
            entidad.activo = false;
            entidad.esPortada = false;

            if (eraPortada)
            {
                await AsignarPortadaDisponibleAsync(
                    entidad.terrenoId,
                    entidad.fotoTerrenoId,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Fotografía desactivada correctamente."));
        }

        [HttpPost("{fotoTerrenoId:int}/reactivar")]
        public async Task<IActionResult> Reactivar(
            int fotoTerrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? entidad = await db.FotoTerreno
                .Include(item => item.Terreno)
                .FirstOrDefaultAsync(
                    item =>
                        item.fotoTerrenoId == fotoTerrenoId &&
                        !item.activo,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "La fotografía inactiva no existe."));
            }

            if (entidad.Terreno is null ||
                !entidad.Terreno.activo)
            {
                return Conflict(Error(
                    "Debe reactivar o corregir primero el terreno relacionado."));
            }

            entidad.activo = true;

            bool existePortada = await db.FotoTerreno
                .AnyAsync(
                    item =>
                        item.terrenoId == entidad.terrenoId &&
                        item.activo &&
                        item.esPortada,
                    cancellationToken);

            if (!existePortada)
                entidad.esPortada = true;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Fotografía reactivada correctamente."));
        }

        [HttpDelete("{fotoTerrenoId:int}/definitivo")]
        public async Task<IActionResult> EliminarDefinitivamente(
            int fotoTerrenoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            FotoTerreno? entidad = await db.FotoTerreno
                .FirstOrDefaultAsync(
                    item =>
                        item.fotoTerrenoId == fotoTerrenoId,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error(
                    "La fotografía no existe."));

            if (entidad.activo)
            {
                return Conflict(Error(
                    "Desactive la fotografía antes de eliminarla definitivamente."));
            }

            string ruta = entidad.urlFotoTerreno;

            db.FotoTerreno.Remove(entidad);
            await db.SaveChangesAsync(cancellationToken);

            EliminarImagenSinInterrumpir(ruta);

            return Ok(Exito(
                "Fotografía y archivo eliminados definitivamente."));
        }

        private IQueryable<FotoTerrenoAdminItemDto> Proyectar(
            IQueryable<FotoTerreno> query) =>
            query.Select(item =>
                new FotoTerrenoAdminItemDto
                {
                    FotoTerrenoId = item.fotoTerrenoId,
                    TerrenoId = item.terrenoId,
                    CodigoTerreno = item.Terreno == null
                        ? string.Empty
                        : item.Terreno.codigoTerreno,
                    DireccionTerreno = item.Terreno == null
                        ? string.Empty
                        : item.Terreno.direccionTerreno,
                    PropietarioId = item.Terreno == null
                        ? null
                        : item.Terreno.RelacionesPropietario
                            .Where(relacion => relacion.activo)
                            .Select(relacion =>
                                (int?)relacion.propietarioId)
                            .FirstOrDefault(),
                    Propietario = item.Terreno == null
                        ? string.Empty
                        : item.Terreno.RelacionesPropietario
                            .Where(relacion => relacion.activo)
                            .Select(relacion =>
                                relacion.Propietario.nombreCompleto)
                            .FirstOrDefault() ?? string.Empty,
                    IdentificacionPropietario = item.Terreno == null
                        ? string.Empty
                        : item.Terreno.RelacionesPropietario
                            .Where(relacion => relacion.activo)
                            .Select(relacion =>
                                relacion.Propietario.identificacion)
                            .FirstOrDefault() ?? string.Empty,
                    Url = item.urlFotoTerreno,
                    Titulo = item.tituloFotoTerreno,
                    Descripcion = item.descripcionFotoTerreno,
                    NombreArchivoOriginal =
                        item.nombreArchivoOriginal,
                    FechaRegistroUtc = item.fechaRegistroUtc,
                    FechaCaptura = item.fechaCaptura,
                    EsPortada = item.esPortada,
                    Activo = item.activo,
                    TerrenoActivo =
                        item.Terreno != null &&
                        item.Terreno.activo,
                    EsHuerfana = item.Terreno == null
                });

        private async Task<FotoTerrenoAdminResumenDto>
            ConstruirResumenAsync(
                IQueryable<FotoTerreno> query,
                CancellationToken cancellationToken)
        {
            FotoTerrenoAdminResumenDto? resumen =
                await query
                    .GroupBy(_ => 1)
                    .Select(grupo =>
                        new FotoTerrenoAdminResumenDto
                        {
                            Total = grupo.Count(),
                            Activas = grupo.Count(item =>
                                item.activo),
                            Inactivas = grupo.Count(item =>
                                !item.activo),
                            Portadas = grupo.Count(item =>
                                item.activo &&
                                item.esPortada),
                            Huerfanas = grupo.Count(item =>
                                item.Terreno == null)
                        })
                    .FirstOrDefaultAsync(cancellationToken);

            resumen ??= new FotoTerrenoAdminResumenDto();

            // La comprobación del disco se ejecuta únicamente cuando el
            // usuario aplica un filtro de archivos faltantes. Recorrer todas
            // las rutas en cada página degradaría el rendimiento con galerías
            // grandes.
            resumen.ArchivosFaltantes = -1;

            return resumen;
        }

        private void CompletarEstadoArchivos(
            IEnumerable<FotoTerrenoAdminItemDto> items)
        {
            foreach (FotoTerrenoAdminItemDto item in items)
            {
                item.ArchivoExiste = ArchivoExiste(item.Url);
                item.Url = ConstruirUrlVisible(item.Url);
            }
        }

        private bool ArchivoExiste(string? url)
        {
            string? rutaLocal =
                ExtraerRutaLocalTerreno(url);

            if (rutaLocal is null)
            {
                // Una URL externa pertenece al proveedor de almacenamiento y
                // no puede comprobarse mediante el sistema de archivos local.
                return Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out _);
            }

            string ruta = rutaLocal.TrimStart('/');

            string raiz = Path.GetFullPath(
                Path.Combine(
                    environment.ContentRootPath,
                    "resources",
                    "uploads",
                    "terrenos"));

            string rutaFisica = Path.GetFullPath(
                Path.Combine(
                    environment.ContentRootPath,
                    ruta.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            string prefijoSeguro =
                raiz.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            return rutaFisica.StartsWith(
                       prefijoSeguro,
                       StringComparison.OrdinalIgnoreCase) &&
                   System.IO.File.Exists(rutaFisica);
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
                // La base de datos conserva prioridad sobre la limpieza física.
                // Un bloqueo temporal del archivo no debe revertir la operación.
            }
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

        private async Task AsignarPortadaDisponibleAsync(
            int terrenoId,
            int fotoExcluidaId,
            CancellationToken cancellationToken)
        {
            FotoTerreno? siguiente =
                await db.FotoTerreno
                    .Where(item =>
                        item.terrenoId == terrenoId &&
                        item.fotoTerrenoId != fotoExcluidaId &&
                        item.activo)
                    .OrderByDescending(item =>
                        item.fechaCaptura)
                    .ThenByDescending(item =>
                        item.fechaRegistroUtc)
                    .FirstOrDefaultAsync(cancellationToken);

            if (siguiente != null)
                siguiente.esPortada = true;
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipoPermiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    CodigoPermiso,
                    tipoPermiso,
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

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };

        private static object Exito(string mensaje) =>
            new
            {
                success = true,
                message = mensaje
            };
    }
}
