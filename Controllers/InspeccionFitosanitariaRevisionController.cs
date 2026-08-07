using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Data.Common;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Coordina la revisión humana completa sin mezclarla con el cierre
    /// definitivo de la inspección. El analizador puede trabajar por fotografía,
    /// devolver evidencia al técnico y finalizar únicamente cuando el expediente
    /// completo se encuentre listo para el aprobador.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/revision-fitosanitaria")]
    public sealed class InspeccionFitosanitariaRevisionController : ControllerBase
    {
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaDatabase flujoDatabase;
        private readonly InspeccionFitosanitariaDevolucionDatabase database;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly ILogger<InspeccionFitosanitariaRevisionController> logger;

        public InspeccionFitosanitariaRevisionController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control,
            ILogger<InspeccionFitosanitariaRevisionController> logger)
        {
            this.permisos = permisos;
            this.control = control;
            this.logger = logger;
            flujoDatabase = new InspeccionFitosanitariaDatabase(db);
            database = new InspeccionFitosanitariaDevolucionDatabase(db);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(db);
        }

        [HttpGet("{id:int}/contexto")]
        public async Task<IActionResult> ObtenerContexto(
            int id,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            try
            {
                await InicializarFlujoAsync(cancellationToken);
                InspeccionFitosanitariaControlRegistro? registro =
                    await control.ObtenerAsync(id, cancellationToken);

                if (registro == null || !registro.Activo)
                    return NotFound(Error("No se encontró la inspección indicada."));

                bool esTecnicoPropietario =
                    registro.UsuarioSolicitanteId == usuarioId.Value;

                bool puedeAnalizador = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Leer,
                    cancellationToken);

                bool puedeAprobador = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Leer,
                    cancellationToken);

                if (!esTecnicoPropietario && !puedeAnalizador && !puedeAprobador)
                    return Forbid();

                ContextoRevisionAnalizadorDto contexto =
                    await database.ObtenerContextoAsync(id, cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Contexto de revisión obtenido correctamente.",
                    data = contexto
                });
            }
            catch (DbException ex)
            {
                logger.LogError(
                    ex,
                    "Error de base de datos al preparar el contexto de revisión de la inspección {InspeccionId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "No fue posible preparar el contexto de revisión. Reinicie la API después de publicar la corrección para que el esquema incompleto sea reparado automáticamente."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al preparar el contexto de revisión de la inspección {InspeccionId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "No fue posible cargar el flujo de revisión de esta inspección. Revise el registro del backend asociado al identificador mostrado."));
            }
        }

        [HttpPost("{id:int}/devolver-tecnico")]
        public async Task<IActionResult> DevolverAlTecnico(
            int id,
            [FromBody] DevolverFotografiaTecnicoRequest? request,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null || request.FotografiaId <= 0)
                return BadRequest(Error("Seleccione una fotografía válida."));

            if (request.MotivoDevolucionTecnicoId <= 0)
                return BadRequest(Error("Seleccione un motivo de devolución."));

            string instrucciones = (request.Instrucciones ?? string.Empty).Trim();
            if (instrucciones.Length is < 8 or > 3000)
            {
                return BadRequest(Error(
                    "Las instrucciones específicas deben contener entre 8 y 3000 caracteres."));
            }

            await InicializarFlujoAsync(cancellationToken);
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NotFound(Error("No se encontró la inspección indicada."));

            if (registro.CerradaDefinitiva)
            {
                return Conflict(Error(
                    "La inspección está cerrada definitivamente y no admite devoluciones."));
            }

            ResultadoAsignacionFlujo asignacionAnalizador =
                await asignaciones.TomarAnalizadorAsync(
                    id,
                    usuarioId!.Value,
                    cancellationToken);

            if (!asignacionAnalizador.Exitoso)
                return Conflict(Error(asignacionAnalizador.Mensaje));

            MotivoDevolucionTecnicoRespuesta? motivo =
                await database.ObtenerMotivoAsync(
                    request.MotivoDevolucionTecnicoId,
                    cancellationToken);

            if (motivo == null || !motivo.Activo)
                return Conflict(Error("El motivo seleccionado no está disponible."));

            try
            {
                DevolucionTecnicoFotografiaDto devolucion =
                    await database.DevolverAlTecnicoAsync(
                        id,
                        request.FotografiaId,
                        motivo,
                        instrucciones,
                        usuarioId!.Value,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La fotografía fue devuelta al técnico y la etapa técnica quedó reabierta para resolver la corrección.",
                    data = devolucion
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(Error(ex.Message));
            }
        }

        [HttpPost("{id:int}/resolver-devolucion")]
        public async Task<IActionResult> ResolverDevolucion(
            int id,
            [FromBody] ResolverDevolucionTecnicoRequest? request,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null || request.FotografiaId <= 0)
                return BadRequest(Error("Seleccione una fotografía válida."));

            if (string.IsNullOrWhiteSpace(request.TipoFotografia))
                return BadRequest(Error("Seleccione el tipo de fotografía corregido."));

            if (request.FechaIdentificacionCampo.Date > DateTime.UtcNow.Date)
            {
                return BadRequest(Error(
                    "La fecha de identificación en campo no puede estar en el futuro."));
            }

            string respuesta = (request.RespuestaTecnico ?? string.Empty).Trim();
            if (respuesta.Length is < 8 or > 2000)
            {
                return BadRequest(Error(
                    "La respuesta técnica debe contener entre 8 y 2000 caracteres."));
            }

            await InicializarFlujoAsync(cancellationToken);
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NotFound(Error("No se encontró la inspección indicada."));

            if (registro.UsuarioSolicitanteId != usuarioId.Value)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    Error("Solo el técnico que creó la inspección puede resolver la devolución."));
            }

            if (registro.CerradaDefinitiva)
                return Conflict(Error("La inspección está cerrada definitivamente."));

            request.RespuestaTecnico = respuesta;

            try
            {
                await database.ResolverDevolucionAsync(
                    id,
                    request,
                    usuarioId!.Value,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La corrección fue registrada. La fotografía quedó pendiente de un nuevo análisis con IA."
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(Error(ex.Message));
            }
        }

        [HttpPost("{id:int}/finalizar-analizador")]
        public async Task<IActionResult> FinalizarAnalizador(
            int id,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarFlujoAsync(cancellationToken);
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NotFound(Error("No se encontró la inspección indicada."));

            if (registro.CerradaDefinitiva)
                return Conflict(Error("La inspección está cerrada definitivamente."));

            ResultadoAsignacionFlujo asignacionAnalizador =
                await asignaciones.TomarAnalizadorAsync(
                    id,
                    usuarioId!.Value,
                    cancellationToken);

            if (!asignacionAnalizador.Exitoso)
                return Conflict(Error(asignacionAnalizador.Mensaje));

            try
            {
                (bool exitoso, string mensaje) =
                    await database.FinalizarAnalizadorAsync(
                        id,
                        usuarioId!.Value,
                        cancellationToken);

                if (!exitoso)
                    return Conflict(Error(mensaje));

                return Ok(new
                {
                    success = true,
                    message = mensaje,
                    data = await database.ObtenerContextoAsync(
                        id,
                        cancellationToken)
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(Error(ex.Message));
            }
        }

        private async Task InicializarFlujoAsync(
            CancellationToken cancellationToken)
        {
            /*
             * La revisión usa tanto las tablas nuevas de devolución como las
             * tablas históricas de análisis humano e historial por fotografía.
             * Ambas se inicializan aquí para que el flujo funcione incluso en
             * una instalación donde esta sea la primera pantalla utilizada.
             */
            await flujoDatabase.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await database.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);
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

            if (permiso.Permitido)
                return null;

            return StatusCode(
                permiso.CodigoEstado,
                new
                {
                    success = false,
                    message = permiso.Mensaje
                });
        }

        private async Task<bool> TienePermisoAsync(
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

            return permiso.Permitido;
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
