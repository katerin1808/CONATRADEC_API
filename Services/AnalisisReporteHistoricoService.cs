using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Reportes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CONATRADEC_API.Services;

public sealed class AnalisisControlHistorialDto
{
    public int AnalisisSueloId { get; set; }
    public int AnalisisSueloCalculoId { get; set; }
    public int VersionRegistro { get; set; } = 1;
    public DateTime? FechaCreacionClienteUtc { get; set; }
    public DateTime? FechaUltimaModificacionUtc { get; set; }
    public DateTime FechaCreacionServidor { get; set; }
    public string OrigenRegistro { get; set; } = "ONLINE";
    public string ETag =>
        $"\"analisis-{AnalisisSueloCalculoId}-v{VersionRegistro}\"";
}

public sealed class AnalisisVersionHistorialDto
{
    public long AnalisisReporteSnapshotId { get; set; }
    public int AnalisisSueloCalculoId { get; set; }
    public int VersionRegistro { get; set; }
    public string TipoEvento { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public DateTime? FechaCreacionClienteUtc { get; set; }
    public DateTime? FechaOperacionClienteUtc { get; set; }
    public DateTime FechaOperacionUtc { get; set; }
    public int? UsuarioId { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public bool Vigente { get; set; }
}

/// <summary>
/// Administra versiones inmutables del reporte.
///
/// La información visible de un PDF o Excel deja de depender de catálogos
/// modificables. Cada creación, edición y eliminación conserva una fotografía
/// JSON completa, con hash SHA-256 y número de versión.
/// </summary>
public sealed class AnalisisReporteHistoricoService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

    private readonly DBContext db;
    private readonly AnalisisReporteDatosService datosService;
    private readonly ILogger<AnalisisReporteHistoricoService> logger;

    public AnalisisReporteHistoricoService(
        DBContext db,
        AnalisisReporteDatosService datosService,
        ILogger<AnalisisReporteHistoricoService> logger)
    {
        this.db = db;
        this.datosService = datosService;
        this.logger = logger;
    }

    public async Task<AnalisisControlHistorialDto?> ObtenerControlAsync(
        int analisisSueloCalculoId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT TOP (1)
    a.analisisSueloId,
    c.analisisSueloCalculoId,
    ISNULL(c.versionRegistro, 1) AS versionRegistro,
    c.fechaCreacionClienteUtc,
    c.fechaUltimaModificacionUtc,
    a.fechaCreacionAnalisisSuelo,
    ISNULL(NULLIF(LTRIM(RTRIM(c.origenRegistro)), N''), N'ONLINE')
        AS origenRegistro
FROM dbo.analisisSueloCalculo AS c
INNER JOIN dbo.analisisSuelo AS a
    ON a.analisisSueloId = c.analisisSueloId
WHERE c.analisisSueloCalculoId = @id;
""";

        return await ConsultarUnoAsync(
            sql,
            command => AgregarParametro(command, "@id", analisisSueloCalculoId),
            reader => new AnalisisControlHistorialDto
            {
                AnalisisSueloId = reader.GetInt32(0),
                AnalisisSueloCalculoId = reader.GetInt32(1),
                VersionRegistro = Math.Max(1, reader.GetInt32(2)),
                FechaCreacionClienteUtc = LeerFechaNullable(reader, 3),
                FechaUltimaModificacionUtc = LeerFechaNullable(reader, 4),
                FechaCreacionServidor = reader.GetDateTime(5),
                OrigenRegistro = reader.IsDBNull(6)
                    ? "ONLINE"
                    : reader.GetString(6).Trim().ToUpperInvariant()
            },
            cancellationToken);
    }

    public static int? ObtenerVersionDesdeETag(
        string? etag,
        int analisisSueloCalculoId)
    {
        if (string.IsNullOrWhiteSpace(etag))
            return null;

        string valor = etag.Trim().Trim('"');
        string prefijo = $"analisis-{analisisSueloCalculoId}-v";

        if (!valor.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(valor[prefijo.Length..], out int version) &&
               version > 0
            ? version
            : null;
    }

    public async Task<bool> OperacionOfflineCompletadaAsync(
        Guid operacionLocalId,
        CancellationToken cancellationToken = default)
    {
        if (operacionLocalId == Guid.Empty)
            return false;

        const string sql = """
IF OBJECT_ID(N'dbo.analisisOfflineOperacion', N'U') IS NULL
BEGIN
    SELECT CAST(0 AS INT);
END
ELSE
BEGIN
    SELECT CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.analisisOfflineOperacion
        WHERE operacionLocalId = @id
          AND estado = N'COMPLETADO'
    ) THEN 1 ELSE 0 END;
END;
""";

        int? result = await ConsultarEscalarEnteroAsync(
            sql,
            command => AgregarParametro(command, "@id", operacionLocalId),
            cancellationToken);

        return result == 1;
    }

    public async Task<int?> BuscarCalculoPorIdentificadorAsync(
        string? identificador,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identificador))
            return null;

        string valor = identificador.Trim();

        return await (
            from analisis in db.AnalisisSuelos.AsNoTracking()
            join calculo in db.AnalisisSueloCalculos.AsNoTracking()
                on analisis.analisisSueloId equals calculo.analisisSueloId
            where analisis.identificadorAnalisisSuelo == valor
            orderby calculo.analisisSueloCalculoId descending
            select (int?)calculo.analisisSueloCalculoId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<int>> ObtenerCalculosPorAnalisisAsync(
        int analisisSueloId,
        CancellationToken cancellationToken = default)
    {
        return await db.AnalisisSueloCalculos
            .AsNoTracking()
            .Where(x => x.analisisSueloId == analisisSueloId)
            .OrderBy(x => x.analisisSueloCalculoId)
            .Select(x => x.analisisSueloCalculoId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<int>> ObtenerCalculosSinSnapshotAsync(
        int limite,
        CancellationToken cancellationToken = default)
    {
        limite = Math.Clamp(limite, 1, 200);

        const string sql = """
SELECT TOP (@limite)
    c.analisisSueloCalculoId
FROM dbo.analisisSueloCalculo AS c
INNER JOIN dbo.analisisSuelo AS a
    ON a.analisisSueloId = c.analisisSueloId
WHERE c.activo = 1
  AND a.activo = 1
  AND
  (
      c.fechaUltimaModificacionUtc IS NOT NULL
      OR a.fechaCreacionAnalisisSuelo < DATEADD(MINUTE, -2, GETDATE())
  )
  AND NOT EXISTS
(
    SELECT 1
    FROM dbo.analisisReporteSnapshot AS s
    WHERE s.analisisSueloCalculoId = c.analisisSueloCalculoId
      AND s.versionRegistro = ISNULL(c.versionRegistro, 1)
      AND s.activo = 1
)
ORDER BY c.analisisSueloCalculoId;
""";

        return await ConsultarListaAsync(
            sql,
            command => AgregarParametro(command, "@limite", limite),
            reader => reader.GetInt32(0),
            cancellationToken);
    }

    public async Task InicializarMetadatosCreacionAsync(
        int analisisSueloCalculoId,
        DateTime? fechaCreacionClienteUtc,
        string? origen,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE c
SET
    c.fechaCreacionClienteUtc =
        COALESCE(c.fechaCreacionClienteUtc, @fechaCreacionClienteUtc),
    c.fechaUltimaModificacionUtc = SYSUTCDATETIME(),
    c.versionRegistro = 1,
    c.origenRegistro = @origen
FROM dbo.analisisSueloCalculo AS c
WHERE c.analisisSueloCalculoId = @id;
""";

        await EjecutarAsync(
            sql,
            command =>
            {
                AgregarParametro(command, "@id", analisisSueloCalculoId);
                AgregarParametro(
                    command,
                    "@fechaCreacionClienteUtc",
                    NormalizarUtc(fechaCreacionClienteUtc));
                AgregarParametro(command, "@origen", NormalizarOrigen(origen));
            },
            cancellationToken);
    }

