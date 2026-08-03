using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia/configuracion")]
    public sealed class DiagnosticoIAConfiguracionController :
        ControllerBase
    {
        private const int ConfiguracionId = 1;
        private const int MaximoHistorial = 25;

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public DiagnosticoIAConfiguracionController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            PermisoApiService permisos)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet]
        public async Task<IActionResult> Obtener(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIAConfiguracionDto configuracion =
                await CrearDtoAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Configuración de diagnóstico IA obtenida correctamente.",
                data = configuracion
            });
        }

        [HttpPut]
        public async Task<IActionResult> Actualizar(
            [FromBody] DiagnosticoIAConfiguracionActualizarRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request.MaximoRevisionesGemini is < 1 or > 20)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El máximo de revisiones debe estar entre 1 y 20."
                });
            }

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                DiagnosticoIAConfiguracion? configuracion =
                    await diagnosticoDb.Configuraciones
                        .FirstOrDefaultAsync(
                            item =>
                                item.DiagnosticoIAConfiguracionId ==
                                ConfiguracionId,
                            cancellationToken);

                if (configuracion == null)
                {
                    configuracion = new DiagnosticoIAConfiguracion
                    {
                        DiagnosticoIAConfiguracionId = ConfiguracionId,
                        MaximoRevisionesGemini = 2,
                        RevisionesIlimitadas = false,
                        FechaModificacionUtc = DateTime.UtcNow
                    };

                    diagnosticoDb.Configuraciones.Add(configuracion);
                    await diagnosticoDb.SaveChangesAsync(
                        cancellationToken);
                }

                int maximoAnterior =
                    configuracion.MaximoRevisionesGemini;

                bool ilimitadasAnterior =
                    configuracion.RevisionesIlimitadas;

                bool cambio =
                    maximoAnterior != request.MaximoRevisionesGemini ||
                    ilimitadasAnterior != request.RevisionesIlimitadas;

                if (cambio)
                {
                    configuracion.MaximoRevisionesGemini =
                        request.MaximoRevisionesGemini;

                    configuracion.RevisionesIlimitadas =
                        request.RevisionesIlimitadas;

                    configuracion.FechaModificacionUtc =
                        DateTime.UtcNow;

                    configuracion.UsuarioModificacionId =
                        usuarioId!.Value;

                    diagnosticoDb.ConfiguracionHistorial.Add(
                        new DiagnosticoIAConfiguracionHistorial
                        {
                            DiagnosticoIAConfiguracionId =
                                ConfiguracionId,
                            MaximoAnterior = maximoAnterior,
                            IlimitadasAnterior = ilimitadasAnterior,
                            MaximoNuevo =
                                request.MaximoRevisionesGemini,
                            IlimitadasNuevo =
                                request.RevisionesIlimitadas,
                            UsuarioId = usuarioId.Value,
                            FechaUtc = DateTime.UtcNow
                        });

                    await diagnosticoDb.SaveChangesAsync(
                        cancellationToken);
                }

                DiagnosticoIAConfiguracionDto dto =
                    await CrearDtoAsync(cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = cambio
                        ? "Configuración de diagnóstico IA actualizada correctamente."
                        : "La configuración ya tenía los valores indicados.",
                    data = dto
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                return Conflict(new
                {
                    success = false,
                    message =
                        "La configuración fue modificada por otro usuario. Actualice la pantalla e intente nuevamente."
                });
            }
            catch (OperationCanceledException)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
        }

        private async Task<DiagnosticoIAConfiguracionDto> CrearDtoAsync(
            CancellationToken cancellationToken)
        {
            DiagnosticoIAConfiguracion configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.DiagnosticoIAConfiguracionId ==
                            ConfiguracionId,
                        cancellationToken)
                ?? new DiagnosticoIAConfiguracion
                {
                    DiagnosticoIAConfiguracionId = ConfiguracionId,
                    MaximoRevisionesGemini = 2,
                    RevisionesIlimitadas = false,
                    FechaModificacionUtc = DateTime.UtcNow
                };

            List<DiagnosticoIAConfiguracionHistorial> historial =
                await diagnosticoDb.ConfiguracionHistorial
                    .AsNoTracking()
                    .OrderByDescending(item => item.FechaUtc)
                    .Take(MaximoHistorial)
                    .ToListAsync(cancellationToken);

            int[] usuariosIds = historial
                .Select(item => item.UsuarioId)
                .Append(configuracion.UsuarioModificacionId ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            Dictionary<int, string> usuarios = usuariosIds.Length == 0
                ? []
                : await db.Usuarios
                    .AsNoTracking()
                    .Where(item => usuariosIds.Contains(item.UsuarioId))
                    .ToDictionaryAsync(
                        item => item.UsuarioId,
                        item => item.nombreCompletoUsuario,
                        cancellationToken);

            return new DiagnosticoIAConfiguracionDto
            {
                MaximoRevisionesGemini =
                    configuracion.MaximoRevisionesGemini,
                RevisionesIlimitadas =
                    configuracion.RevisionesIlimitadas,
                FechaModificacionUtc =
                    configuracion.FechaModificacionUtc,
                UsuarioModificacionId =
                    configuracion.UsuarioModificacionId,
                UsuarioModificacion =
                    configuracion.UsuarioModificacionId.HasValue
                        ? usuarios.GetValueOrDefault(
                            configuracion.UsuarioModificacionId.Value,
                            $"Usuario {configuracion.UsuarioModificacionId.Value}")
                        : "Configuración inicial del sistema",
                Historial = historial
                    .Select(item =>
                        new DiagnosticoIAConfiguracionHistorialDto
                        {
                            DiagnosticoIAConfiguracionHistorialId =
                                item.DiagnosticoIAConfiguracionHistorialId,
                            MaximoAnterior = item.MaximoAnterior,
                            IlimitadasAnterior =
                                item.IlimitadasAnterior,
                            MaximoNuevo = item.MaximoNuevo,
                            IlimitadasNuevo = item.IlimitadasNuevo,
                            UsuarioId = item.UsuarioId,
                            Usuario = usuarios.GetValueOrDefault(
                                item.UsuarioId,
                                $"Usuario {item.UsuarioId}"),
                            FechaUtc = item.FechaUtc
                        })
                    .ToList()
            };
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazConfiguracion,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje
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
    }
}
