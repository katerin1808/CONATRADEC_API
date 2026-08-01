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
/// Administra la delimitación opcional de un terreno.
///
/// El punto principal continúa almacenado en dbo.terreno.latitud y
/// dbo.terreno.longitud. Estos endpoints nunca lo sustituyen.
/// </summary>
[ApiController]
[Authorize]
[Route("api/terreno-poligono")]
public sealed class TerrenoPoligonoController : ControllerBase
{
    private const string PermisoTerreno = "terrenoPage";
    private const string PermisoMapa = "MapaTerrenosWeb";

    private readonly DBContext db;
    private readonly PermisoApiService permisos;
    private readonly ILogger<TerrenoPoligonoController> logger;

    public TerrenoPoligonoController(
        DBContext db,
        PermisoApiService permisos,
        ILogger<TerrenoPoligonoController> logger)
    {
        this.db = db;
        this.permisos = permisos;
        this.logger = logger;
    }

    [HttpGet("{terrenoId:int}")]
    public async Task<IActionResult> Obtener(
        int terrenoId,
        CancellationToken cancellationToken)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Leer,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        RespuestaDto? resultado = await ObtenerUnoAsync(
            terrenoId,
            usuarioPropietarioId: null,
            cancellationToken);

        return resultado is null
            ? NotFound(new
            {
                success = false,
                message = "No se encontró el terreno solicitado."
            })
            : Ok(resultado);
    }

    /// <summary>
    /// Polígonos activos para el mapa administrativo.
    /// Los terrenos sin delimitación no se incluyen.
    /// </summary>
    [HttpGet("mapa")]
    [HttpGet("resumen")]
    public async Task<IActionResult> ListarAdministracion(
        CancellationToken cancellationToken)
    {
        IActionResult? acceso = await ValidarLecturaMapaAsync(
            cancellationToken);

        if (acceso is not null)
            return acceso;

        return Ok(await ListarAsync(
            usuarioPropietarioId: null,
            cancellationToken));
    }

    /// <summary>
    /// Polígonos de los terrenos pertenecientes al propietario vinculado
    /// con el usuario autenticado. No recibe propietarioId del navegador.
    /// </summary>
    [HttpGet("mis-terrenos")]
    public async Task<IActionResult> ListarMisTerrenos(
        CancellationToken cancellationToken)
    {
        int? usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
        {
            return Unauthorized(new
            {
                success = false,
                message = "No se encontró el usuario autenticado."
            });
        }

        return Ok(await ListarAsync(
            usuarioId.Value,
            cancellationToken));
    }

    [HttpPost("{terrenoId:int}")]
    public async Task<IActionResult> Crear(
        int terrenoId,
        [FromBody] GuardarDto dto,
        CancellationToken cancellationToken)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Agregar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        TerrenoPoligonoGeometria.Resultado geometria;

        try
        {
            geometria = TerrenoPoligonoGeometria.ValidarYCalcular(
                dto.Vertices);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }

        bool terrenoExiste = await db.Terreno
            .AsNoTracking()
            .AnyAsync(
                x => x.terrenoId == terrenoId && x.activo,
                cancellationToken);

        if (!terrenoExiste)
        {
            return NotFound(new
            {
                success = false,
                message = "El terreno no existe o se encuentra inactivo."
            });
        }

        try
        {
            await EjecutarAsync(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.terrenoPoligono
                    WHERE terrenoId = @terrenoId
                      AND activo = 1
                )
                BEGIN
                    THROW 50001, 'El terreno ya posee una delimitación activa.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.terrenoPoligono
                    WHERE terrenoId = @terrenoId
                )
                BEGIN
                    UPDATE dbo.terrenoPoligono
                    SET geometriaGeoJson = @geometria,
                        areaMetrosCuadrados = @areaMetros,
                        areaHectareas = @areaHectareas,
                        areaManzanasCalculada = @areaManzanas,
                        fechaActualizacionUtc = SYSUTCDATETIME(),
                        usuarioActualizacionId = @usuarioId,
                        activo = 1
                    WHERE terrenoId = @terrenoId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.terrenoPoligono
                    (
                        terrenoId,
                        geometriaGeoJson,
                        areaMetrosCuadrados,
                        areaHectareas,
                        areaManzanasCalculada,
                        fechaCreacionUtc,
                        fechaActualizacionUtc,
                        usuarioActualizacionId,
                        activo
                    )
                    VALUES
                    (
                        @terrenoId,
                        @geometria,
                        @areaMetros,
                        @areaHectareas,
                        @areaManzanas,
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME(),
                        @usuarioId,
                        1
                    );
                END;
                """,
                command =>
                {
                    AgregarParametro(command, "@terrenoId", terrenoId);
                    AgregarParametro(command, "@geometria", geometria.GeoJson);
                    AgregarParametro(
                        command,
                        "@areaMetros",
                        geometria.AreaMetrosCuadrados);
                    AgregarParametro(
                        command,
                        "@areaHectareas",
                        geometria.AreaHectareas);
                    AgregarParametro(
                        command,
                        "@areaManzanas",
                        geometria.AreaManzanas);
                    AgregarParametro(command, "@usuarioId", ObtenerUsuarioId());
                },
                cancellationToken);

            RespuestaDto? guardado = await ObtenerUnoAsync(
                terrenoId,
                usuarioPropietarioId: null,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Delimitación creada correctamente.",
                data = guardado
            });
        }
        catch (SqlException ex) when (ex.Number == 50001)
        {
            return Conflict(new
            {
                success = false,
                message =
                    "El terreno ya posee una delimitación. Utilice la opción de editar."
            });
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return TablaNoInstalada();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creando el polígono del terreno {TerrenoId}.",
                terrenoId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message = "Ocurrió un error al crear la delimitación."
                });
        }
    }

    [HttpPut("{terrenoId:int}")]
    public async Task<IActionResult> Guardar(
        int terrenoId,
        [FromBody] GuardarDto dto,
        CancellationToken cancellationToken)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Actualizar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        TerrenoPoligonoGeometria.Resultado geometria;

        try
        {
            geometria = TerrenoPoligonoGeometria.ValidarYCalcular(
                dto.Vertices);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }

        bool terrenoExiste = await db.Terreno
            .AsNoTracking()
            .AnyAsync(
                x => x.terrenoId == terrenoId && x.activo,
                cancellationToken);

        if (!terrenoExiste)
        {
            return NotFound(new
            {
                success = false,
                message = "El terreno no existe o se encuentra inactivo."
            });
        }

        try
        {
            await EjecutarAsync(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.terrenoPoligono
                    WHERE terrenoId = @terrenoId
                )
                BEGIN
                    UPDATE dbo.terrenoPoligono
                    SET geometriaGeoJson = @geometria,
                        areaMetrosCuadrados = @areaMetros,
                        areaHectareas = @areaHectareas,
                        areaManzanasCalculada = @areaManzanas,
                        fechaActualizacionUtc = SYSUTCDATETIME(),
                        usuarioActualizacionId = @usuarioId,
                        activo = 1
                    WHERE terrenoId = @terrenoId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.terrenoPoligono
                    (
                        terrenoId,
                        geometriaGeoJson,
                        areaMetrosCuadrados,
                        areaHectareas,
                        areaManzanasCalculada,
                        fechaCreacionUtc,
                        fechaActualizacionUtc,
                        usuarioActualizacionId,
                        activo
                    )
                    VALUES
                    (
                        @terrenoId,
                        @geometria,
                        @areaMetros,
                        @areaHectareas,
                        @areaManzanas,
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME(),
                        @usuarioId,
                        1
                    );
                END;
                """,
                command =>
                {
                    AgregarParametro(command, "@terrenoId", terrenoId);
                    AgregarParametro(command, "@geometria", geometria.GeoJson);
                    AgregarParametro(
                        command,
                        "@areaMetros",
                        geometria.AreaMetrosCuadrados);
                    AgregarParametro(
                        command,
                        "@areaHectareas",
                        geometria.AreaHectareas);
                    AgregarParametro(
                        command,
                        "@areaManzanas",
                        geometria.AreaManzanas);
                    AgregarParametro(command, "@usuarioId", ObtenerUsuarioId());
                },
                cancellationToken);

            RespuestaDto? guardado = await ObtenerUnoAsync(
                terrenoId,
                usuarioPropietarioId: null,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Delimitación guardada correctamente.",
                data = guardado
            });
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return TablaNoInstalada();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error guardando el polígono del terreno {TerrenoId}.",
                terrenoId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message = "Ocurrió un error al guardar la delimitación."
                });
        }
    }

    [HttpDelete("{terrenoId:int}")]
    public async Task<IActionResult> Eliminar(
        int terrenoId,
        CancellationToken cancellationToken)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        try
        {
            int afectados = await EjecutarAsync(
                """
                UPDATE dbo.terrenoPoligono
                SET activo = 0,
                    fechaActualizacionUtc = SYSUTCDATETIME(),
                    usuarioActualizacionId = @usuarioId
                WHERE terrenoId = @terrenoId
                  AND activo = 1;
                """,
                command =>
                {
                    AgregarParametro(command, "@terrenoId", terrenoId);
                    AgregarParametro(command, "@usuarioId", ObtenerUsuarioId());
                },
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = afectados > 0
                    ? "Delimitación eliminada. El punto principal se conserva."
                    : "El terreno ya se encontraba sin delimitación."
            });
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return TablaNoInstalada();
        }
    }

    private async Task<List<RespuestaDto>> ListarAsync(
        int? usuarioPropietarioId,
        CancellationToken cancellationToken)
    {
        string filtroPropietario = usuarioPropietarioId.HasValue
            ? """
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.propietarioTerreno pt
                  INNER JOIN dbo.usuarioPropietario up
                      ON up.propietarioId = pt.propietarioId
                     AND up.activo = 1
                  WHERE pt.terrenoId = t.terrenoId
                    AND pt.activo = 1
                    AND up.usuarioId = @usuarioPropietarioId
              )
              """
            : string.Empty;

        string sql =
            $"""
            SELECT
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
            WHERE p.activo = 1
            {filtroPropietario}
            ORDER BY t.codigoTerreno;
            """;

        try
        {
            return await ConsultarAsync(
                sql,
                command =>
                {
                    if (usuarioPropietarioId.HasValue)
                    {
                        AgregarParametro(
                            command,
                            "@usuarioPropietarioId",
                            usuarioPropietarioId.Value);
                    }
                },
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // Sin tabla, todos los mapas continúan funcionando solo con puntos.
            return [];
        }
    }

    private async Task<RespuestaDto?> ObtenerUnoAsync(
        int terrenoId,
        int? usuarioPropietarioId,
        CancellationToken cancellationToken)
    {
        string filtroPropietario = usuarioPropietarioId.HasValue
            ? """
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.propietarioTerreno pt
                  INNER JOIN dbo.usuarioPropietario up
                      ON up.propietarioId = pt.propietarioId
                     AND up.activo = 1
                  WHERE pt.terrenoId = t.terrenoId
                    AND pt.activo = 1
                    AND up.usuarioId = @usuarioPropietarioId
              )
              """
            : string.Empty;

        string sql =
            $"""
            SELECT TOP (1)
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
            FROM dbo.terreno t
            LEFT JOIN dbo.terrenoPoligono p
                ON p.terrenoId = t.terrenoId
               AND p.activo = 1
            WHERE t.terrenoId = @terrenoId
              AND t.activo = 1
            {filtroPropietario};
            """;

        try
        {
            List<RespuestaDto> resultados = await ConsultarAsync(
                sql,
                command =>
                {
                    AgregarParametro(command, "@terrenoId", terrenoId);

                    if (usuarioPropietarioId.HasValue)
                    {
                        AgregarParametro(
                            command,
                            "@usuarioPropietarioId",
                            usuarioPropietarioId.Value);
                    }
                },
                cancellationToken);

            return resultados.FirstOrDefault();
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            Terreno? terreno = await db.Terreno
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.terrenoId == terrenoId && x.activo,
                    cancellationToken);

            return terreno is null
                ? null
                : new RespuestaDto
                {
                    TerrenoId = terreno.terrenoId,
                    CodigoTerreno = terreno.codigoTerreno,
                    TienePoligono = false,
                    LatitudPunto = terreno.latitud,
                    LongitudPunto = terreno.longitud,
                    ExtensionRegistradaManzanas =
                        terreno.extensionManzanaTerreno,
                    PuntoDentroPoligono = true
                };
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
                    TienePoligono = vertices.Count >= 3,
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

    private async Task<int> EjecutarAsync(
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
            configurar(command);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (cerrar)
                await conexion.CloseAsync();
        }
    }

    private async Task<IActionResult?> ValidarPermisoAsync(
        TipoPermisoApi permiso,
        CancellationToken cancellationToken)
    {
        ResultadoPermisoApi resultado = await permisos.ValidarAsync(
            ObtenerUsuarioId(),
            PermisoTerreno,
            permiso,
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

    private ObjectResult TablaNoInstalada() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new
            {
                success = false,
                message =
                    "El módulo de polígonos no está instalado. Ejecute el script 20260801_TerrenoPoligono.sql."
            });
}
