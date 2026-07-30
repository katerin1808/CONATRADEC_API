using System.Data;
using System.Data.Common;
using System.Text.Json;
using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Reportes;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers;

[ApiController]
[Route("api/control-analisis")]
public sealed class ControlAnalisisController : ControllerBase
{
    private const string Permiso = PortalWebDatabaseInitializer.AuditoriaAnalisisWeb;

    private readonly DBContext db;
    private readonly AnalisisReporteDatosService reporteService;
    private readonly ILogger<ControlAnalisisController> logger;

    public ControlAnalisisController(
        DBContext db,
        AnalisisReporteDatosService reporteService,
        ILogger<ControlAnalisisController> logger)
    {
        this.db = db;
        this.reporteService = reporteService;
        this.logger = logger;
    }

    [HttpGet("{analisisSueloCalculoId:int}/pdf")]
    public async Task<IActionResult> ObtenerPdf(
        int analisisSueloCalculoId,
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioId,
        CancellationToken cancellationToken)
    {
        ResultadoPermiso permiso = await ValidarPermisoAsync(usuarioId, "leer", cancellationToken);
        if (!permiso.Permitido)
            return StatusCode(permiso.StatusCode, new { success = false, message = permiso.Mensaje });

        AnalisisReporte? reporte = await reporteService.ObtenerAsync(
            analisisSueloCalculoId,
            cancellationToken);

        if (reporte is not null)
        {
            byte[] pdfActual = AnalisisReportePdf.Generar(reporte);
            return File(pdfActual, "application/pdf", $"{reporte.NombreArchivoBase}.pdf");
        }

        var historico = await ConsultarPdfHistoricoAsync(
            analisisSueloCalculoId,
            cancellationToken);

        if (historico.Pdf is null)
        {
            return NotFound(new
            {
                success = false,
                message = "No se encontró un PDF vigente ni una copia histórica del análisis."
            });
        }

        Response.Headers["X-Analisis-Estado"] = "ELIMINADO";
        return File(
            historico.Pdf,
            "application/pdf",
            string.IsNullOrWhiteSpace(historico.NombreArchivo)
                ? $"analisis-eliminado-{analisisSueloCalculoId}.pdf"
                : historico.NombreArchivo);
    }

    [HttpPost("{analisisSueloId:int}/eliminar")]
    public async Task<IActionResult> Eliminar(
        int analisisSueloId,
        [FromBody] EliminarAnalisisAdministrativoDto dto,
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioId,
        CancellationToken cancellationToken)
    {
        ResultadoPermiso permiso = await ValidarPermisoAsync(usuarioId, "eliminar", cancellationToken);
        if (!permiso.Permitido)
            return StatusCode(permiso.StatusCode, new { success = false, message = permiso.Mensaje });

        string motivo = (dto.Motivo ?? string.Empty).Trim();
        if (motivo.Length < 5)
            return BadRequest(new { success = false, message = "Indique un motivo de eliminación de al menos 5 caracteres." });
        if (motivo.Length > 500)
            return BadRequest(new { success = false, message = "El motivo no puede superar 500 caracteres." });

        await using DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            bool existeActivo = await ExisteAsync(
                connection,
                transaction,
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.analisisSuelo WHERE analisisSueloId=@id AND activo=1) THEN 1 ELSE 0 END",
                analisisSueloId,
                cancellationToken);

            if (!existeActivo)
                return BadRequest(new { success = false, message = "El análisis no existe o ya se encuentra eliminado." });

            ManifiestoRestauracion manifiesto = await ConstruirManifiestoAsync(
                connection,
                transaction,
                analisisSueloId,
                cancellationToken);

            if (manifiesto.AnalisisSueloCalculoIds.Count == 0)
                return BadRequest(new { success = false, message = "El análisis no posee un cálculo activo que pueda administrarse." });

