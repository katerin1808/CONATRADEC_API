using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/actualizaciones/descargas")]
    public sealed class ActualizacionesDescargasController : ControllerBase
    {
        private const string EstadoActiva = "ACTIVA";
        private const string EstadoUtilizada = "UTILIZADA";
        private const string EstadoVencida = "VENCIDA";
        private const string EstadoRevocada = "REVOCADA";
        private const string EstadoBloqueada = "BLOQUEADA";
        private const string EstadoPublicada = "PUBLICADA";

        private const int IntentosPermitidos = 8;
        private static readonly TimeSpan VentanaIntentos =
            TimeSpan.FromMinutes(15);

        private static readonly TimeSpan VigenciaPermisoDescarga =
            TimeSpan.FromHours(2);

        private const string CookieDispositivo =
            "cntr_descarga_device";

        private const string AlfabetoLlave =
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly ActualizacionesDbContext actualizacionesDb;
        private readonly PermisoApiService permisoApiService;
        private readonly IWebHostEnvironment environment;
        private readonly ILogger<ActualizacionesDescargasController> logger;

        public ActualizacionesDescargasController(
            ActualizacionesDbContext actualizacionesDb,
            PermisoApiService permisoApiService,
            IWebHostEnvironment environment,
            ILogger<ActualizacionesDescargasController> logger)
        {
            this.actualizacionesDb = actualizacionesDb;
            this.permisoApiService = permisoApiService;
            this.environment = environment;
            this.logger = logger;
        }

        [HttpGet("portal")]
        public async Task<ActionResult> Portal(
            [FromQuery] string plataforma,
            [FromQuery] string canal = "PRODUCCION",
            CancellationToken cancellationToken = default)
        {
            string plataformaNormalizada =
                NormalizarPlataforma(plataforma);

            string canalNormalizado =
                NormalizarCanal(canal);

            if (string.IsNullOrWhiteSpace(plataformaNormalizada) ||
                string.IsNullOrWhiteSpace(canalNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La plataforma o el canal no son válidos."
                });
            }

            ActualizacionAplicacion? version = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.Estado == EstadoPublicada &&
                    x.Plataforma == plataformaNormalizada &&
                    x.Canal == canalNormalizado)
                .OrderByDescending(x => x.VersionCodigo)
                .FirstOrDefaultAsync(cancellationToken);

            if (version == null)
            {
                return Ok(new
                {
                    success = true,
                    message =
                        "No existe una versión publicada para este destino.",
                    data = (object?)null
                });
            }

            /*
             * La publicación en la base de datos no es suficiente. Si una
             * publicación del backend eliminó la carpeta de instaladores, el
             * portal no debe anunciar una descarga que fallará al validar la
             * llave.
             */
            if (!System.IO.File.Exists(version.RutaArchivo))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message =
                            "La versión está publicada, pero el archivo físico del instalador ya no existe en el servidor. Vuelva a subir y publicar el instalador.",
                        code = "INSTALLER_FILE_MISSING"
                    });
            }

            return Ok(new
            {
                success = true,
                message = "Versión publicada obtenida correctamente.",
                data = MapearPortal(version)
            });
        }

        [HttpGet("llaves")]
        public async Task<ActionResult> ListarLlaves(
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

            await ActualizarLlavesVencidasAsync(cancellationToken);

            IQueryable<ActualizacionLlaveDescarga> query =
                actualizacionesDb.LlavesDescarga
                    .AsNoTracking()
                    .Where(x => x.Activo);

            string plataformaNormalizada =
                NormalizarPlataforma(plataforma);

            string canalNormalizado =
                NormalizarCanal(canal);

            string estadoNormalizado =
                NormalizarEstado(estado);

            if (!string.IsNullOrWhiteSpace(plataformaNormalizada))
                query = query.Where(x =>
                    x.Plataforma == plataformaNormalizada);

            if (!string.IsNullOrWhiteSpace(canalNormalizado))
                query = query.Where(x =>
                    x.Canal == canalNormalizado);

            if (!string.IsNullOrWhiteSpace(estadoNormalizado))
                query = query.Where(x =>
                    x.Estado == estadoNormalizado);

            List<LlaveDescargaListadoDto> data = await query
                .OrderByDescending(x => x.FechaCreacionUtc)
                .Take(500)
                .Select(x => new LlaveDescargaListadoDto
                {
                    ActualizacionLlaveDescargaId =
                        x.ActualizacionLlaveDescargaId,
                    LlaveEnmascarada =
                        "CNTR-••••-••••-" + x.UltimosCaracteres,
                    Plataforma = x.Plataforma,
                    Canal = x.Canal,
                    Estado = x.Estado,
                    Destinatario = x.Destinatario,
                    Observacion = x.Observacion,
                    CantidadMaximaUsos = x.CantidadMaximaUsos,
                    CantidadUsos = x.CantidadUsos,
                    UsuarioCreacionId = x.UsuarioCreacionId,
                    UsuarioRevocacionId = x.UsuarioRevocacionId,
                    FechaCreacionUtc = x.FechaCreacionUtc,
                    FechaExpiracionUtc = x.FechaExpiracionUtc,
                    FechaUltimoUsoUtc = x.FechaUltimoUsoUtc,
                    FechaRevocacionUtc = x.FechaRevocacionUtc
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Llaves obtenidas correctamente.",
                data
            });
        }

        [HttpPost("llaves")]
        public async Task<ActionResult> CrearLlave(
            [FromBody] CrearLlaveDescargaDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string plataforma = NormalizarPlataforma(dto.Plataforma);
            string canal = NormalizarCanal(dto.Canal);

            if (string.IsNullOrWhiteSpace(plataforma) ||
                string.IsNullOrWhiteSpace(canal))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La plataforma o el canal no son válidos."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.Destinatario))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Indique el destinatario o responsable de la llave."
                });
            }

            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime expiracionUtc = dto.FechaExpiracionUtc?.ToUniversalTime()
                ?? ahoraUtc.AddHours(dto.VigenciaHoras ?? 24);

            if (expiracionUtc <= ahoraUtc.AddMinutes(1))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fecha de expiración debe ser posterior a la fecha actual."
                });
            }

            string llave = GenerarLlave();
            string hash = CalcularHashLlave(llave);
            string ultimos = llave[^4..];

            var entidad = new ActualizacionLlaveDescarga
            {
                HashLlave = hash,
                UltimosCaracteres = ultimos,
                Plataforma = plataforma,
                Canal = canal,
                Estado = EstadoActiva,
                Destinatario = dto.Destinatario.Trim(),
                Observacion = dto.Observacion?.Trim() ?? string.Empty,
                CantidadMaximaUsos = dto.CantidadMaximaUsos,
                CantidadUsos = 0,
                UsuarioCreacionId = usuarioSesionId!.Value,
                FechaCreacionUtc = ahoraUtc,
                FechaExpiracionUtc = expiracionUtc,
                Activo = true
            };

            actualizacionesDb.LlavesDescarga.Add(entidad);
            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            var data = new LlaveDescargaCreadaDto
            {
                ActualizacionLlaveDescargaId =
                    entidad.ActualizacionLlaveDescargaId,
                Llave = llave,
                LlaveEnmascarada =
                    "CNTR-••••-••••-" + ultimos,
                Plataforma = entidad.Plataforma,
                Canal = entidad.Canal,
                Estado = entidad.Estado,
                Destinatario = entidad.Destinatario,
                Observacion = entidad.Observacion,
                CantidadMaximaUsos = entidad.CantidadMaximaUsos,
                CantidadUsos = entidad.CantidadUsos,
                UsuarioCreacionId = entidad.UsuarioCreacionId,
                FechaCreacionUtc = entidad.FechaCreacionUtc,
                FechaExpiracionUtc = entidad.FechaExpiracionUtc
            };

            return Ok(new
            {
                success = true,
                message = "Llave generada correctamente. Cópiela ahora; no volverá a mostrarse completa.",
                data
            });
        }

        [HttpPut("llaves/{id:int}/revocar")]
        public async Task<ActionResult> RevocarLlave(
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

            ActualizacionLlaveDescarga? entidad =
                await actualizacionesDb.LlavesDescarga
                    .FirstOrDefaultAsync(
                        x =>
                            x.ActualizacionLlaveDescargaId == id &&
                            x.Activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La llave no fue encontrada."
                });
            }

            if (entidad.Estado == EstadoRevocada)
            {
                return Ok(new
                {
                    success = true,
                    message = "La llave ya se encontraba revocada.",
                    data = MapearLlave(entidad)
                });
            }

            entidad.Estado = EstadoRevocada;
            entidad.UsuarioRevocacionId = usuarioSesionId!.Value;
            entidad.FechaRevocacionUtc = DateTime.UtcNow;

            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Llave revocada correctamente.",
                data = MapearLlave(entidad)
            });
        }

        [HttpPut("llaves/{id:int}/bloquear")]
        public async Task<ActionResult> BloquearLlave(
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

            ActualizacionLlaveDescarga? entidad =
                await actualizacionesDb.LlavesDescarga
                    .FirstOrDefaultAsync(
                        x =>
                            x.ActualizacionLlaveDescargaId == id &&
                            x.Activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La llave no fue encontrada."
                });
            }

            if (entidad.Estado == EstadoBloqueada)
            {
                return Ok(new
                {
                    success = true,
                    message = "La llave ya se encontraba bloqueada.",
                    data = MapearLlave(entidad)
                });
            }

            if (entidad.Estado != EstadoActiva)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo una llave activa puede bloquearse temporalmente."
                });
            }

            entidad.Estado = EstadoBloqueada;
            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Llave bloqueada temporalmente.",
                data = MapearLlave(entidad)
            });
        }

        [HttpPut("llaves/{id:int}/reactivar")]
        public async Task<ActionResult> ReactivarLlave(
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

            ActualizacionLlaveDescarga? entidad =
                await actualizacionesDb.LlavesDescarga
                    .FirstOrDefaultAsync(
                        x =>
                            x.ActualizacionLlaveDescargaId == id &&
                            x.Activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La llave no fue encontrada."
                });
            }

            if (entidad.Estado != EstadoBloqueada)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Solo una llave bloqueada puede reactivarse."
                });
            }

            if (entidad.FechaExpiracionUtc <= DateTime.UtcNow)
            {
                entidad.Estado = EstadoVencida;
                await actualizacionesDb.SaveChangesAsync(cancellationToken);

                return BadRequest(new
                {
                    success = false,
                    message = "La llave venció y ya no puede reactivarse."
                });
            }

            if (entidad.CantidadUsos >= entidad.CantidadMaximaUsos)
            {
                entidad.Estado = EstadoUtilizada;
                await actualizacionesDb.SaveChangesAsync(cancellationToken);

                return BadRequest(new
                {
                    success = false,
                    message =
                        "La llave ya alcanzó su límite de usos y no puede reactivarse."
                });
            }

            entidad.Estado = EstadoActiva;
            await actualizacionesDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Llave reactivada correctamente.",
                data = MapearLlave(entidad)
            });
        }

        [HttpGet("auditoria")]
        public async Task<ActionResult> Auditoria(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] int limite = 200,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            limite = Math.Clamp(limite, 1, 1000);

            List<AuditoriaDescargaDto> data = await actualizacionesDb
                .AuditoriaDescargas
                .AsNoTracking()
                .OrderByDescending(x => x.FechaUtc)
                .Take(limite)
                .Select(x => new AuditoriaDescargaDto
                {
                    ActualizacionDescargaAuditoriaId =
                        x.ActualizacionDescargaAuditoriaId,
                    ActualizacionLlaveDescargaId =
                        x.ActualizacionLlaveDescargaId,
                    ActualizacionAplicacionId =
                        x.ActualizacionAplicacionId,
                    Resultado = x.Resultado,
                    Detalle = x.Detalle,
                    Plataforma = x.Plataforma,
                    Canal = x.Canal,
                    VersionNombre = x.VersionNombre,
                    VersionCodigo = x.VersionCodigo,
                    NombreArchivo = x.NombreArchivo,
                    IpCliente = x.IpCliente,
                    Navegador = x.Navegador,
                    SistemaOperativo = x.SistemaOperativo,
                    TipoDispositivo = x.TipoDispositivo,
                    IdentificadorDispositivoWeb =
                        x.IdentificadorDispositivoWeb,
                    Destinatario = x.Destinatario,
                    UsuarioGeneradorId = x.UsuarioGeneradorId,
                    FechaUtc = x.FechaUtc
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Auditoría obtenida correctamente.",
                data
            });
        }

        [HttpPost("validar")]
        public async Task<ActionResult> Validar(
            [FromBody] ValidarLlaveDescargaDto dto,
            CancellationToken cancellationToken = default)
        {
            ResultadoAutorizacion resultado = await AutorizarAsync(
                dto,
                cancellationToken);

            if (!resultado.Exito)
            {
                return StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje
                    });
            }

            return Ok(new
            {
                success = true,
                message = "Llave validada. La descarga fue autorizada.",
                data = resultado.Data
            });
        }

        [HttpPost("validar-formulario")]
        [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
        public async Task<IActionResult> ValidarFormulario(
            [FromForm] ValidarLlaveFormularioDto dto,
            CancellationToken cancellationToken = default)
        {
            ResultadoAutorizacion resultado = await AutorizarAsync(
                dto,
                cancellationToken);

            if (!resultado.Exito || resultado.Data == null)
            {
                return PaginaResultado(
                    resultado.CodigoEstado,
                    "Descarga no autorizada",
                    resultado.Mensaje,
                    dto.UrlRetorno,
                    esError: true);
            }

            Response.StatusCode = StatusCodes.Status303SeeOther;
            Response.Headers["Location"] = resultado.Data.UrlDescarga;
            return new EmptyResult();
        }

        private async Task<ResultadoAutorizacion> AutorizarAsync(
            ValidarLlaveDescargaDto dto,
            CancellationToken cancellationToken)
        {
            ContextoCliente cliente = ObtenerContextoCliente();
            DateTime ahoraUtc = DateTime.UtcNow;

            int intentosRecientes = await actualizacionesDb
                .AuditoriaDescargas
                .AsNoTracking()
                .CountAsync(
                    x =>
                        x.IpCliente == cliente.IpCliente &&
                        x.Resultado.StartsWith("RECHAZADA") &&
                        x.FechaUtc >= ahoraUtc - VentanaIntentos,
                    cancellationToken);

            if (intentosRecientes >= IntentosPermitidos)
            {
                await RegistrarAuditoriaAsync(
                    null,
                    null,
                    string.Empty,
                    "RECHAZADA_BLOQUEO_IP",
                    "Se alcanzó el límite temporal de intentos permitidos.",
                    NormalizarPlataforma(dto.Plataforma),
                    NormalizarCanal(dto.Canal),
                    cliente,
                    cancellationToken);

                return ResultadoAutorizacion.Error(
                    StatusCodes.Status429TooManyRequests,
                    "Se realizaron demasiados intentos. Espere 15 minutos antes de volver a probar.");
            }

            string plataforma = NormalizarPlataforma(dto.Plataforma);
            string canal = NormalizarCanal(dto.Canal);
            string llaveNormalizada = NormalizarLlave(dto.Llave);

            if (string.IsNullOrWhiteSpace(plataforma) ||
                string.IsNullOrWhiteSpace(canal) ||
                llaveNormalizada.Length != 16)
            {
                await RegistrarAuditoriaAsync(
                    null,
                    null,
                    string.Empty,
                    "RECHAZADA_FORMATO",
                    "La llave, plataforma o canal no tienen un formato válido.",
                    plataforma,
                    canal,
                    cliente,
                    cancellationToken);

                return ResultadoAutorizacion.Error(
                    StatusCodes.Status400BadRequest,
                    "La llave o el destino seleccionado no son válidos.");
            }

            string hash = CalcularHashNormalizado(llaveNormalizada);

            await using var transaction =
                await actualizacionesDb.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            ActualizacionLlaveDescarga? llave =
                await actualizacionesDb.LlavesDescarga
                    .FirstOrDefaultAsync(
                        x => x.HashLlave == hash && x.Activo,
                        cancellationToken);

            if (llave == null)
            {
                await RegistrarAuditoriaAsync(
                    null,
                    null,
                    string.Empty,
                    "RECHAZADA_LLAVE",
                    "La llave no existe.",
                    plataforma,
                    canal,
                    cliente,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return ResultadoAutorizacion.Error(
                    StatusCodes.Status401Unauthorized,
                    "La llave ingresada no es válida.");
            }

            string? motivoInvalidez = ValidarEstadoLlave(
                llave,
                plataforma,
                canal,
                ahoraUtc);

            if (!string.IsNullOrWhiteSpace(motivoInvalidez))
            {
                await RegistrarAuditoriaAsync(
                    llave,
                    null,
                    string.Empty,
                    "RECHAZADA_ESTADO",
                    motivoInvalidez,
                    plataforma,
                    canal,
                    cliente,
                    cancellationToken);

                await actualizacionesDb.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ResultadoAutorizacion.Error(
                    StatusCodes.Status403Forbidden,
                    motivoInvalidez);
            }

            ActualizacionAplicacion? version = await actualizacionesDb
                .ActualizacionesAplicacion
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.Estado == EstadoPublicada &&
                    x.Plataforma == plataforma &&
                    x.Canal == canal)
                .OrderByDescending(x => x.VersionCodigo)
                .FirstOrDefaultAsync(cancellationToken);

            if (version == null ||
                !System.IO.File.Exists(version.RutaArchivo))
            {
                await RegistrarAuditoriaAsync(
                    llave,
                    version,
                    string.Empty,
                    "RECHAZADA_SIN_VERSION",
                    "No existe un instalador publicado y disponible para la llave.",
                    plataforma,
                    canal,
                    cliente,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return ResultadoAutorizacion.Error(
                    StatusCodes.Status404NotFound,
                    "Todavía no existe un instalador publicado para esta plataforma y canal.");
            }

            llave.CantidadUsos++;
            llave.FechaUltimoUsoUtc = ahoraUtc;

            if (llave.CantidadUsos >= llave.CantidadMaximaUsos)
                llave.Estado = EstadoUtilizada;

            string operacionId =
                ActualizacionDescargaTokenService.NuevaOperacionId();

            DateTime expiracionPermisoUtc =
                ahoraUtc.Add(VigenciaPermisoDescarga);

            string permiso = ActualizacionDescargaTokenService.Crear(
                environment,
                version.ActualizacionAplicacionId,
                llave.ActualizacionLlaveDescargaId,
                operacionId,
                VigenciaPermisoDescarga);

            GuardarPermisoEnCookie(
                version.ActualizacionAplicacionId,
                permiso,
                expiracionPermisoUtc);

            string urlDescarga = ConstruirUrlDescarga(
                version.ActualizacionAplicacionId);

            await RegistrarAuditoriaAsync(
                llave,
                version,
                operacionId,
                "AUTORIZADA",
                "La llave fue validada y se emitió un permiso temporal de descarga.",
                plataforma,
                canal,
                cliente,
                cancellationToken);

            await actualizacionesDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var data = new DescargaAutorizadaDto
            {
                ActualizacionAplicacionId =
                    version.ActualizacionAplicacionId,
                Plataforma = version.Plataforma,
                Canal = version.Canal,
                VersionNombre = version.VersionNombre,
                VersionCodigo = version.VersionCodigo,
                NotasVersion = version.NotasVersion,
                NombreArchivo = version.NombreArchivo,
                TamanoBytes = version.TamanoBytes,
                HashSha256 = version.HashSha256,
                UrlDescarga = urlDescarga,
                FechaExpiracionPermisoUtc = expiracionPermisoUtc
            };

            return ResultadoAutorizacion.Ok(data);
        }

        private async Task RegistrarAuditoriaAsync(
            ActualizacionLlaveDescarga? llave,
            ActualizacionAplicacion? version,
            string operacionId,
            string resultado,
            string detalle,
            string plataforma,
            string canal,
            ContextoCliente cliente,
            CancellationToken cancellationToken)
        {
            actualizacionesDb.AuditoriaDescargas.Add(
                new ActualizacionDescargaAuditoria
                {
                    ActualizacionLlaveDescargaId =
                        llave?.ActualizacionLlaveDescargaId,
                    ActualizacionAplicacionId =
                        version?.ActualizacionAplicacionId,
                    OperacionId = operacionId,
                    Resultado = resultado,
                    Detalle = Limitar(detalle, 500),
                    Plataforma = Limitar(
                        version?.Plataforma ?? plataforma,
                        20),
                    Canal = Limitar(
                        version?.Canal ?? canal,
                        20),
                    VersionNombre = Limitar(
                        version?.VersionNombre ?? string.Empty,
                        30),
                    VersionCodigo = version?.VersionCodigo,
                    NombreArchivo = Limitar(
                        version?.NombreArchivo ?? string.Empty,
                        260),
                    IpCliente = Limitar(cliente.IpCliente, 80),
                    EncabezadoForwardedFor = Limitar(
                        cliente.ForwardedFor,
                        500),
                    AgenteUsuario = Limitar(
                        cliente.AgenteUsuario,
                        1000),
                    Navegador = Limitar(cliente.Navegador, 100),
                    SistemaOperativo = Limitar(
                        cliente.SistemaOperativo,
                        100),
                    TipoDispositivo = Limitar(
                        cliente.TipoDispositivo,
                        80),
                    IdentificadorDispositivoWeb = Limitar(
                        cliente.IdentificadorDispositivoWeb,
                        100),
                    Destinatario = Limitar(
                        llave?.Destinatario ?? string.Empty,
                        200),
                    UsuarioGeneradorId = llave?.UsuarioCreacionId,
                    FechaUtc = DateTime.UtcNow
                });

            await actualizacionesDb.SaveChangesAsync(cancellationToken);
        }

        private ContextoCliente ObtenerContextoCliente()
        {
            string forwardedFor = Request.Headers["X-Forwarded-For"]
                .ToString();

            IPAddress? remota = HttpContext.Connection.RemoteIpAddress;
            string ipCliente = remota?.ToString() ?? "desconocida";

            if (EsProxyPrivado(remota) &&
                !string.IsNullOrWhiteSpace(forwardedFor))
            {
                string candidata = forwardedFor
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .FirstOrDefault() ?? string.Empty;

                if (IPAddress.TryParse(candidata, out _))
                    ipCliente = candidata;
            }

            string agente = Request.Headers["User-Agent"].ToString();
            string identificador = ObtenerOCrearIdentificadorDispositivo();

            return new ContextoCliente(
                ipCliente,
                forwardedFor,
                agente,
                DetectarNavegador(agente),
                DetectarSistemaOperativo(agente),
                DetectarTipoDispositivo(agente),
                identificador);
        }

        private string ObtenerOCrearIdentificadorDispositivo()
        {
            if (Request.Cookies.TryGetValue(
                    CookieDispositivo,
                    out string? existente) &&
                !string.IsNullOrWhiteSpace(existente) &&
                existente.Length <= 100)
            {
                return existente;
            }

            string nuevo = Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant();

            Response.Cookies.Append(
                CookieDispositivo,
                nuevo,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = EsSolicitudHttpsPublica(),
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddYears(1)
                });

            return nuevo;
        }

        private static string? ValidarEstadoLlave(
            ActualizacionLlaveDescarga llave,
            string plataforma,
            string canal,
            DateTime ahoraUtc)
        {
            if (llave.Estado == EstadoRevocada)
                return "La llave fue revocada por un administrador.";

            if (llave.Estado == EstadoBloqueada)
                return "La llave se encuentra bloqueada.";

            if (llave.FechaExpiracionUtc <= ahoraUtc)
            {
                llave.Estado = EstadoVencida;
                return "La llave ya venció.";
            }

            if (llave.CantidadUsos >= llave.CantidadMaximaUsos ||
                llave.Estado == EstadoUtilizada)
            {
                llave.Estado = EstadoUtilizada;
                return "La llave ya alcanzó la cantidad máxima de usos.";
            }

            if (!string.Equals(
                    llave.Plataforma,
                    plataforma,
                    StringComparison.Ordinal))
            {
                return "La llave no corresponde a la plataforma seleccionada.";
            }

            if (!string.Equals(
                    llave.Canal,
                    canal,
                    StringComparison.Ordinal))
            {
                return "La llave no corresponde al canal seleccionado.";
            }

            if (llave.Estado != EstadoActiva)
                return "La llave no se encuentra activa.";

            return null;
        }

        private async Task ActualizarLlavesVencidasAsync(
            CancellationToken cancellationToken)
        {
            DateTime ahoraUtc = DateTime.UtcNow;

            List<ActualizacionLlaveDescarga> vencidas =
                await actualizacionesDb.LlavesDescarga
                    .Where(x =>
                        x.Activo &&
                        x.Estado == EstadoActiva &&
                        x.FechaExpiracionUtc <= ahoraUtc)
                    .ToListAsync(cancellationToken);

            if (vencidas.Count == 0)
                return;

            foreach (ActualizacionLlaveDescarga llave in vencidas)
                llave.Estado = EstadoVencida;

            await actualizacionesDb.SaveChangesAsync(cancellationToken);
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    ActualizacionesDatabaseInitializer.CodigoInterfazLlavesDescarga,
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

        private void GuardarPermisoEnCookie(
            int actualizacionId,
            string permiso,
            DateTime expiracionUtc)
        {
            string ruta =
                $"{Request.PathBase}/api/actualizaciones/descargar/{actualizacionId}";

            Response.Cookies.Append(
                ActualizacionDescargaTokenService.ObtenerNombreCookie(
                    actualizacionId),
                permiso,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = EsSolicitudHttpsPublica(),
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Path = string.IsNullOrWhiteSpace(ruta)
                        ? "/"
                        : ruta,
                    Expires = new DateTimeOffset(
                        DateTime.SpecifyKind(
                            expiracionUtc,
                            DateTimeKind.Utc))
                });
        }

        private string ConstruirUrlDescarga(int actualizacionId)
        {
            string baseUrl = ObtenerBaseUrlPublica();

            return baseUrl.TrimEnd('/') +
                   $"/api/actualizaciones/descargar/{actualizacionId}";
        }

        private bool EsSolicitudHttpsPublica()
        {
            if (Request.IsHttps)
                return true;

            if (!EsProxyPrivado(HttpContext.Connection.RemoteIpAddress))
                return false;

            string forwardedProto = Request.Headers["X-Forwarded-Proto"]
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault() ?? string.Empty;

            return string.Equals(
                forwardedProto,
                "https",
                StringComparison.OrdinalIgnoreCase);
        }

        private string ObtenerBaseUrlPublica()
        {
            string scheme = EsSolicitudHttpsPublica()
                ? "https"
                : Request.Scheme;

            HostString host = Request.Host;

            if (EsProxyPrivado(HttpContext.Connection.RemoteIpAddress))
            {
                string forwardedHost = Request.Headers["X-Forwarded-Host"]
                    .ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .FirstOrDefault() ?? string.Empty;

                if (Uri.TryCreate(
                        $"{scheme}://{forwardedHost}",
                        UriKind.Absolute,
                        out Uri? publicUri))
                {
                    host = new HostString(publicUri.Authority);
                }
            }

            return $"{scheme}://{host}{Request.PathBase}";
        }

        private IActionResult PaginaResultado(
            int codigoEstado,
            string titulo,
            string mensaje,
            string? urlRetorno,
            bool esError)
        {
            string retorno = NormalizarUrlRetorno(urlRetorno);
            string tituloSeguro = WebUtility.HtmlEncode(titulo);
            string mensajeSeguro = WebUtility.HtmlEncode(mensaje);
            string retornoSeguro = WebUtility.HtmlEncode(retorno);
            string clase = esError ? "error" : "success";

            string html = $$"""
                <!doctype html>
                <html lang="es">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>{{tituloSeguro}} | CONATRADEC</title>
                    <style>
                        *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;background:#eef5f2;color:#17362f;font-family:Arial,sans-serif}.card{width:min(580px,100%);background:#fff;border:1px solid #d8e5df;border-radius:22px;padding:34px;box-shadow:0 22px 65px rgba(23,54,47,.13)}.brand{font-weight:900;letter-spacing:.08em;color:#315c52}.icon{width:62px;height:62px;display:grid;place-items:center;border-radius:18px;margin:24px 0 18px;font-size:30px}.icon.error{background:#ffe3e3;color:#c92a2a}.icon.success{background:#d3f9d8;color:#2b8a3e}h1{margin:0 0 12px;font-size:29px}p{margin:0;color:#536b65;line-height:1.65}.button{display:inline-flex;margin-top:26px;padding:13px 20px;border-radius:12px;background:#3b655b;color:white;text-decoration:none;font-weight:800}
                    </style>
                </head>
                <body>
                    <main class="card">
                        <div class="brand">CONATRADEC</div>
                        <div class="icon {{clase}}">!</div>
                        <h1>{{tituloSeguro}}</h1>
                        <p>{{mensajeSeguro}}</p>
                        <a class="button" href="{{retornoSeguro}}">Volver al portal</a>
                    </main>
                </body>
                </html>
                """;

            /*
             * Este resultado es una página para una persona, no una respuesta
             * JSON de la API. Se devuelve 200 para evitar que el filtro global
             * de errores reemplace el HTML por un objeto JSON cuyo mensaje
             * contenga todo el documento codificado.
             */
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Resultado-Descarga"] =
                codigoEstado.ToString();

            return new ContentResult
            {
                StatusCode = StatusCodes.Status200OK,
                ContentType = "text/html; charset=utf-8",
                Content = html
            };
        }

        private string NormalizarUrlRetorno(string? valor)
        {
            if (Uri.TryCreate(valor, UriKind.Absolute, out Uri? absoluta) &&
                absoluta.Scheme is "http" or "https")
            {
                return absoluta.AbsoluteUri;
            }

            string baseUrl = ObtenerBaseUrlPublica();

            return baseUrl.TrimEnd('/') + "/";
        }

        private static DescargaPortalDto MapearPortal(
            ActualizacionAplicacion version) =>
            new()
            {
                ActualizacionAplicacionId =
                    version.ActualizacionAplicacionId,
                Plataforma = version.Plataforma,
                Canal = version.Canal,
                VersionNombre = version.VersionNombre,
                VersionCodigo = version.VersionCodigo,
                NotasVersion = version.NotasVersion,
                NombreArchivo = version.NombreArchivo,
                TamanoBytes = version.TamanoBytes,
                HashSha256 = version.HashSha256,
                FechaPublicacionUtc = version.FechaPublicacionUtc
            };

        private static LlaveDescargaListadoDto MapearLlave(
            ActualizacionLlaveDescarga x) =>
            new()
            {
                ActualizacionLlaveDescargaId =
                    x.ActualizacionLlaveDescargaId,
                LlaveEnmascarada =
                    "CNTR-••••-••••-" + x.UltimosCaracteres,
                Plataforma = x.Plataforma,
                Canal = x.Canal,
                Estado = x.Estado,
                Destinatario = x.Destinatario,
                Observacion = x.Observacion,
                CantidadMaximaUsos = x.CantidadMaximaUsos,
                CantidadUsos = x.CantidadUsos,
                UsuarioCreacionId = x.UsuarioCreacionId,
                UsuarioRevocacionId = x.UsuarioRevocacionId,
                FechaCreacionUtc = x.FechaCreacionUtc,
                FechaExpiracionUtc = x.FechaExpiracionUtc,
                FechaUltimoUsoUtc = x.FechaUltimoUsoUtc,
                FechaRevocacionUtc = x.FechaRevocacionUtc
            };

        private static string GenerarLlave()
        {
            Span<byte> bytes = stackalloc byte[12];
            RandomNumberGenerator.Fill(bytes);

            Span<char> caracteres = stackalloc char[12];

            for (int i = 0; i < caracteres.Length; i++)
            {
                caracteres[i] =
                    AlfabetoLlave[bytes[i] % AlfabetoLlave.Length];
            }

            string valor = new(caracteres);

            return $"CNTR-{valor[..4]}-{valor[4..8]}-{valor[8..12]}";
        }

        private static string NormalizarLlave(string? valor) =>
            new((valor ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());

        private static string CalcularHashLlave(string llave) =>
            CalcularHashNormalizado(NormalizarLlave(llave));

        private static string CalcularHashNormalizado(string normalizada)
        {
            byte[] hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(normalizada));

            return Convert.ToHexString(hash).ToLowerInvariant();
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

        private static string NormalizarEstado(string? valor)
        {
            string normalizado =
                (valor ?? string.Empty).Trim().ToUpperInvariant();

            return normalizado is
                EstadoActiva or
                EstadoUtilizada or
                EstadoVencida or
                EstadoRevocada or
                EstadoBloqueada
                    ? normalizado
                    : string.Empty;
        }

        private static string DetectarNavegador(string agente)
        {
            if (agente.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
                return "Microsoft Edge";
            if (agente.Contains("OPR/", StringComparison.OrdinalIgnoreCase))
                return "Opera";
            if (agente.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
                return "Google Chrome";
            if (agente.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
                return "Mozilla Firefox";
            if (agente.Contains("Safari/", StringComparison.OrdinalIgnoreCase))
                return "Safari";
            return "No identificado";
        }

        private static string DetectarSistemaOperativo(string agente)
        {
            if (agente.Contains("Android", StringComparison.OrdinalIgnoreCase))
                return "Android";
            if (agente.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                return "Windows";
            if (agente.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                agente.Contains("iPad", StringComparison.OrdinalIgnoreCase))
                return "iOS/iPadOS";
            if (agente.Contains("Mac OS", StringComparison.OrdinalIgnoreCase))
                return "macOS";
            if (agente.Contains("Linux", StringComparison.OrdinalIgnoreCase))
                return "Linux";
            return "No identificado";
        }

        private static string DetectarTipoDispositivo(string agente)
        {
            if (agente.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
                agente.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                agente.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
                return "Móvil";

            if (agente.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
                agente.Contains("iPad", StringComparison.OrdinalIgnoreCase))
                return "Tableta";

            return "Escritorio";
        }

        private static bool EsProxyPrivado(IPAddress? ip)
        {
            if (ip == null || IPAddress.IsLoopback(ip))
                return true;

            if (ip.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
            }

            byte[] bytes = ip.GetAddressBytes();

            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = valor?.Trim() ?? string.Empty;
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private sealed record ContextoCliente(
            string IpCliente,
            string ForwardedFor,
            string AgenteUsuario,
            string Navegador,
            string SistemaOperativo,
            string TipoDispositivo,
            string IdentificadorDispositivoWeb);

        private sealed class ResultadoAutorizacion
        {
            public bool Exito { get; private init; }
            public int CodigoEstado { get; private init; }
            public string Mensaje { get; private init; } = string.Empty;
            public DescargaAutorizadaDto? Data { get; private init; }

            public static ResultadoAutorizacion Ok(
                DescargaAutorizadaDto data) =>
                new()
                {
                    Exito = true,
                    CodigoEstado = StatusCodes.Status200OK,
                    Mensaje = "Descarga autorizada.",
                    Data = data
                };

            public static ResultadoAutorizacion Error(
                int codigoEstado,
                string mensaje) =>
                new()
                {
                    Exito = false,
                    CodigoEstado = codigoEstado,
                    Mensaje = mensaje
                };
        }
    }
}
