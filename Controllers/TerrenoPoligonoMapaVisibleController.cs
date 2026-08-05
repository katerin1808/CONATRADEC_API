using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.TerrenoPoligonoDto;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Entrega únicamente las delimitaciones requeridas por la vista actual
/// del Centro Geoespacial. Evita enviar todos los polígonos del país en la
/// carga inicial.
/// </summary>
[ApiController]
[Authorize]
[Route("api/terreno-poligono")]
public sealed class TerrenoPoligonoMapaVisibleController : ControllerBase
{
    private const string PermisoTerreno = "terrenoPage";
    private const string PermisoMapa = "MapaTerrenosWeb";

    private readonly DBContext db;
    private readonly PermisoApiService permisos;
    private readonly ILogger<TerrenoPoligonoMapaVisibleController> logger;

    public TerrenoPoligonoMapaVisibleController(
        DBContext db,
        PermisoApiService permisos,
        ILogger<TerrenoPoligonoMapaVisibleController> logger)
    {
        this.db = db;
        this.permisos = permisos;
        this.logger = logger;
    }

    [HttpGet("mapa-visible")]
    public async Task<IActionResult> ListarVisible(
        [FromQuery] double? sur,
        [FromQuery] double? norte,
        [FromQuery] double? oeste,
        [FromQuery] double? este,
        [FromQuery] int zoom = 0,
        [FromQuery] string? departamento = null,
        [FromQuery] string? municipio = null,
        [FromQuery] int limite = 900,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarLecturaMapaAsync(
            cancellationToken);

        if (acceso is not null)
            return acceso;

        bool tieneTerritorio =
            !string.IsNullOrWhiteSpace(departamento) ||
            !string.IsNullOrWhiteSpace(municipio);

        bool limitesValidos =
            sur.HasValue && norte.HasValue &&
            oeste.HasValue && este.HasValue &&
            sur.Value < norte.Value &&
            oeste.Value < este.Value;

        if (!tieneTerritorio && (!limitesValidos || zoom < 13))
            return Ok(Array.Empty<RespuestaDto>());

        limite = Math.Clamp(limite, 1, 1200);

        const string sql = """
            SELECT TOP (@limite)
                t.terrenoId,
                t.codigoTerreno,
                t.latitud,
                t.longitud,
                t.extensionManzanaTerreno,
                p.geometriaGeoJson,
                p.areaMetrosCuadrados,
                p.areaHectareas,
                p.areaManzanasCalculada,
                p.fechaActualizacionUtc
            FROM dbo.terrenoPoligono p
            INNER JOIN dbo.terreno t
                ON t.terrenoId = p.terrenoId
               AND t.activo = 1
            INNER JOIN dbo.municipio m
                ON m.municipioId = t.municipioId
               AND m.activo = 1
            INNER JOIN dbo.departamento d
                ON d.departamentoId = m.departamentoId
               AND d.activo = 1
            WHERE p.activo = 1
              AND
              (
                  (
                      @usarTerritorio = 1
                      AND
                      (
                          @departamento = N''
                          OR d.nombreDepartamento = @departamento
                      )
                      AND
                      (
                          @municipio = N''
                          OR m.nombreMunicipio = @municipio
                      )
                  )
                  OR
                  (
                      @usarTerritorio = 0
                      AND t.latitud BETWEEN @sur AND @norte
                      AND t.longitud BETWEEN @oeste AND @este
                  )
              )
            ORDER BY t.codigoTerreno, t.terrenoId;
            """;

        try
        {
            List<RespuestaDto> resultados = await ConsultarAsync(
                sql,
                command =>
                {
                    AgregarParametro(command, "@limite", limite);
                    AgregarParametro(
                        command,
                        "@usarTerritorio",
                        tieneTerritorio ? 1 : 0);
                    AgregarParametro(
                        command,
                        "@departamento",
                        departamento?.Trim() ?? string.Empty);
                    AgregarParametro(
                        command,
                        "@municipio",
                        municipio?.Trim() ?? string.Empty);
                    AgregarParametro(command, "@sur", sur ?? -90d);
                    AgregarParametro(command, "@norte", norte ?? 90d);
                    AgregarParametro(command, "@oeste", oeste ?? -180d);
                    AgregarParametro(command, "@este", este ?? 180d);
                },
                cancellationToken);

            return Ok(resultados);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return Ok(Array.Empty<RespuestaDto>());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error consultando polígonos visibles del mapa.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message =
                        "No fue posible cargar las delimitaciones visibles."
                });
        }
    }

    private async Task<List<RespuestaDto>> ConsultarAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        DbConnection conexion = db.Database.GetDbConnection();
        bool cerrar = conexion.State != ConnectionState.Open;

        if (cerrar)
            await conexion.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command = conexion.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;
            configurar(command);

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var resultados = new List<RespuestaDto>();

            while (await reader.ReadAsync(cancellationToken))
            {
                string? geoJson = reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5);

                List<VerticeDto> vertices =
                    TerrenoPoligonoGeometria.LeerVertices(geoJson);

                if (vertices.Count < 3)
                    continue;

                decimal extension = reader.GetDecimal(4);
                decimal areaManzanas = reader.IsDBNull(8)
                    ? 0
                    : reader.GetDecimal(8);
                decimal diferencia = decimal.Round(
                    Math.Abs(extension - areaManzanas),
                    4);
                decimal? porcentaje = extension > 0
                    ? decimal.Round(diferencia / extension * 100m, 2)
                    : null;
                decimal latitud = reader.GetDecimal(2);
                decimal longitud = reader.GetDecimal(3);

                resultados.Add(new RespuestaDto
                {
                    TerrenoId = reader.GetInt32(0),
                    CodigoTerreno = reader.GetString(1),
                    TienePoligono = true,
                    LatitudPunto = latitud,
                    LongitudPunto = longitud,
                    ExtensionRegistradaManzanas = extension,
                    Vertices = vertices,
                    AreaMetrosCuadrados = reader.IsDBNull(6)
                        ? 0
                        : reader.GetDecimal(6),
                    AreaHectareas = reader.IsDBNull(7)
                        ? 0
                        : reader.GetDecimal(7),
                    AreaManzanasCalculada = areaManzanas,
                    DiferenciaManzanas = diferencia,
                    DiferenciaPorcentaje = porcentaje,
                    PuntoDentroPoligono =
                        TerrenoPoligonoGeometria.ContienePunto(
                            vertices,
                            latitud,
                            longitud),
                    FechaActualizacionUtc = reader.IsDBNull(9)
                        ? null
                        : reader.GetDateTime(9)
                });
            }

            return resultados;
        }
        finally
        {
            if (cerrar)
                await conexion.CloseAsync();
        }
    }

    private async Task<IActionResult?> ValidarLecturaMapaAsync(
        CancellationToken cancellationToken)
    {
        int? usuarioId = ObtenerUsuarioId();

        ResultadoPermisoApi terreno = await permisos.ValidarAsync(
            usuarioId,
            PermisoTerreno,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (terreno.Permitido)
            return null;

        ResultadoPermisoApi mapa = await permisos.ValidarAsync(
            usuarioId,
            PermisoMapa,
            TipoPermisoApi.Leer,
            cancellationToken);

        return mapa.Permitido
            ? null
            : StatusCode(
                mapa.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene permiso para consultar las delimitaciones."
                });
    }

    private int? ObtenerUsuarioId()
    {
        string? valor =
            User.FindFirstValue("uid") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            Request.Headers["X-Usuario-Id"].FirstOrDefault();

        return int.TryParse(valor, out int usuarioId) && usuarioId > 0
            ? usuarioId
            : null;
    }

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object? valor)
    {
        DbParameter parametro = command.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.Value = valor ?? DBNull.Value;
        command.Parameters.Add(parametro);
    }
}
