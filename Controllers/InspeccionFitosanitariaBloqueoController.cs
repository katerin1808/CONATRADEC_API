using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Reserva temporalmente una inspección para una sola sesión por etapa.
    /// ANALIZADOR y APROBADOR poseen bloqueos independientes para conservar el
    /// trabajo por fotografía sin permitir dos usuarios simultáneos en la
    /// misma etapa. La asignación persistente y el bloqueo temporal son acciones
    /// separadas: una etapa sin responsable debe tomarse explícitamente antes
    /// de iniciar una sesión de edición.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/revision-fitosanitaria/{id:int}/bloqueo")]
    public sealed class InspeccionFitosanitariaBloqueoController : ControllerBase
    {
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaBloqueoDatabase bloqueos;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;

        public InspeccionFitosanitariaBloqueoController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            PermisoApiService permisos)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.permisos = permisos;
            bloqueos = new InspeccionFitosanitariaBloqueoDatabase(diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(
                diagnosticoDb);
        }

        /// <summary>
        /// Consulta la asignación persistente de la etapa sin adquirir ningún
        /// bloqueo. Se utiliza para presentar correctamente los estados
        /// "sin asignar", "asignada a mí" y "asignada a otro usuario".
        /// </summary>
        [HttpGet("asignacion")]
        public async Task<IActionResult> ObtenerAsignacion(
            int id,
            [FromQuery] string modo,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa))
            {
                return BadRequest(Error(
                    "La asignación solo está disponible para analizador o aprobador."));
            }

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId.Value,
                etapa,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "La inspección indicada no existe o ya no está activa."));
            }

            InspeccionFitosanitariaAsignacionRegistro asignacion =
                await asignaciones.ObtenerAsync(id, cancellationToken);

            int? asignado = etapa == "ANALIZADOR"
                ? asignacion.UsuarioAnalizadorId
                : asignacion.UsuarioAprobadorId;

            string nombreAsignado = await ObtenerNombreUsuarioAsync(
                asignado,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = asignado.HasValue
                    ? "Asignación de la etapa obtenida correctamente."
                    : "La etapa todavía no tiene un responsable asignado.",
                data = CrearEstadoAsignacion(
                    id,
                    etapa,
                    usuarioId.Value,
                    asignado,
                    nombreAsignado)
            });
        }

        /// <summary>
        /// Toma explícitamente una etapa todavía sin responsable. Esta acción
        /// no adquiere el bloqueo de edición; el cliente debe solicitarlo
        /// inmediatamente después de una asignación exitosa.
        /// </summary>
        [HttpPost("tomar")]
        public async Task<IActionResult> Tomar(
            int id,
            [FromQuery] string modo,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa))
            {
                return BadRequest(Error(
                    "Solo puede tomar una inspección como analizador o aprobador."));
            }

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId.Value,
                etapa,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "La inspección indicada no existe o ya no está activa."));
            }

            InspeccionFitosanitariaAsignacionRegistro anterior =
                await asignaciones.ObtenerAsync(id, cancellationToken);

            int? asignadoAnterior = etapa == "ANALIZADOR"
                ? anterior.UsuarioAnalizadorId
                : anterior.UsuarioAprobadorId;

            if (asignadoAnterior.HasValue &&
                asignadoAnterior.Value != usuarioId.Value)
            {
                string nombreAsignado = await ObtenerNombreUsuarioAsync(
                    asignadoAnterior,
                    cancellationToken);

                return Conflict(CrearConflictoAsignacion(
                    id,
                    etapa,
                    asignadoAnterior.Value,
                    nombreAsignado));
            }

            ResultadoAsignacionFlujo resultado = etapa == "ANALIZADOR"
                ? await asignaciones.AsignarAnalizadorAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken)
                : await asignaciones.AsignarAprobadorAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken);

            if (!resultado.Exitoso)
            {
                int? responsableReal = etapa == "ANALIZADOR"
                    ? resultado.Asignacion.UsuarioAnalizadorId
                    : resultado.Asignacion.UsuarioAprobadorId;

                string nombreResponsable = await ObtenerNombreUsuarioAsync(
                    responsableReal,
                    cancellationToken);

                return Conflict(CrearConflictoAsignacion(
                    id,
                    etapa,
                    responsableReal ?? 0,
                    nombreResponsable));
            }

            int? asignado = etapa == "ANALIZADOR"
                ? resultado.Asignacion.UsuarioAnalizadorId
                : resultado.Asignacion.UsuarioAprobadorId;

            string usuarioNombre = await ObtenerNombreUsuarioAsync(
                asignado,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = asignadoAnterior.HasValue
                    ? $"La inspección ya estaba asignada al usuario actual como {NombreEtapa(etapa)}."
                    : $"La inspección quedó asignada al usuario actual como {NombreEtapa(etapa)}.",
                data = CrearEstadoAsignacion(
                    id,
                    etapa,
                    usuarioId.Value,
                    asignado,
                    usuarioNombre)
            });
        }

        [HttpPost("adquirir")]
        public async Task<IActionResult> Adquirir(
            int id,
            [FromQuery] string modo,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa))
            {
                return BadRequest(Error(
                    "El bloqueo exclusivo solo está disponible para analizador o aprobador."));
            }

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId.Value,
                etapa,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "La inspección indicada no existe o ya no está activa."));
            }

            /*
             * El bloqueo ya no crea asignaciones. La etapa debe haberse tomado
             * de forma explícita o haber sido asignada/reasignada previamente.
             */
            InspeccionFitosanitariaAsignacionRegistro asignacionActual =
                await asignaciones.ObtenerAsync(id, cancellationToken);

            int? asignadoActual = etapa == "ANALIZADOR"
                ? asignacionActual.UsuarioAnalizadorId
                : asignacionActual.UsuarioAprobadorId;

            if (!asignadoActual.HasValue)
            {
                return Conflict(CrearConflictoSinAsignacion(
                    id,
                    etapa));
            }

            if (asignadoActual.Value != usuarioId.Value)
            {
                string nombreAsignado = await ObtenerNombreUsuarioAsync(
                    asignadoActual,
                    cancellationToken);

                return Conflict(CrearConflictoAsignacion(
                    id,
                    etapa,
                    asignadoActual.Value,
                    nombreAsignado));
            }

            ResultadoAdquisicionBloqueoInspeccion resultado =
                await bloqueos.AdquirirAsync(
                    id,
                    usuarioId.Value,
                    etapa,
                    cancellationToken);

            if (!resultado.Exitoso || resultado.Bloqueo == null)
            {
                string nombre = await ObtenerNombreUsuarioAsync(
                    resultado.Bloqueo?.UsuarioId,
                    cancellationToken);

                string mensaje = !string.IsNullOrWhiteSpace(nombre)
                    ? resultado.Bloqueo?.UsuarioId == usuarioId.Value
                        ? $"Esta misma cuenta ya tiene abierta la inspección como {NombreEtapa(etapa)} en otra ventana o dispositivo. Cierre esa sesión o espere a que el bloqueo venza automáticamente."
                        : $"La inspección está siendo utilizada por {nombre} como {NombreEtapa(etapa)}."
                    : resultado.Mensaje;

                return Conflict(new
                {
                    success = false,
                    message = mensaje,
                    data = CrearRespuesta(
                        resultado.Bloqueo,
                        nombre,
                        adquirido: false)
                });
            }

            string usuarioNombre = await ObtenerNombreUsuarioAsync(
                usuarioId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    $"La inspección quedó bloqueada para esta sesión de {NombreEtapa(etapa)}.",
                data = CrearRespuesta(
                    resultado.Bloqueo,
                    usuarioNombre,
                    adquirido: true,
                    autoAsignada: false)
            });
        }

        [HttpPost("renovar")]
        public async Task<IActionResult> Renovar(
            int id,
            [FromQuery] string modo,
            [FromBody] InspeccionBloqueoSesionRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa) ||
                request == null ||
                !Guid.TryParse(request.Token, out Guid token))
            {
                return BadRequest(Error(
                    "La sesión de bloqueo enviada no es válida."));
            }

            BloqueoInspeccionRegistro? bloqueo = await bloqueos.RenovarAsync(
                id,
                usuarioId.Value,
                etapa,
                token,
                cancellationToken);

            if (bloqueo == null)
            {
                return Conflict(Error(
                    "El bloqueo exclusivo de esta ficha venció o fue liberado. Regrese a la bandeja y abra nuevamente la inspección."));
            }

            string usuarioNombre = await ObtenerNombreUsuarioAsync(
                usuarioId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Bloqueo renovado correctamente.",
                data = CrearRespuesta(
                    bloqueo,
                    usuarioNombre,
                    adquirido: true)
            });
        }

        [HttpPost("liberar")]
        public async Task<IActionResult> Liberar(
            int id,
            [FromQuery] string modo,
            [FromBody] InspeccionBloqueoSesionRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa) ||
                request == null ||
                !Guid.TryParse(request.Token, out Guid token))
            {
                return BadRequest(Error(
                    "La sesión de bloqueo enviada no es válida."));
            }

            await bloqueos.LiberarAsync(
                id,
                usuarioId.Value,
                etapa,
                token,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Bloqueo liberado correctamente."
            });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int usuarioId,
            string etapa,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            string interfaz = etapa == "ANALIZADOR"
                ? DiagnosticoIAFlujo.InterfazAnalizador
                : DiagnosticoIAFlujo.InterfazAprobador;

            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                permiso,
                cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private async Task<string> ObtenerNombreUsuarioAsync(
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (usuarioId is not > 0)
                return string.Empty;

            Usuario? usuario = await db.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.UsuarioId == usuarioId.Value,
                    cancellationToken);

            if (usuario == null)
                return $"usuario #{usuarioId.Value}";

            if (!string.IsNullOrWhiteSpace(usuario.nombreCompletoUsuario))
                return usuario.nombreCompletoUsuario.Trim();

            if (!string.IsNullOrWhiteSpace(usuario.nombreUsuario))
                return usuario.nombreUsuario.Trim();

            return $"usuario #{usuarioId.Value}";
        }

        private static object CrearEstadoAsignacion(
            int inspeccionId,
            string etapa,
            int usuarioActualId,
            int? usuarioAsignadoId,
            string usuarioAsignadoNombre)
        {
            bool asignadaAlUsuarioActual =
                usuarioAsignadoId.HasValue &&
                usuarioAsignadoId.Value == usuarioActualId;

            return new
            {
                inspeccionId,
                modo = etapa.ToLowerInvariant(),
                usuarioAsignadoId,
                usuarioAsignadoNombre = usuarioAsignadoNombre ?? string.Empty,
                asignadaAlUsuarioActual,
                disponibleParaTomar = !usuarioAsignadoId.HasValue,
                asignadaAOtroUsuario =
                    usuarioAsignadoId.HasValue &&
                    !asignadaAlUsuarioActual
            };
        }

        private static object CrearConflictoSinAsignacion(
            int inspeccionId,
            string etapa) =>
            new
            {
                success = false,
                message =
                    $"La etapa de {NombreEtapa(etapa)} todavía no tiene un responsable. Use la opción 'Tomar inspección' antes de iniciar la edición.",
                data = new
                {
                    inspeccionId,
                    modo = etapa.ToLowerInvariant(),
                    usuarioAsignadoId = (int?)null,
                    usuarioAsignadoNombre = string.Empty,
                    asignadaAlUsuarioActual = false,
                    disponibleParaTomar = true,
                    asignadaAOtroUsuario = false
                }
            };

        private static object CrearConflictoAsignacion(
            int inspeccionId,
            string etapa,
            int usuarioAsignadoId,
            string usuarioAsignadoNombre)
        {
            string mensaje = string.IsNullOrWhiteSpace(usuarioAsignadoNombre)
                ? $"La inspección ya está asignada a otro usuario para la etapa de {NombreEtapa(etapa)}. Puede consultarla, pero solo el responsable asignado puede modificarla."
                : $"La inspección está asignada a {usuarioAsignadoNombre} para la etapa de {NombreEtapa(etapa)}. Puede consultarla, pero solo ese responsable puede modificarla.";

            return new
            {
                success = false,
                message = mensaje,
                data = new
                {
                    adquirido = false,
                    inspeccionId,
                    modo = etapa.ToLowerInvariant(),
                    usuarioId = usuarioAsignadoId,
                    usuarioNombre = usuarioAsignadoNombre ?? string.Empty,
                    token = string.Empty,
                    fechaAdquisicionUtc = (DateTime?)null,
                    ultimoHeartbeatUtc = (DateTime?)null,
                    expiraUtc = (DateTime?)null,
                    vigenciaSegundos =
                        InspeccionFitosanitariaBloqueoDatabase.VigenciaSegundos,
                    asignadaAOtroUsuario = true,
                    autoAsignada = false
                }
            };
        }

        private static object CrearRespuesta(
            BloqueoInspeccionRegistro? bloqueo,
            string usuarioNombre,
            bool adquirido,
            bool autoAsignada = false)
        {
            if (bloqueo == null)
            {
                return new
                {
                    adquirido,
                    inspeccionId = 0,
                    modo = string.Empty,
                    usuarioId = 0,
                    usuarioNombre = usuarioNombre ?? string.Empty,
                    token = string.Empty,
                    fechaAdquisicionUtc = (DateTime?)null,
                    ultimoHeartbeatUtc = (DateTime?)null,
                    expiraUtc = (DateTime?)null,
                    vigenciaSegundos =
                        InspeccionFitosanitariaBloqueoDatabase.VigenciaSegundos,
                    asignadaAOtroUsuario = false,
                    autoAsignada
                };
            }

            return new
            {
                adquirido,
                inspeccionId = bloqueo.InspeccionId,
                modo = bloqueo.Etapa.ToLowerInvariant(),
                usuarioId = bloqueo.UsuarioId,
                usuarioNombre = usuarioNombre ?? string.Empty,
                token = adquirido
                    ? bloqueo.TokenSesion.ToString("D")
                    : string.Empty,
                fechaAdquisicionUtc = bloqueo.FechaAdquisicionUtc,
                ultimoHeartbeatUtc = bloqueo.UltimoHeartbeatUtc,
                expiraUtc = bloqueo.ExpiraUtc,
                vigenciaSegundos =
                    InspeccionFitosanitariaBloqueoDatabase.VigenciaSegundos,
                asignadaAOtroUsuario = false,
                autoAsignada
            };
        }

        private static string NormalizarModo(string? modo)
        {
            string valor = (modo ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return valor switch
            {
                "analizador" => "ANALIZADOR",
                "aprobador" => "APROBADOR",
                _ => string.Empty
            };
        }

        private static string NombreEtapa(string etapa) =>
            etapa == "ANALIZADOR"
                ? "analizador"
                : "aprobador";

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("UsuarioId") ??
                User.FindFirstValue("usuarioId");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };
    }

    public sealed class InspeccionBloqueoSesionRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
