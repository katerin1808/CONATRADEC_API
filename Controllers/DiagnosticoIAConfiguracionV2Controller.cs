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
    /// Versión auditada de la configuración de Diagnóstico IA.
    /// Conserva intacto el controlador histórico y añade concurrencia
    /// optimista real mediante RowVersion transportada por el cliente.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia/configuracion/v2")]
    public sealed class DiagnosticoIAConfiguracionV2Controller : ControllerBase
    {
        private const int ConfiguracionId = 1;
        private const int MaximoHistorial = 25;

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public DiagnosticoIAConfiguracionV2Controller(
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

            await AsegurarConfiguracionAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Configuración de diagnóstico IA obtenida correctamente.",
                data = await CrearDtoAsync(cancellationToken)
            });
        }

        [HttpPut]
        public async Task<IActionResult> Actualizar(
            [FromBody] DiagnosticoIAConfiguracionV2ActualizarRequest request,
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
                return BadRequest(Error(
                    "El máximo de revisiones debe estar entre 1 y 20."));
            }

            if (!IntentarDecodificarRowVersion(
                    request.RowVersion,
                    out byte[] rowVersionEsperada))
            {
                return BadRequest(Error(
                    "La versión de la configuración no es válida. Actualice la pantalla e intente nuevamente."));
            }

            await AsegurarConfiguracionAsync(cancellationToken);

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            bool transaccionConfirmada = false;

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
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "La configuración fue reinicializada. Actualice la pantalla e intente nuevamente."));
                }

                if (!configuracion.RowVersion.SequenceEqual(rowVersionEsperada))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "La configuración fue modificada por otro usuario. Se cargarán los valores más recientes antes de volver a guardar."));
                }

                int maximoAnterior = configuracion.MaximoRevisionesGemini;
                bool ilimitadasAnterior = configuracion.RevisionesIlimitadas;

                bool cambio =
                    maximoAnterior != request.MaximoRevisionesGemini ||
                    ilimitadasAnterior != request.RevisionesIlimitadas;

                if (cambio)
                {
                    diagnosticoDb.Entry(configuracion)
                        .Property(item => item.RowVersion)
                        .OriginalValue = rowVersionEsperada;

                    configuracion.MaximoRevisionesGemini =
                        request.MaximoRevisionesGemini;
                    configuracion.RevisionesIlimitadas =
                        request.RevisionesIlimitadas;
                    configuracion.FechaModificacionUtc = DateTime.UtcNow;
                    configuracion.UsuarioModificacionId = usuarioId!.Value;

                    diagnosticoDb.ConfiguracionHistorial.Add(
                        new DiagnosticoIAConfiguracionHistorial
                        {
                            DiagnosticoIAConfiguracionId = ConfiguracionId,
                            MaximoAnterior = maximoAnterior,
                            IlimitadasAnterior = ilimitadasAnterior,
                            MaximoNuevo = request.MaximoRevisionesGemini,
                            IlimitadasNuevo = request.RevisionesIlimitadas,
                            UsuarioId = usuarioId.Value,
                            FechaUtc = DateTime.UtcNow
                        });

                    await diagnosticoDb.SaveChangesAsync(cancellationToken);
                }

                DiagnosticoIAConfiguracionV2Dto dto =
                    await CrearDtoAsync(cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

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
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);

                return Conflict(Error(
                    "La configuración fue modificada por otro usuario. Se cargarán los valores más recientes antes de volver a guardar."));
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task AsegurarConfiguracionAsync(
            CancellationToken cancellationToken)
        {
            bool existe = await diagnosticoDb.Configuraciones
                .AsNoTracking()
                .AnyAsync(
                    item => item.DiagnosticoIAConfiguracionId == ConfiguracionId,
                    cancellationToken);

            if (existe)
                return;

            diagnosticoDb.Configuraciones.Add(
                new DiagnosticoIAConfiguracion
                {
                    DiagnosticoIAConfiguracionId = ConfiguracionId,
                    MaximoRevisionesGemini = 2,
                    RevisionesIlimitadas = false,
                    FechaModificacionUtc = DateTime.UtcNow
                });

            try
            {
                await diagnosticoDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Otra solicitud pudo crear el singleton al mismo tiempo.
                diagnosticoDb.ChangeTracker.Clear();
            }
        }

        private async Task<DiagnosticoIAConfiguracionV2Dto> CrearDtoAsync(
            CancellationToken cancellationToken)
        {
            DiagnosticoIAConfiguracion configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .FirstAsync(
                        item =>
                            item.DiagnosticoIAConfiguracionId == ConfiguracionId,
                        cancellationToken);

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

            return new DiagnosticoIAConfiguracionV2Dto
            {
                MaximoRevisionesGemini = configuracion.MaximoRevisionesGemini,
                RevisionesIlimitadas = configuracion.RevisionesIlimitadas,
                FechaModificacionUtc = configuracion.FechaModificacionUtc,
                UsuarioModificacionId = configuracion.UsuarioModificacionId,
                UsuarioModificacion = configuracion.UsuarioModificacionId.HasValue
                    ? usuarios.GetValueOrDefault(
                        configuracion.UsuarioModificacionId.Value,
                        $"Usuario {configuracion.UsuarioModificacionId.Value}")
                    : "Configuración inicial del sistema",
                RowVersion = Convert.ToBase64String(configuracion.RowVersion),
                Historial = historial
                    .Select(item =>
                        new DiagnosticoIAConfiguracionHistorialDto
                        {
                            DiagnosticoIAConfiguracionHistorialId =
                                item.DiagnosticoIAConfiguracionHistorialId,
                            MaximoAnterior = item.MaximoAnterior,
                            IlimitadasAnterior = item.IlimitadasAnterior,
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
                    Error(resultado.Mensaje));
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

        private static bool IntentarDecodificarRowVersion(
            string? valor,
            out byte[] rowVersion)
        {
            rowVersion = [];

            if (string.IsNullOrWhiteSpace(valor))
                return false;

            try
            {
                rowVersion = Convert.FromBase64String(valor.Trim());
                return rowVersion.Length == 8;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