            int calculoPrincipalId = manifiesto.AnalisisSueloCalculoIds.Max();
            byte[]? pdf = null;
            string? nombrePdf = null;

            try
            {
                AnalisisReporte? reporte = await reporteService.ObtenerAsync(
                    calculoPrincipalId,
                    cancellationToken);
                if (reporte is not null)
                {
                    pdf = AnalisisReportePdf.Generar(reporte);
                    nombrePdf = $"{reporte.NombreArchivoBase}-eliminado.pdf";
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No fue posible guardar la copia PDF antes de eliminar el análisis {AnalisisSueloId}.", analisisSueloId);
            }

            string json = JsonSerializer.Serialize(manifiesto);

            await InsertarEliminacionAsync(
                connection,
                transaction,
                analisisSueloId,
                calculoPrincipalId,
                usuarioId!.Value,
                motivo,
                json,
                pdf,
                nombrePdf,
                cancellationToken);

            await AplicarEstadoAsync(
                connection,
                transaction,
                manifiesto,
                activo: false,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "El análisis fue eliminado y su estado previo quedó guardado para una recuperación segura."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Error al eliminar administrativamente el análisis {AnalisisSueloId}.", analisisSueloId);
            return StatusCode(500, new { success = false, message = "No fue posible eliminar el análisis." });
        }
    }

    [HttpPost("{analisisSueloId:int}/recuperar")]
    public async Task<IActionResult> Recuperar(
        int analisisSueloId,
        [FromBody] RecuperarAnalisisAdministrativoDto dto,
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioId,
        CancellationToken cancellationToken)
    {
        ResultadoPermiso permiso = await ValidarPermisoAsync(usuarioId, "actualizar", cancellationToken);
        if (!permiso.Permitido)
            return StatusCode(permiso.StatusCode, new { success = false, message = permiso.Mensaje });

        string motivo = (dto.Motivo ?? string.Empty).Trim();
        if (motivo.Length < 5)
            return BadRequest(new { success = false, message = "Indique un motivo de recuperación de al menos 5 caracteres." });
        if (motivo.Length > 500)
            return BadRequest(new { success = false, message = "El motivo no puede superar 500 caracteres." });

        await using DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            EliminacionGuardada? eliminacion = await ObtenerEliminacionPendienteAsync(
                connection,
                transaction,
                analisisSueloId,
                cancellationToken);

            if (eliminacion is null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No existe un manifiesto pendiente. Los análisis eliminados antes de esta mejora requieren recuperación asistida."
                });
            }

            ManifiestoRestauracion? manifiesto = JsonSerializer.Deserialize<ManifiestoRestauracion>(
                eliminacion.ManifiestoJson);

            if (manifiesto is null)
                return StatusCode(500, new { success = false, message = "El manifiesto de recuperación está dañado." });

            await AplicarEstadoAsync(
                connection,
                transaction,
                manifiesto,
                activo: true,
                cancellationToken);

            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
UPDATE dbo.analisisSueloEliminacion
SET estado=N'RECUPERADO',
    usuarioRecuperacionId=@usuarioId,
    fechaRecuperacionUtc=SYSUTCDATETIME(),
    motivoRecuperacion=@motivo