    public async Task<int> IncrementarVersionAsync(
        int analisisSueloCalculoId,
        string? origen,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE c
SET
    c.versionRegistro = ISNULL(c.versionRegistro, 1) + 1,
    c.fechaUltimaModificacionUtc = SYSUTCDATETIME()
FROM dbo.analisisSueloCalculo AS c
WHERE c.analisisSueloCalculoId = @id;

SELECT ISNULL(c.versionRegistro, 1)
FROM dbo.analisisSueloCalculo AS c
WHERE c.analisisSueloCalculoId = @id;
""";

        int? version = await ConsultarEscalarEnteroAsync(
            sql,
            command =>
            {
                AgregarParametro(command, "@id", analisisSueloCalculoId);
            },
            cancellationToken);

        return Math.Max(1, version ?? 1);
    }

    public async Task<bool> ExisteVersionAsync(
        int analisisSueloCalculoId,
        int versionRegistro,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.analisisReporteSnapshot
    WHERE analisisSueloCalculoId = @id
      AND versionRegistro = @version
      AND activo = 1
)
THEN 1 ELSE 0 END;
""";

        int? existe = await ConsultarEscalarEnteroAsync(
            sql,
            command =>
            {
                AgregarParametro(command, "@id", analisisSueloCalculoId);
                AgregarParametro(command, "@version", versionRegistro);
            },
            cancellationToken);

        return existe == 1;
    }

