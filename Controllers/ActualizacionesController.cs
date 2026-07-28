using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/actualizaciones")]
    public sealed class ActualizacionesController : ControllerBase
    {
        private const string EstadoBorrador = "BORRADOR";
        private const string EstadoPublicada = "PUBLICADA";
        private const string EstadoRevocada = "REVOCADA";

        private const long TamanoMaximoArchivo =
            1024L * 1024L * 1024L;

        private static readonly Regex PatronVersion = new(
            @"^\d+\.\d+\.\d+(?:\.\d+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ActualizacionesDbContext actualizacionesDb;
        private readonly PermisoApiService permisoApiService;
        private readonly IWebHostEnvironment environment;
        private readonly ILogger<ActualizacionesController> logger;

        public ActualizacionesController(
            ActualizacionesDbContext actualizacionesDb,
            PermisoApiService permisoApiService,
            IWebHostEnvironment environment,
            ILogger<ActualizacionesController> logger)
        {
            this.actualizacionesDb = actualizacionesDb;
            this.permisoApiService = permisoApiService;
            this.environment = environment;
            this.logger = logger;
        }

        /// <summary>
        /// Endpoint público utilizado por la aplicación instalada.
        /// </summary>
        [HttpGet("comprobar")]
        public async Task<ActionResult> Comprobar(
            [FromQuery] string plataforma,
            [FromQuery] long versionCodigo,
            [FromQuery] string canal = "PRODUCCION",
            CancellationToken cancellationToken = default)
        {
            string plataformaNormalizada = NormalizarPlataforma(plataforma);
            string canalNormalizado = NormalizarCanal(canal);

            if (string.IsNullOrWhiteSpace(plataformaNormalizada))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La plataforma debe ser ANDROID o WINDOWS."
                });
            }

            if (string.IsNullOrWhiteSpace(canalNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El canal debe ser PRODUCCION o PRUEBAS."
                });
            }

            List<ActualizacionAplicacion> disponibles = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.Estado == EstadoPublicada &&
                    x.Plataforma == plataformaNormalizada &&
                    x.Canal == canalNormalizado &&
                    x.VersionCodigo > versionCodigo)
                .OrderByDescending(x => x.VersionCodigo)
                .ToListAsync(cancellationToken);

            ActualizacionAplicacion? actualizacion =
                disponibles.FirstOrDefault();

            if (actualizacion == null)
            {
                return Ok(new
                {
                    success = true,
                    message = "La aplicación está actualizada.",
                    actualizacionDisponible = false,
                    data = (object?)null
                });
            }

            /*
             * Una versión obligatoria intermedia no puede quedar anulada por
             * una publicación posterior marcada como opcional.
             */
            bool obligatoria = disponibles.Any(x =>
                x.Obligatoria ||
                (x.VersionMinimaCodigo.HasValue &&
                 versionCodigo < x.VersionMinimaCodigo.Value));

            ActualizacionDisponibleDto data =
                MapearDisponible(actualizacion, obligatoria);

            return Ok(new
            {
                success = true,
                message = obligatoria
                    ? "Existe una actualización obligatoria."
                    : "Existe una nueva actualización disponible.",
                actualizacionDisponible = true,
                data
            });
        }

        [HttpGet("administrar")]
        public async Task<ActionResult> Administrar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] string? plataforma = null,
            [FromQuery] string? canal = null,
            [FromQuery] string? estado = null,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<ActualizacionAplicacion> query = actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(plataforma))
            {
                string valor = NormalizarPlataforma(plataforma);
                if (!string.IsNullOrWhiteSpace(valor))
                    query = query.Where(x => x.Plataforma == valor);
            }

            if (!string.IsNullOrWhiteSpace(canal))
            {
                string valor = NormalizarCanal(canal);
                if (!string.IsNullOrWhiteSpace(valor))
                    query = query.Where(x => x.Canal == valor);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                string valor = estado.Trim().ToUpperInvariant();
                if (valor is EstadoBorrador or EstadoPublicada or EstadoRevocada)
                    query = query.Where(x => x.Estado == valor);
            }

            List<ActualizacionAplicacion> registros = await query
                .OrderByDescending(x => x.FechaCreacionUtc)
                .ThenByDescending(x => x.VersionCodigo)
                .ToListAsync(cancellationToken);

            List<ActualizacionAdministracionDto> data = registros
                .Select(MapearAdministracion)
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Versiones obtenidas correctamente.",
                data
            });
        }

        [HttpGet("siguiente-version")]
        public async Task<ActionResult> SiguienteVersion(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] string plataforma,
            [FromQuery] string canal = "PRODUCCION",
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string plataformaNormalizada = NormalizarPlataforma(plataforma);
            string canalNormalizado = NormalizarCanal(canal);

            if (string.IsNullOrWhiteSpace(plataformaNormalizada) ||
                string.IsNullOrWhiteSpace(canalNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La plataforma o el canal no son válidos."
                });
            }

            ActualizacionAplicacion? ultima = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.Plataforma == plataformaNormalizada &&
                    x.Canal == canalNormalizado)
                .OrderByDescending(x => x.VersionCodigo)
                .FirstOrDefaultAsync(cancellationToken);

            string ultimaVersionNombre =
                ultima?.VersionNombre ?? "1.0.1";

            long ultimaVersionCodigo =
                ultima?.VersionCodigo ?? 2;

            var data = new SiguienteVersionDto
            {
                Plataforma = plataformaNormalizada,
                Canal = canalNormalizado,
                UltimaVersionNombre = ultimaVersionNombre,
                UltimaVersionCodigo = ultimaVersionCodigo,
                SiguienteVersionNombre =
                    IncrementarVersionCorreccion(ultimaVersionNombre),
                SiguienteVersionCodigo = ultimaVersionCodigo + 1
            };

            return Ok(new
            {
                success = true,
                message = "Siguiente versión calculada correctamente.",
                data
            });
        }

        [HttpPost("subir")]
        [RequestSizeLimit(TamanoMaximoArchivo)]
        [RequestFormLimits(MultipartBodyLengthLimit = TamanoMaximoArchivo)]
        public async Task<ActionResult> Subir(
            [FromForm] ActualizacionSubirDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActionResult? validacion = ValidarSubida(dto);
            if (validacion != null)
                return validacion;

            string plataforma = NormalizarPlataforma(dto.Plataforma);
            string canal = NormalizarCanal(dto.Canal);
            string versionNombre = dto.VersionNombre.Trim();

            long? ultimaCompilacion = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.Plataforma == plataforma &&
                    x.Canal == canal)
                .Select(x => (long?)x.VersionCodigo)
                .MaxAsync(cancellationToken);

            if (ultimaCompilacion.HasValue &&
                dto.VersionCodigo <= ultimaCompilacion.Value)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"La compilación debe ser mayor que {ultimaCompilacion.Value}, que es la última registrada para esta plataforma y canal."
                });
            }

            string extension = Path
                .GetExtension(dto.Archivo.FileName)
                .ToLowerInvariant();

            string carpeta = Path.Combine(
                environment.ContentRootPath,
                "resources",
                "uploads",
                "actualizaciones",
                plataforma.ToLowerInvariant(),
                canal.ToLowerInvariant());

            Directory.CreateDirectory(carpeta);

            string nombreAlmacenado =
                $"{dto.VersionCodigo}_{Guid.NewGuid():N}{extension}";

            string rutaFisica = Path.Combine(
                carpeta,
                nombreAlmacenado);

            try
            {
                await using (FileStream destino = new(
                    rutaFisica,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    useAsync: true))
                {
                    await dto.Archivo.CopyToAsync(
                        destino,
                        cancellationToken);
                }

                string hashSha256 = await CalcularSha256Async(
                    rutaFisica,
                    cancellationToken);

                DateTime ahoraUtc = DateTime.UtcNow;

                var entidad = new ActualizacionAplicacion
                {
                    Plataforma = plataforma,
                    Canal = canal,
                    VersionNombre = versionNombre,
                    VersionCodigo = dto.VersionCodigo,
                    NotasVersion = dto.NotasVersion?.Trim() ?? string.Empty,
                    Obligatoria = dto.Obligatoria,
                    VersionMinimaCodigo = dto.VersionMinimaCodigo,
                    Estado = EstadoBorrador,
                    NombreArchivo = Path.GetFileName(dto.Archivo.FileName),
                    NombreArchivoAlmacenado = nombreAlmacenado,
                    RutaArchivo = rutaFisica,
                    TipoContenido = ObtenerTipoContenido(extension),
                    TamanoBytes = dto.Archivo.Length,
                    HashSha256 = hashSha256,
                    UsuarioCreacionId = usuarioSesionId!.Value,
                    UsuarioUltimaModificacionId = usuarioSesionId.Value,
                    FechaCreacionUtc = ahoraUtc,
                    FechaUltimaModificacionUtc = ahoraUtc,
                    Activo = true
                };

                actualizacionesDb.ActualizacionesAplicacion.Add(entidad);
                await actualizacionesDb.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La versión fue cargada como borrador. Revísela y publíquela cuando esté lista.",
                    data = MapearAdministracion(entidad)
                });
            }
            catch
            {
                EliminarArchivoSeguro(rutaFisica);
                throw;
            }
        }

        [HttpPut("{id:int}/configuracion")]
        public async Task<ActionResult> Configurar(
            int id,
            [FromBody] ActualizacionConfiguracionDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActualizacionAplicacion? entidad = await BuscarActivaAsync(
                id,
                cancellationToken);

            if (entidad == null)
                return NoEncontrada();

            entidad.NotasVersion = dto.NotasVersion?.Trim() ?? string.Empty;
            entidad.Obligatoria = dto.Obligatoria;
            entidad.VersionMinimaCodigo = dto.VersionMinimaCodigo;
            entidad.UsuarioUltimaModificacionId = usuarioSesionId!.Value;
            entidad.FechaUltimaModificacionUtc = DateTime.UtcNow;

            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Configuración actualizada correctamente.",
                data = MapearAdministracion(entidad)
            });
        }

        [HttpPut("{id:int}/publicar")]
        public async Task<ActionResult> Publicar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActualizacionAplicacion? entidad = await BuscarActivaAsync(
                id,
                cancellationToken);

            if (entidad == null)
                return NoEncontrada();

            if (entidad.Estado == EstadoRevocada)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Una versión revocada no puede volver a publicarse."
                });
            }

            if (!System.IO.File.Exists(entidad.RutaArchivo))
            {
                return Conflict(new
                {
                    success = false,
                    message = "El archivo físico de esta versión no existe en el servidor."
                });
            }

            bool existeVersionSuperiorPublicada = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Activo &&
                        x.Estado == EstadoPublicada &&
                        x.Plataforma == entidad.Plataforma &&
                        x.Canal == entidad.Canal &&
                        x.VersionCodigo > entidad.VersionCodigo,
                    cancellationToken);

            if (existeVersionSuperiorPublicada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una compilación superior publicada para esta plataforma y canal."
                });
            }

            DateTime ahoraUtc = DateTime.UtcNow;

            entidad.Estado = EstadoPublicada;
            entidad.FechaPublicacionUtc ??= ahoraUtc;
            entidad.FechaUltimaModificacionUtc = ahoraUtc;
            entidad.UsuarioUltimaModificacionId = usuarioSesionId!.Value;

            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Versión publicada correctamente.",
                data = MapearAdministracion(entidad)
            });
        }

        [HttpPut("{id:int}/revocar")]
        public async Task<ActionResult> Revocar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActualizacionAplicacion? entidad = await BuscarActivaAsync(
                id,
                cancellationToken);

            if (entidad == null)
                return NoEncontrada();

            entidad.Estado = EstadoRevocada;
            entidad.UsuarioUltimaModificacionId = usuarioSesionId!.Value;
            entidad.FechaUltimaModificacionUtc = DateTime.UtcNow;

            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Versión revocada correctamente.",
                data = MapearAdministracion(entidad)
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Eliminar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActualizacionAplicacion? entidad = await BuscarActivaAsync(
                id,
                cancellationToken);

            if (entidad == null)
                return NoEncontrada();

            if (entidad.Estado != EstadoBorrador)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo se pueden eliminar borradores. Las versiones publicadas deben revocarse para conservar el historial."
                });
            }

            actualizacionesDb.ActualizacionesAplicacion.Remove(entidad);
            await actualizacionesDb.SaveChangesAsync(cancellationToken);
            EliminarArchivoSeguro(entidad.RutaArchivo);

            return Ok(new
            {
                success = true,
                message = "Borrador eliminado correctamente."
            });
        }

        [HttpGet("descargar/{id:int}")]
        public async Task<IActionResult> Descargar(
            int id,
            CancellationToken cancellationToken = default)
        {
            ActualizacionAplicacion? entidad = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.ActualizacionAplicacionId == id &&
                        x.Activo &&
                        x.Estado == EstadoPublicada,
                    cancellationToken);

            if (entidad == null ||
                !System.IO.File.Exists(entidad.RutaArchivo))
            {
                return NotFound(new
                {
                    success = false,
                    message = "La actualización solicitada no está disponible."
                });
            }

            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-Archivo-SHA256"] = entidad.HashSha256;

            return PhysicalFile(
                entidad.RutaArchivo,
                entidad.TipoContenido,
                entidad.NombreArchivo,
                enableRangeProcessing: true);
        }

        private ActionResult? ValidarSubida(ActualizacionSubirDto dto)
        {
            if (dto.Archivo == null || dto.Archivo.Length <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Debe seleccionar un archivo de actualización."
                });
            }

            if (dto.Archivo.Length > TamanoMaximoArchivo)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El archivo no puede superar 1 GB."
                });
            }

            string plataforma = NormalizarPlataforma(dto.Plataforma);
            string canal = NormalizarCanal(dto.Canal);

            if (string.IsNullOrWhiteSpace(plataforma))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La plataforma debe ser ANDROID o WINDOWS."
                });
            }

            if (string.IsNullOrWhiteSpace(canal))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El canal debe ser PRODUCCION o PRUEBAS."
                });
            }

            string versionNombre = dto.VersionNombre?.Trim() ?? string.Empty;

            if (!PatronVersion.IsMatch(versionNombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La versión debe usar el formato 1.0.2 o 1.0.2.0."
                });
            }

            if (dto.VersionMinimaCodigo.HasValue &&
                dto.VersionMinimaCodigo.Value > dto.VersionCodigo)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La compilación mínima no puede ser mayor que la compilación publicada."
                });
            }

            string extension = Path
                .GetExtension(dto.Archivo.FileName)
                .ToLowerInvariant();

            bool extensionValida = plataforma switch
            {
                "ANDROID" => extension == ".apk",
                "WINDOWS" => extension is
                    ".msix" or
                    ".msixbundle" or
                    ".appinstaller" or
                    ".exe",
                _ => false
            };

            if (!extensionValida)
            {
                return BadRequest(new
                {
                    success = false,
                    message = plataforma == "ANDROID"
                        ? "Android solo admite archivos APK."
                        : "Windows admite MSIX, MSIXBUNDLE, APPINSTALLER o EXE."
                });
            }

            return null;
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    ActualizacionesDatabaseInitializer.CodigoInterfaz,
                    permiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje
                });
        }

        private Task<ActualizacionAplicacion?> BuscarActivaAsync(
            int id,
            CancellationToken cancellationToken) =>
            actualizacionesDb.ActualizacionesAplicacion
                .FirstOrDefaultAsync(
                    x =>
                        x.ActualizacionAplicacionId == id &&
                        x.Activo,
                    cancellationToken);

        private NotFoundObjectResult NoEncontrada() =>
            NotFound(new
            {
                success = false,
                message = "La versión no fue encontrada."
            });

        private ActualizacionDisponibleDto MapearDisponible(
            ActualizacionAplicacion entidad,
            bool obligatoria) =>
            new()
            {
                ActualizacionAplicacionId =
                    entidad.ActualizacionAplicacionId,
                Plataforma = entidad.Plataforma,
                Canal = entidad.Canal,
                VersionNombre = entidad.VersionNombre,
                VersionCodigo = entidad.VersionCodigo,
                NotasVersion = entidad.NotasVersion,
                Obligatoria = obligatoria,
                VersionMinimaCodigo = entidad.VersionMinimaCodigo,
                NombreArchivo = entidad.NombreArchivo,
                TipoContenido = entidad.TipoContenido,
                TamanoBytes = entidad.TamanoBytes,
                HashSha256 = entidad.HashSha256,
                UrlDescarga = ConstruirUrlDescarga(
                    entidad.ActualizacionAplicacionId),
                FechaPublicacionUtc = entidad.FechaPublicacionUtc
            };

        private ActualizacionAdministracionDto MapearAdministracion(
            ActualizacionAplicacion entidad) =>
            new()
            {
                ActualizacionAplicacionId =
                    entidad.ActualizacionAplicacionId,
                Plataforma = entidad.Plataforma,
                Canal = entidad.Canal,
                VersionNombre = entidad.VersionNombre,
                VersionCodigo = entidad.VersionCodigo,
                NotasVersion = entidad.NotasVersion,
                Obligatoria = entidad.Obligatoria,
                VersionMinimaCodigo = entidad.VersionMinimaCodigo,
                Estado = entidad.Estado,
                NombreArchivo = entidad.NombreArchivo,
                TipoContenido = entidad.TipoContenido,
                TamanoBytes = entidad.TamanoBytes,
                HashSha256 = entidad.HashSha256,
                UsuarioCreacionId = entidad.UsuarioCreacionId,
                UsuarioUltimaModificacionId =
                    entidad.UsuarioUltimaModificacionId,
                FechaCreacionUtc = entidad.FechaCreacionUtc,
                FechaUltimaModificacionUtc =
                    entidad.FechaUltimaModificacionUtc,
                FechaPublicacionUtc = entidad.FechaPublicacionUtc,
                UrlDescarga = entidad.Estado == EstadoPublicada
                    ? ConstruirUrlDescarga(
                        entidad.ActualizacionAplicacionId)
                    : string.Empty
            };

        private string ConstruirUrlDescarga(int id)
        {
            string baseUrl =
                $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

            return $"{baseUrl.TrimEnd('/')}/api/actualizaciones/descargar/{id}";
        }

        private static string NormalizarPlataforma(string? valor)
        {
            string normalizado =
                (valor ?? string.Empty).Trim().ToUpperInvariant();

            return normalizado switch
            {
                "ANDROID" => "ANDROID",
                "WINDOWS" => "WINDOWS",
                "WINUI" => "WINDOWS",
                _ => string.Empty
            };
        }

        private static string NormalizarCanal(string? valor)
        {
            string normalizado =
                (valor ?? string.Empty).Trim().ToUpperInvariant();

            return normalizado switch
            {
                "PRODUCCION" => "PRODUCCION",
                "PRODUCCIÓN" => "PRODUCCION",
                "PRUEBAS" => "PRUEBAS",
                _ => string.Empty
            };
        }

        private static string IncrementarVersionCorreccion(
            string versionActual)
        {
            string[] partes = versionActual.Split('.');

            if (partes.Length < 3 ||
                !int.TryParse(partes[0], out int mayor) ||
                !int.TryParse(partes[1], out int menor) ||
                !int.TryParse(partes[2], out int correccion))
            {
                return "1.0.2";
            }

            return $"{mayor}.{menor}.{correccion + 1}";
        }

        private static string ObtenerTipoContenido(string extension) =>
            extension switch
            {
                ".apk" => "application/vnd.android.package-archive",
                ".msix" => "application/msix",
                ".msixbundle" => "application/msixbundle",
                ".appinstaller" => "application/xml",
                ".exe" => "application/vnd.microsoft.portable-executable",
                _ => "application/octet-stream"
            };

        private static async Task<string> CalcularSha256Async(
            string rutaArchivo,
            CancellationToken cancellationToken)
        {
            await using FileStream stream = new(
                rutaArchivo,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);

            byte[] hash = await SHA256.HashDataAsync(
                stream,
                cancellationToken);

            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private void EliminarArchivoSeguro(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta) ||
                !System.IO.File.Exists(ruta))
            {
                return;
            }

            try
            {
                System.IO.File.Delete(ruta);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No fue posible eliminar el archivo de actualización {Ruta}.",
                    ruta);
            }
        }
    }
}