WHERE analisisSueloEliminacionId=@eliminacionId;
""";
            AgregarParametro(command, "@usuarioId", usuarioId!.Value);
            AgregarParametro(command, "@motivo", motivo);
            AgregarParametro(command, "@eliminacionId", eliminacion.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "El análisis fue recuperado exactamente con las versiones que estaban activas antes de eliminarlo."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Error al recuperar el análisis {AnalisisSueloId}.", analisisSueloId);
            return StatusCode(500, new { success = false, message = "No fue posible recuperar el análisis." });
        }
    }

    [HttpGet("{analisisSueloId:int}/estado-eliminacion")]
    public async Task<IActionResult> EstadoEliminacion(
        int analisisSueloId,
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioId,
        CancellationToken cancellationToken)
    {
        ResultadoPermiso permiso = await ValidarPermisoAsync(usuarioId, "leer", cancellationToken);
        if (!permiso.Permitido)
            return StatusCode(permiso.StatusCode, new { success = false, message = permiso.Mensaje });

        await using DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT TOP(1)
    analisisSueloEliminacionId,
    fechaEliminacionUtc,
    motivoEliminacion,
    estado,
    fechaRecuperacionUtc,
    motivoRecuperacion,
    CASE WHEN manifiestoRestauracionJson IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS tieneManifiesto,
    CASE WHEN pdfHistorico IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS tienePdf
FROM dbo.analisisSueloEliminacion
WHERE analisisSueloId=@id
ORDER BY fechaEliminacionUtc DESC;
""";
        AgregarParametro(command, "@id", analisisSueloId);

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Ok(new { success = true, data = (object?)null });

        return Ok(new
        {
            success = true,
            data = new
            {
                eliminacionId = reader.GetInt64(0),
                fechaEliminacionUtc = reader.GetDateTime(1),
                motivoEliminacion = reader.GetString(2),
                estado = reader.GetString(3),
                fechaRecuperacionUtc = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                motivoRecuperacion = reader.IsDBNull(5) ? null : reader.GetString(5),
                tieneManifiesto = reader.GetBoolean(6),
                tienePdf = reader.GetBoolean(7)
            }
        });
    }

    private async Task<ResultadoPermiso> ValidarPermisoAsync(
        int? usuarioId,
        string operacion,
        CancellationToken cancellationToken)
    {
        if (!usuarioId.HasValue || usuarioId.Value <= 0)
            return new(false, 401, "No se encontró el usuario autenticado.");

        var datos = await (
            from usuario in db.Usuarios.AsNoTracking()
            join rol in db.Roles.AsNoTracking() on usuario.rolId equals rol.rolId
            where usuario.UsuarioId == usuarioId.Value && usuario.activo && rol.activo
            select new { usuario.rolId, rol.nombreRol }).FirstOrDefaultAsync(cancellationToken);

        if (datos is null)
            return new(false, 401, "El usuario no está activo.");

        if (datos.nombreRol.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
            return new(true, 200, string.Empty);

        bool permitido = await (
            from relacion in db.RolInterfaz.AsNoTracking()
            join interfaz in db.Interfaz.AsNoTracking() on relacion.interfazId equals interfaz.interfazId
            where relacion.rolId == datos.rolId && interfaz.activo && interfaz.nombreInterfaz == Permiso
            select operacion == "leer" ? relacion.leer == true
                 : operacion == "actualizar" ? relacion.actualizar == true
                 : operacion == "eliminar" ? relacion.eliminar == true
                 : false).FirstOrDefaultAsync(cancellationToken);

        return permitido
            ? new(true, 200, string.Empty)
            : new(false, 403, "No tiene permiso para realizar esta acción sobre análisis de suelo.");
    }

    private static async Task<ManifiestoRestauracion> ConstruirManifiestoAsync(
        DbConnection connection,
        DbTransaction transaction,
        int analisisSueloId,
        CancellationToken cancellationToken)
    {
        ManifiestoRestauracion m = new() { AnalisisSueloId = analisisSueloId };
        m.ElementosOriginalesIds = await ConsultarIdsAsync(connection, transaction,
            "SELECT analisisSueloElementoQuimicoId FROM dbo.analisisSueloElementoQuimico WHERE analisisSueloId=@id AND activo=1", analisisSueloId, cancellationToken);
        m.AnalisisSueloCalculoIds = await ConsultarIdsAsync(connection, transaction,
            "SELECT analisisSueloCalculoId FROM dbo.analisisSueloCalculo WHERE analisisSueloId=@id AND activo=1", analisisSueloId, cancellationToken);

        if (m.AnalisisSueloCalculoIds.Count > 0)
        {
            string ids = string.Join(',', m.AnalisisSueloCalculoIds);
            m.ElementosCalculadosIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT analisisSueloCalculoElementoQuimicoId FROM dbo.analisisSueloCalculoElementoQuimico WHERE analisisSueloCalculoId IN ({ids}) AND activo=1", cancellationToken);
            m.FormulaNutricionalIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT formulaNutricionalId FROM dbo.formulaNutricional WHERE analisisSueloCalculoId IN ({ids}) AND activo=1", cancellationToken);
            m.EnmiendaCalcareaIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT enmiendaCalcareaId FROM dbo.enmiendaCalcarea WHERE analisisSueloCalculoId IN ({ids}) AND activo=1", cancellationToken);
            m.FertilizacionMixtaIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT fertilizacionMixtaId FROM dbo.fertilizacionMixta WHERE analisisSueloCalculoId IN ({ids}) AND activo=1", cancellationToken);
        }

        if (m.FormulaNutricionalIds.Count > 0)
        {
            string ids = string.Join(',', m.FormulaNutricionalIds);
            m.FormulaDetalleIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT formulaNutricionalDetalleId FROM dbo.formulaNutricionalDetalle WHERE formulaNutricionalId IN ({ids}) AND activo=1", cancellationToken);
            m.FormulaAporteIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT formulaNutricionalAporteId FROM dbo.formulaNutricionalAporte WHERE formulaNutricionalId IN ({ids}) AND activo=1", cancellationToken);
        }

        if (m.FertilizacionMixtaIds.Count > 0)
        {
            string ids = string.Join(',', m.FertilizacionMixtaIds);
            m.MixtaFuenteIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT fertilizacionMixtaFuenteId FROM dbo.fertilizacionMixtaFuente WHERE fertilizacionMixtaId IN ({ids}) AND activo=1", cancellationToken);
            m.MixtaDetalleIds = await ConsultarIdsSinParametroAsync(connection, transaction,
                $"SELECT fertilizacionMixtaDetalleId FROM dbo.fertilizacionMixtaDetalle WHERE fertilizacionMixtaId IN ({ids}) AND activo=1", cancellationToken);
        }

        return m;
    }

    private static async Task AplicarEstadoAsync(
        DbConnection connection,
        DbTransaction transaction,
        ManifiestoRestauracion m,
        bool activo,
        CancellationToken cancellationToken)
    {
        await ActualizarPorIdAsync(connection, transaction, "analisisSuelo", "analisisSueloId", [m.AnalisisSueloId], activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "analisisSueloElementoQuimico", "analisisSueloElementoQuimicoId", m.ElementosOriginalesIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "analisisSueloCalculo", "analisisSueloCalculoId", m.AnalisisSueloCalculoIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "analisisSueloCalculoElementoQuimico", "analisisSueloCalculoElementoQuimicoId", m.ElementosCalculadosIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "formulaNutricional", "formulaNutricionalId", m.FormulaNutricionalIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "formulaNutricionalDetalle", "formulaNutricionalDetalleId", m.FormulaDetalleIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "formulaNutricionalAporte", "formulaNutricionalAporteId", m.FormulaAporteIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "enmiendaCalcarea", "enmiendaCalcareaId", m.EnmiendaCalcareaIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "fertilizacionMixta", "fertilizacionMixtaId", m.FertilizacionMixtaIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "fertilizacionMixtaFuente", "fertilizacionMixtaFuenteId", m.MixtaFuenteIds, activo, cancellationToken);
        await ActualizarPorIdAsync(connection, transaction, "fertilizacionMixtaDetalle", "fertilizacionMixtaDetalleId", m.MixtaDetalleIds, activo, cancellationToken);
    }

    private static async Task ActualizarPorIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tabla,
        string columnaId,
        IReadOnlyCollection<int> ids,
        bool activo,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        string lista = string.Join(',', ids.Distinct());
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE dbo.[{tabla}] SET activo={(activo ? 1 : 0)} WHERE [{columnaId}] IN ({lista});";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ExisteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        int id,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AgregarParametro(command, "@id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<List<int>> ConsultarIdsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        int id,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AgregarParametro(command, "@id", id);
        return await LeerIdsAsync(command, cancellationToken);
    }

    private static async Task<List<int>> ConsultarIdsSinParametroAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await LeerIdsAsync(command, cancellationToken);
    }

    private static async Task<List<int>> LeerIdsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        List<int> ids = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    private static async Task InsertarEliminacionAsync(
        DbConnection connection,
        DbTransaction transaction,
        int analisisSueloId,
        int calculoId,
        int usuarioId,
        string motivo,
        string json,
        byte[]? pdf,
        string? nombrePdf,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO dbo.analisisSueloEliminacion
(
    analisisSueloId, analisisSueloCalculoId, usuarioEliminacionId,
    fechaEliminacionUtc, motivoEliminacion, manifiestoRestauracionJson,
    pdfHistorico, nombreArchivoPdf, estado
)
VALUES
(
    @analisisId, @calculoId, @usuarioId,
    SYSUTCDATETIME(), @motivo, @json,
    @pdf, @nombrePdf, N'ELIMINADO'
);
""";
        AgregarParametro(command, "@analisisId", analisisSueloId);
        AgregarParametro(command, "@calculoId", calculoId);
        AgregarParametro(command, "@usuarioId", usuarioId);
        AgregarParametro(command, "@motivo", motivo);
        AgregarParametro(command, "@json", json);
        AgregarParametro(command, "@pdf", (object?)pdf ?? DBNull.Value);
        AgregarParametro(command, "@nombrePdf", (object?)nombrePdf ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<EliminacionGuardada?> ObtenerEliminacionPendienteAsync(
        DbConnection connection,
        DbTransaction transaction,
        int analisisSueloId,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT TOP(1) analisisSueloEliminacionId, manifiestoRestauracionJson
FROM dbo.analisisSueloEliminacion
WHERE analisisSueloId=@id AND estado=N'ELIMINADO'
ORDER BY fechaEliminacionUtc DESC;
""";
        AgregarParametro(command, "@id", analisisSueloId);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new(reader.GetInt64(0), reader.GetString(1));
    }

    private async Task<(byte[]? Pdf, string? NombreArchivo)> ConsultarPdfHistoricoAsync(
        int calculoId,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT TOP(1) pdfHistorico, nombreArchivoPdf
FROM dbo.analisisSueloEliminacion
WHERE analisisSueloCalculoId=@id AND pdfHistorico IS NOT NULL
ORDER BY fechaEliminacionUtc DESC;
""";
        AgregarParametro(command, "@id", calculoId);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (null, null);
        return ((byte[])reader[0], reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static void AgregarParametro(DbCommand command, string nombre, object valor)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = nombre;
        parameter.Value = valor;
        command.Parameters.Add(parameter);
    }

    private sealed record ResultadoPermiso(bool Permitido, int StatusCode, string Mensaje);
    private sealed record EliminacionGuardada(long Id, string ManifiestoJson);

    private sealed class ManifiestoRestauracion
    {
        public int AnalisisSueloId { get; set; }
        public List<int> ElementosOriginalesIds { get; set; } = [];
        public List<int> AnalisisSueloCalculoIds { get; set; } = [];
        public List<int> ElementosCalculadosIds { get; set; } = [];
        public List<int> FormulaNutricionalIds { get; set; } = [];
        public List<int> FormulaDetalleIds { get; set; } = [];
        public List<int> FormulaAporteIds { get; set; } = [];
        public List<int> EnmiendaCalcareaIds { get; set; } = [];
        public List<int> FertilizacionMixtaIds { get; set; } = [];
        public List<int> MixtaFuenteIds { get; set; } = [];
        public List<int> MixtaDetalleIds { get; set; } = [];
    }
}