    public async Task<AnalisisReporte?> ObtenerReporteAsync(
        int analisisSueloCalculoId,
        int? versionRegistro = null,
        CancellationToken cancellationToken = default)
    {
        string? json = await ObtenerJsonSnapshotAsync(
            analisisSueloCalculoId,
            versionRegistro,
            cancellationToken);

        if (json == null && !versionRegistro.HasValue)
        {
            AnalisisControlHistorialDto? control =
                await ObtenerControlAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

            if (control != null)
            {
                await CapturarSiFaltaAsync(
                    analisisSueloCalculoId,
                    control.VersionRegistro,
                    "LEGADO_INICIAL",
                    usuarioId: null,
                    control.OrigenRegistro,
                    control.FechaCreacionClienteUtc,
                    solicitud: null,
                    cancellationToken);

                json = await ObtenerJsonSnapshotAsync(
                    analisisSueloCalculoId,
                    versionRegistro: null,
                    cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AnalisisReporte>(
                json,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "El snapshot del cálculo {CalculoId} no contiene JSON válido.",
                analisisSueloCalculoId);

            return null;
        }
    }

    public async Task<AnalisisReporte?> ObtenerReporteSinCapturarAsync(
        int analisisSueloCalculoId,
        int? versionRegistro = null,
        CancellationToken cancellationToken = default)
    {
        string? json = await ObtenerJsonSnapshotAsync(
            analisisSueloCalculoId,
            versionRegistro,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AnalisisReporte>(
                json,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "El snapshot del cálculo {CalculoId} no contiene JSON válido.",
                analisisSueloCalculoId);

            return null;
        }
    }

    public async Task<List<AnalisisVersionHistorialDto>> ListarVersionesAsync(
        int analisisSueloCalculoId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
    analisisReporteSnapshotId,
    analisisSueloCalculoId,
    versionRegistro,
    tipoEvento,
    origen,
    fechaCreacionClienteUtc,
    fechaOperacionClienteUtc,
    fechaOperacionUtc,
    usuarioId,
    hashSha256,
    vigente
FROM dbo.analisisReporteSnapshot
WHERE analisisSueloCalculoId = @id
  AND activo = 1
ORDER BY versionRegistro DESC;
""";

        return await ConsultarListaAsync(
            sql,
            command => AgregarParametro(command, "@id", analisisSueloCalculoId),
            reader => new AnalisisVersionHistorialDto
            {
                AnalisisReporteSnapshotId = reader.GetInt64(0),
                AnalisisSueloCalculoId = reader.GetInt32(1),
                VersionRegistro = reader.GetInt32(2),
                TipoEvento = reader.GetString(3),
                Origen = reader.GetString(4),
                FechaCreacionClienteUtc = LeerFechaNullable(reader, 5),
                FechaOperacionClienteUtc = LeerFechaNullable(reader, 6),
                FechaOperacionUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(7),
                    DateTimeKind.Utc),
                UsuarioId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                HashSha256 = reader.GetString(9),
                Vigente = reader.GetBoolean(10)
            },
            cancellationToken);
    }

    public async Task CapturarSiFaltaAsync(
        int analisisSueloCalculoId,
        int versionRegistro,
        string tipoEvento,
        int? usuarioId,
        string? origen,
        DateTime? fechaCreacionClienteUtc,
        GuardarTodoDto? solicitud,
        CancellationToken cancellationToken = default)
    {
        if (await ExisteVersionAsync(
                analisisSueloCalculoId,
                versionRegistro,
                cancellationToken))
        {
            return;
        }

        AnalisisReporte? reporte = await ConstruirReporteAsync(
            analisisSueloCalculoId,
            solicitud,
            origen,
            cancellationToken);

        if (reporte == null)
        {
            throw new InvalidOperationException(
                "No fue posible reconstruir el reporte que se debe versionar.");
        }

        string json = JsonSerializer.Serialize(reporte, JsonOptions);

        await GuardarSnapshotJsonAsync(
            analisisSueloCalculoId,
            versionRegistro,
            tipoEvento,
            usuarioId,
            origen,
            fechaCreacionClienteUtc,
            solicitud?.fechaOperacionClienteUtc,
            json,
            cancellationToken);
    }

    public async Task DuplicarUltimaVersionAsync(
        int analisisSueloCalculoId,
        int nuevaVersion,
        string tipoEvento,
        int? usuarioId,
        string? origen,
        DateTime? fechaCreacionClienteUtc,
        CancellationToken cancellationToken = default)
    {
        string? json = await ObtenerJsonSnapshotAsync(
            analisisSueloCalculoId,
            versionRegistro: null,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return;

        await GuardarSnapshotJsonAsync(
            analisisSueloCalculoId,
            nuevaVersion,
            tipoEvento,
            usuarioId,
            origen,
            fechaCreacionClienteUtc,
            null,
            json,
            cancellationToken);
    }

    public async Task EnriquecerListadoAsync(
        JsonNode? root,
        CancellationToken cancellationToken = default)
    {
        if (root is not JsonObject objeto)
            return;

        JsonNode? data = objeto["data"];
        JsonArray? items = data switch
        {
            JsonObject dataObject => dataObject["items"] as JsonArray,
            JsonArray dataArray => dataArray,
            _ => null
        };

        if (items == null)
            return;

        foreach (JsonNode? node in items)
        {
            if (node is not JsonObject item)
                continue;

            int id = LeerEnteroJson(item["analisisSueloCalculoId"]);
            if (id <= 0)
                continue;

            AnalisisReporte? reporte =
                await ObtenerReporteSinCapturarAsync(
                    id,
                    cancellationToken: cancellationToken);

            if (reporte == null)
                continue;

            item["nombreCliente"] = reporte.Cliente;
            item["nombreTerreno"] = reporte.Terreno;

            if (item.ContainsKey("propietario"))
                item["propietario"] = reporte.Cliente;

            if (item["terreno"] is JsonObject terreno)
            {
                if (terreno["propietario"] is JsonObject propietario)
                    propietario["nombreCompleto"] = reporte.Cliente;
            }
        }
    }

    private async Task<AnalisisReporte?> ConstruirReporteAsync(
        int analisisSueloCalculoId,
        GuardarTodoDto? solicitud,
        string? origen,
        CancellationToken cancellationToken)
    {
        AnalisisReporte? reporteServidor =
            await datosService.ObtenerAsync(
                analisisSueloCalculoId,
                cancellationToken);

        if (reporteServidor == null)
            return null;

        AnalisisReporte? reporteCliente =
            DeserializarReporteCliente(
                solicitud?.reporteHistoricoCliente);

        bool usarModulosCliente =
            reporteCliente != null &&
            string.Equals(
                NormalizarOrigen(origen),
                "OFFLINE",
                StringComparison.OrdinalIgnoreCase);

        if (usarModulosCliente)
        {
            reporteServidor.Balance = reporteCliente!.Balance;
            reporteServidor.Enmienda = reporteCliente.Enmienda;
            reporteServidor.FertilizacionMixta =
                reporteCliente.FertilizacionMixta;

            if (reporteCliente.ValoresLaboratorio.Count > 0)
            {
                reporteServidor.ValoresLaboratorio =
                    reporteCliente.ValoresLaboratorio;
            }

            if (!string.IsNullOrWhiteSpace(reporteCliente.Terreno) &&
                !reporteCliente.Terreno.StartsWith(
                    "Terreno #",
                    StringComparison.OrdinalIgnoreCase))
            {
                reporteServidor.Terreno = reporteCliente.Terreno;
            }

            if (!string.IsNullOrWhiteSpace(reporteCliente.TipoCultivo))
                reporteServidor.TipoCultivo = reporteCliente.TipoCultivo;

            if (!string.IsNullOrWhiteSpace(reporteCliente.TipoAnalisis))
                reporteServidor.TipoAnalisis = reporteCliente.TipoAnalisis;

            if (!string.IsNullOrWhiteSpace(reporteCliente.Responsable))
                reporteServidor.Responsable = reporteCliente.Responsable;

            if (!string.IsNullOrWhiteSpace(
                    reporteCliente.UnidadMateriaOrganica))
            {
                reporteServidor.UnidadMateriaOrganica =
                    reporteCliente.UnidadMateriaOrganica;
            }
        }

        if (solicitud?.requerimientoAnual?.elementos?.Count > 0)
        {
            reporteServidor.Requerimientos =
                solicitud.requerimientoAnual.elementos
                    .Select(x => new AnalisisReporteRequerimiento
                    {
                        Elemento = FormatearElemento(
                            x.nombreElementoQuimico,
                            x.simboloElementoQuimico,
                            x.elementoQuimicosId),
                        CantidadIngresada = x.cantidadIngresada,
                        CantidadConvertidaLbMz = x.cantidadConvertidaLbMz,
                        RequerimientoLbMz = x.requerimientoCalculado,
                        UnidadResultado = string.IsNullOrWhiteSpace(x.unidadResultado)
                            ? "lb/Mz"
                            : x.unidadResultado.Trim(),
                        Clasificacion = x.clasificacion?.Trim() ?? string.Empty,
                        Observacion = x.observacion?.Trim() ?? string.Empty
                    })
                    .ToList();
        }
        else
        {
            reporteServidor.Requerimientos =
                await ObtenerRequerimientosGuardadosAsync(
                    analisisSueloCalculoId,
                    cancellationToken);
        }

        string propietarioHistorico =
            await ObtenerPropietarioHistoricoAsync(
                analisisSueloCalculoId,
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(propietarioHistorico))
            reporteServidor.Cliente = propietarioHistorico;

        return reporteServidor;
    }

    private static AnalisisReporte? DeserializarReporteCliente(
        JsonElement? elemento)
    {
        if (!elemento.HasValue ||
            elemento.Value.ValueKind is JsonValueKind.Null or
                JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            JsonObject? root =
                JsonNode.Parse(elemento.Value.GetRawText())
                    as JsonObject;

            if (root == null)
                return null;

            return new AnalisisReporte
            {
                Terreno = LeerTextoJson(root["terreno"]),
                TipoCultivo = LeerTextoJson(root["tipoCultivo"]),
                TipoAnalisis = LeerTextoJson(root["tipoAnalisis"]),
                Responsable = LeerTextoJson(root["responsable"]),
                UnidadMateriaOrganica =
                    LeerTextoJson(root["unidadMateriaOrganica"]),

                ValoresLaboratorio =
                    root["valoresLaboratorio"]?
                        .Deserialize<List<AnalisisReporteValorLaboratorio>>(
                            JsonOptions) ??
                    new List<AnalisisReporteValorLaboratorio>(),

                Balance = root["balance"]?
                    .Deserialize<AnalisisReporteBalance>(JsonOptions),

                Enmienda = root["enmienda"]?
                    .Deserialize<AnalisisReporteEnmienda>(JsonOptions),

                FertilizacionMixta =
                    root["fertilizacionMixta"]?
                        .Deserialize<
                            AnalisisReporteFertilizacionMixta>(
                                JsonOptions)
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<AnalisisReporteRequerimiento>>
        ObtenerRequerimientosGuardadosAsync(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
    {
        var filas = await (
            from valor in db.AnalisisSueloCalculoElementoQuimicos.AsNoTracking()
            join elemento in db.elementoQuimico.AsNoTracking()
                on valor.elementoQuimicosId equals elemento.elementoQuimicosId
            where valor.analisisSueloCalculoId == analisisSueloCalculoId &&
                  valor.activo
            select new
            {
                valor.elementoQuimicosId,
                elemento.nombreElementoQuimico,
                elemento.simboloElementoQuimico,
                valor.cantidadIngresada,
                valor.cantidadConvertidaLbMz,
                valor.requerimientoCalculado,
                valor.clasificacion,
                valor.observacion
            }).ToListAsync(cancellationToken);

        return filas
            .Select(x => new AnalisisReporteRequerimiento
            {
                Elemento = FormatearElemento(
                    x.nombreElementoQuimico,
                    x.simboloElementoQuimico,
                    x.elementoQuimicosId),
                CantidadIngresada = x.cantidadIngresada,
                CantidadConvertidaLbMz = x.cantidadConvertidaLbMz,
                RequerimientoLbMz = x.requerimientoCalculado,
                UnidadResultado = "lb/Mz",
                Clasificacion = x.clasificacion ?? string.Empty,
                Observacion = x.observacion ?? string.Empty
            })
            .ToList();
    }

    private async Task<string> ObtenerPropietarioHistoricoAsync(
        int analisisSueloCalculoId,
        CancellationToken cancellationToken)
    {
        var datos = await (
            from calculo in db.AnalisisSueloCalculos.AsNoTracking()
            join analisis in db.AnalisisSuelos.AsNoTracking()
                on calculo.analisisSueloId equals analisis.analisisSueloId
            where calculo.analisisSueloCalculoId == analisisSueloCalculoId
            select new
            {
                calculo.terrenoId,
                calculo.fechaCalculo,
                analisis.fechaCreacionAnalisisSuelo
            }).FirstOrDefaultAsync(cancellationToken);

        if (datos == null)
            return string.Empty;

        AnalisisControlHistorialDto? control =
            await ObtenerControlAsync(
                analisisSueloCalculoId,
                cancellationToken);

        bool fueCreadoOffline = string.Equals(
            control?.OrigenRegistro,
            "OFFLINE",
            StringComparison.OrdinalIgnoreCase);

        DateTime referenciaUtc =
            fueCreadoOffline && control?.FechaCreacionClienteUtc != null
                ? control.FechaCreacionClienteUtc.Value
                : ConvertirFechaServidorAUtc(
                    datos.fechaCreacionAnalisisSuelo != default
                        ? datos.fechaCreacionAnalisisSuelo
                        : datos.fechaCalculo);

        string propietario = await db.PropietarioTerrenos
            .AsNoTracking()
            .Where(relacion =>
                relacion.terrenoId == datos.terrenoId &&
                relacion.fechaAsignacionUtc <= referenciaUtc &&
                (!relacion.fechaDesasignacionUtc.HasValue ||
                 relacion.fechaDesasignacionUtc.Value > referenciaUtc))
            .OrderByDescending(relacion => relacion.fechaAsignacionUtc)
            .Select(relacion => relacion.Propietario.nombreCompleto)
            .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(propietario))
            return propietario;

        return await db.PropietarioTerrenos
            .AsNoTracking()
            .Where(relacion => relacion.terrenoId == datos.terrenoId)
            .OrderBy(relacion => relacion.fechaAsignacionUtc)
            .Select(relacion => relacion.Propietario.nombreCompleto)
            .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;
    }

    private async Task GuardarSnapshotJsonAsync(
        int analisisSueloCalculoId,
        int versionRegistro,
        string tipoEvento,
        int? usuarioId,
        string? origen,
        DateTime? fechaCreacionClienteUtc,
        DateTime? fechaOperacionClienteUtc,
        string json,
        CancellationToken cancellationToken)
    {
        if (await ExisteVersionAsync(
                analisisSueloCalculoId,
                versionRegistro,
                cancellationToken))
        {
            return;
        }

        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            const string desactivar = """
UPDATE dbo.analisisReporteSnapshot
SET vigente = 0
WHERE analisisSueloCalculoId = @id
  AND activo = 1;
""";

            await EjecutarAsync(
                desactivar,
                command => AgregarParametro(command, "@id", analisisSueloCalculoId),
                cancellationToken);

            const string insertar = """
INSERT INTO dbo.analisisReporteSnapshot
(
    analisisSueloCalculoId,
    versionRegistro,
    tipoEvento,
    origen,
    fechaCreacionClienteUtc,
    fechaOperacionClienteUtc,
    fechaOperacionUtc,
    usuarioId,
    datosJson,
    hashSha256,
    vigente,
    activo
)
VALUES
(
    @id,
    @version,
    @evento,
    @origen,
    @fechaCliente,
    @fechaOperacionCliente,
    SYSUTCDATETIME(),
    @usuarioId,
    @json,
    @hash,
    1,
    1
);
""";

            await EjecutarAsync(
                insertar,
                command =>
                {
                    AgregarParametro(command, "@id", analisisSueloCalculoId);
                    AgregarParametro(command, "@version", Math.Max(1, versionRegistro));
                    AgregarParametro(command, "@evento", NormalizarEvento(tipoEvento));
                    AgregarParametro(command, "@origen", NormalizarOrigen(origen));
                    AgregarParametro(
                        command,
                        "@fechaCliente",
                        NormalizarUtc(fechaCreacionClienteUtc));
                    AgregarParametro(
                        command,
                        "@fechaOperacionCliente",
                        NormalizarUtc(fechaOperacionClienteUtc));
                    AgregarParametro(command, "@usuarioId", usuarioId);
                    AgregarParametro(command, "@json", json);
                    AgregarParametro(command, "@hash", hash);
                },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string?> ObtenerJsonSnapshotAsync(
        int analisisSueloCalculoId,
        int? versionRegistro,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (1) datosJson
FROM dbo.analisisReporteSnapshot
WHERE analisisSueloCalculoId = @id
  AND activo = 1
  AND (@version IS NULL OR versionRegistro = @version)
ORDER BY
    CASE WHEN @version IS NULL THEN vigente ELSE 1 END DESC,
    versionRegistro DESC;
""";

        return await ConsultarUnoAsync(
            sql,
            command =>
            {
                AgregarParametro(command, "@id", analisisSueloCalculoId);
                AgregarParametro(command, "@version", versionRegistro);
            },
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            cancellationToken);
    }

    private async Task<T?> ConsultarUnoAsync<T>(
        string sql,
        Action<DbCommand> configurar,
        Func<DbDataReader, T> mapear,
        CancellationToken cancellationToken)
    {
        List<T> filas = await ConsultarListaAsync(
            sql,
            configurar,
            mapear,
            cancellationToken);

        return filas.Count == 0 ? default : filas[0];
    }

    private async Task<List<T>> ConsultarListaAsync<T>(
        string sql,
        Action<DbCommand> configurar,
        Func<DbDataReader, T> mapear,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            configurar(command);

            var resultado = new List<T>();

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(mapear(reader));

            return resultado;
        }
        finally
        {
            if (cerrar)
                await connection.CloseAsync();
        }
    }

    private async Task<int?> ConsultarEscalarEnteroAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            configurar(command);

            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null || value == DBNull.Value
                ? null
                : Convert.ToInt32(value);
        }
        finally
        {
            if (cerrar)
                await connection.CloseAsync();
        }
    }

    private async Task EjecutarAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            configurar(command);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (cerrar)
                await connection.CloseAsync();
        }
    }

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object? valor)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = nombre;
        parameter.Value = valor ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTime? LeerFechaNullable(
        DbDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    private static DateTime? NormalizarUtc(DateTime? fecha)
    {
        if (!fecha.HasValue)
            return null;

        return fecha.Value.Kind switch
        {
            DateTimeKind.Utc => fecha.Value,
            DateTimeKind.Local => fecha.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fecha.Value, DateTimeKind.Utc)
        };
    }

    private static DateTime ConvertirFechaServidorAUtc(DateTime fecha)
    {
        if (fecha.Kind == DateTimeKind.Utc)
            return fecha;

        return DateTime.SpecifyKind(fecha, DateTimeKind.Local)
            .ToUniversalTime();
    }

    private static string NormalizarOrigen(string? origen) =>
        string.Equals(origen?.Trim(), "OFFLINE", StringComparison.OrdinalIgnoreCase)
            ? "OFFLINE"
            : "ONLINE";

    private static string NormalizarEvento(string? evento)
    {
        string valor = string.IsNullOrWhiteSpace(evento)
            ? "CAPTURA"
            : evento.Trim().ToUpperInvariant();

        return valor.Length <= 30 ? valor : valor[..30];
    }

    private static string FormatearElemento(
        string? nombre,
        string? simbolo,
        int id)
    {
        string n = nombre?.Trim() ?? string.Empty;
        string s = simbolo?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(n) && !string.IsNullOrWhiteSpace(s))
            return $"{n} ({s})";

        if (!string.IsNullOrWhiteSpace(n))
            return n;

        if (!string.IsNullOrWhiteSpace(s))
            return s;

        return $"Elemento #{id}";
    }

    private static string LeerTextoJson(JsonNode? node)
    {
        if (node == null)
            return string.Empty;

        try
        {
            return node.GetValue<string>()?.Trim() ?? string.Empty;
        }
        catch
        {
            return node.ToString().Trim();
        }
    }

    private static int LeerEnteroJson(JsonNode? node)
    {
        if (node == null)
            return 0;

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return int.TryParse(node.ToString(), out int value) ? value : 0;
        }
    }
}
