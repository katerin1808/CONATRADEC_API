using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Reglas determinísticas utilizadas por el módulo web Clima y labores.
///
/// Los propietarios pueden consultar únicamente las reglas activas.
/// La creación, edición y desactivación requieren permisos administrativos.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reglas-agricolas-clima")]
public sealed class ReglasAgricolasClimaController : ControllerBase
{
    private const string Interfaz =
        "ReglasAgricolasClimaPage";

    private static readonly SemaphoreSlim Inicializacion =
        new(1, 1);

    private readonly DBContext db;
    private readonly PermisoApiService permisos;
    private readonly ILogger<ReglasAgricolasClimaController> logger;

    public ReglasAgricolasClimaController(
        DBContext db,
        PermisoApiService permisos,
        ILogger<ReglasAgricolasClimaController> logger)
    {
        this.db = db;
        this.permisos = permisos;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] bool incluirInactivas = false,
        CancellationToken cancellationToken = default)
    {
        await AsegurarEstructuraAsync(cancellationToken);

        if (incluirInactivas)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso is not null)
                return acceso;
        }

        string sql = """
            SELECT
                reglaAgricolaClimaId,
                clave,
                nombre,
                descripcion,
                icono,
                orden,
                activo,
                probabilidadLluviaMaxima,
                precipitacionMaximaMm,
                vientoMaximoKmh,
                rafagaMaximaKmh,
                temperaturaMinimaC,
                temperaturaMaximaC,
                humedadMinimaPct,
                humedadMaximaPct,
                indiceUvMaximo,
                bloquearTormentaMedia,
                duracionMinimaHoras,
                mensajeFavorable,
                mensajeNoFavorable,
                fechaRegistroUtc,
                fechaActualizacionUtc
            FROM dbo.reglaAgricolaClima
            WHERE (@incluirInactivas = 1 OR activo = 1)
            ORDER BY orden, nombre, reglaAgricolaClimaId;
            """;

        List<ReglaAgricolaClimaDto> reglas =
            await ConsultarAsync(
                sql,
                command => AgregarParametro(
                    command,
                    "@incluirInactivas",
                    incluirInactivas),
                Mapear,
                cancellationToken);

        return Ok(reglas);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(
        int id,
        CancellationToken cancellationToken = default)
    {
        await AsegurarEstructuraAsync(cancellationToken);

        const string sql = """
            SELECT
                reglaAgricolaClimaId,
                clave,
                nombre,
                descripcion,
                icono,
                orden,
                activo,
                probabilidadLluviaMaxima,
                precipitacionMaximaMm,
                vientoMaximoKmh,
                rafagaMaximaKmh,
                temperaturaMinimaC,
                temperaturaMaximaC,
                humedadMinimaPct,
                humedadMaximaPct,
                indiceUvMaximo,
                bloquearTormentaMedia,
                duracionMinimaHoras,
                mensajeFavorable,
                mensajeNoFavorable,
                fechaRegistroUtc,
                fechaActualizacionUtc
            FROM dbo.reglaAgricolaClima
            WHERE reglaAgricolaClimaId = @id;
            """;

        ReglaAgricolaClimaDto? regla =
            (await ConsultarAsync(
                sql,
                command => AgregarParametro(
                    command,
                    "@id",
                    id),
                Mapear,
                cancellationToken))
            .FirstOrDefault();

        return regla is null
            ? NotFound(new
            {
                success = false,
                message = "No se encontró la regla agrícola."
            })
            : Ok(regla);
    }

    [HttpPost]
    public async Task<IActionResult> Crear(
        [FromBody] ReglaAgricolaClimaGuardarDto dto,
        CancellationToken cancellationToken = default)
    {
        await AsegurarEstructuraAsync(cancellationToken);

        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Agregar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string? error = Validar(dto);

        if (error is not null)
            return BadRequest(new
            {
                success = false,
                message = error
            });

        string clave = NormalizarClave(dto.Clave);

        if (string.IsNullOrWhiteSpace(clave))
        {
            return BadRequest(new
            {
                success = false,
                message =
                    "La clave debe contener letras o números."
            });
        }

        if (await ExisteClaveAsync(
                clave,
                exceptoId: null,
                cancellationToken))
        {
            return Conflict(new
            {
                success = false,
                message =
                    "Ya existe una regla agrícola con esa clave."
            });
        }

        const string sql = """
            INSERT INTO dbo.reglaAgricolaClima
            (
                clave,
                nombre,
                descripcion,
                icono,
                orden,
                activo,
                probabilidadLluviaMaxima,
                precipitacionMaximaMm,
                vientoMaximoKmh,
                rafagaMaximaKmh,
                temperaturaMinimaC,
                temperaturaMaximaC,
                humedadMinimaPct,
                humedadMaximaPct,
                indiceUvMaximo,
                bloquearTormentaMedia,
                duracionMinimaHoras,
                mensajeFavorable,
                mensajeNoFavorable,
                fechaRegistroUtc,
                usuarioRegistroId
            )
            OUTPUT INSERTED.reglaAgricolaClimaId
            VALUES
            (
                @clave,
                @nombre,
                @descripcion,
                @icono,
                @orden,
                @activo,
                @probabilidadLluviaMaxima,
                @precipitacionMaximaMm,
                @vientoMaximoKmh,
                @rafagaMaximaKmh,
                @temperaturaMinimaC,
                @temperaturaMaximaC,
                @humedadMinimaPct,
                @humedadMaximaPct,
                @indiceUvMaximo,
                @bloquearTormentaMedia,
                @duracionMinimaHoras,
                @mensajeFavorable,
                @mensajeNoFavorable,
                SYSUTCDATETIME(),
                @usuarioId
            );
            """;

        int id = await EscalarEnteroAsync(
            sql,
            command => ConfigurarParametros(
                command,
                dto,
                clave,
                ObtenerUsuarioId()),
            cancellationToken);

        return Ok(await ObtenerPorIdAsync(
            id,
            cancellationToken));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(
        int id,
        [FromBody] ReglaAgricolaClimaGuardarDto dto,
        CancellationToken cancellationToken = default)
    {
        await AsegurarEstructuraAsync(cancellationToken);

        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Actualizar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string? error = Validar(dto);

        if (error is not null)
            return BadRequest(new
            {
                success = false,
                message = error
            });

        string clave = NormalizarClave(dto.Clave);

        if (string.IsNullOrWhiteSpace(clave))
        {
            return BadRequest(new
            {
                success = false,
                message =
                    "La clave debe contener letras o números."
            });
        }


        if (await ExisteClaveAsync(
                clave,
                id,
                cancellationToken))
        {
            return Conflict(new
            {
                success = false,
                message =
                    "Ya existe otra regla agrícola con esa clave."
            });
        }

        const string sql = """
            UPDATE dbo.reglaAgricolaClima
            SET
                clave = @clave,
                nombre = @nombre,
                descripcion = @descripcion,
                icono = @icono,
                orden = @orden,
                activo = @activo,
                probabilidadLluviaMaxima =
                    @probabilidadLluviaMaxima,
                precipitacionMaximaMm =
                    @precipitacionMaximaMm,
                vientoMaximoKmh = @vientoMaximoKmh,
                rafagaMaximaKmh = @rafagaMaximaKmh,
                temperaturaMinimaC = @temperaturaMinimaC,
                temperaturaMaximaC = @temperaturaMaximaC,
                humedadMinimaPct = @humedadMinimaPct,
                humedadMaximaPct = @humedadMaximaPct,
                indiceUvMaximo = @indiceUvMaximo,
                bloquearTormentaMedia =
                    @bloquearTormentaMedia,
                duracionMinimaHoras =
                    @duracionMinimaHoras,
                mensajeFavorable = @mensajeFavorable,
                mensajeNoFavorable = @mensajeNoFavorable,
                fechaActualizacionUtc = SYSUTCDATETIME(),
                usuarioActualizacionId = @usuarioId
            WHERE reglaAgricolaClimaId = @id;
            """;

        int filas = await EjecutarAsync(
            sql,
            command =>
            {
                ConfigurarParametros(
                    command,
                    dto,
                    clave,
                    ObtenerUsuarioId());

                AgregarParametro(
                    command,
                    "@id",
                    id);
            },
            cancellationToken);

        if (filas == 0)
        {
            return NotFound(new
            {
                success = false,
                message = "No se encontró la regla agrícola."
            });
        }

        return Ok(await ObtenerPorIdAsync(
            id,
            cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(
        int id,
        CancellationToken cancellationToken = default)
    {
        await AsegurarEstructuraAsync(cancellationToken);

        IActionResult? acceso = await ValidarPermisoAsync(
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        const string sql = """
            UPDATE dbo.reglaAgricolaClima
            SET
                activo = 0,
                fechaActualizacionUtc = SYSUTCDATETIME(),
                usuarioActualizacionId = @usuarioId
            WHERE reglaAgricolaClimaId = @id
              AND activo = 1;
            """;

        int filas = await EjecutarAsync(
            sql,
            command =>
            {
                AgregarParametro(
                    command,
                    "@id",
                    id);

                AgregarParametro(
                    command,
                    "@usuarioId",
                    ObtenerUsuarioId());
            },
            cancellationToken);

        return filas == 0
            ? NotFound(new
            {
                success = false,
                message =
                    "No se encontró una regla activa para desactivar."
            })
            : Ok(new
            {
                success = true,
                message = "Regla desactivada correctamente."
            });
    }

    private async Task AsegurarEstructuraAsync(
        CancellationToken cancellationToken)
    {
        await Inicializacion.WaitAsync(cancellationToken);

        try
        {
            const string sql = """
                IF OBJECT_ID(
                        N'dbo.reglaAgricolaClima',
                        N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.reglaAgricolaClima
                    (
                        reglaAgricolaClimaId
                            INT IDENTITY(1,1) NOT NULL
                            CONSTRAINT PK_reglaAgricolaClima
                            PRIMARY KEY,

                        clave NVARCHAR(60) NOT NULL,
                        nombre NVARCHAR(120) NOT NULL,
                        descripcion NVARCHAR(500) NOT NULL,
                        icono NVARCHAR(80) NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_icono
                            DEFAULT(
                                N'fa-solid fa-seedling'),

                        orden INT NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_orden
                            DEFAULT(0),

                        activo BIT NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_activo
                            DEFAULT(1),

                        probabilidadLluviaMaxima INT NULL,

                        precipitacionMaximaMm
                            DECIMAL(10,2) NULL,

                        vientoMaximoKmh
                            DECIMAL(10,2) NULL,

                        rafagaMaximaKmh
                            DECIMAL(10,2) NULL,

                        temperaturaMinimaC
                            DECIMAL(10,2) NULL,

                        temperaturaMaximaC
                            DECIMAL(10,2) NULL,

                        humedadMinimaPct
                            DECIMAL(10,2) NULL,

                        humedadMaximaPct
                            DECIMAL(10,2) NULL,

                        indiceUvMaximo
                            DECIMAL(10,2) NULL,

                        bloquearTormentaMedia BIT NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_tormenta
                            DEFAULT(1),

                        duracionMinimaHoras INT NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_duracion
                            DEFAULT(3),

                        mensajeFavorable
                            NVARCHAR(300) NOT NULL,

                        mensajeNoFavorable
                            NVARCHAR(300) NOT NULL,

                        fechaRegistroUtc DATETIME2(0) NOT NULL
                            CONSTRAINT
                                DF_reglaAgricolaClima_registro
                            DEFAULT(SYSUTCDATETIME()),

                        fechaActualizacionUtc DATETIME2(0) NULL,

                        usuarioRegistroId INT NULL,

                        usuarioActualizacionId INT NULL
                    );

                    CREATE UNIQUE INDEX
                        UX_reglaAgricolaClima_clave
                        ON dbo.reglaAgricolaClima(clave);
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.reglaAgricolaClima
                )
                BEGIN
                    INSERT INTO dbo.reglaAgricolaClima
                    (
                        clave,
                        nombre,
                        descripcion,
                        icono,
                        orden,
                        activo,
                        probabilidadLluviaMaxima,
                        precipitacionMaximaMm,
                        vientoMaximoKmh,
                        rafagaMaximaKmh,
                        temperaturaMinimaC,
                        temperaturaMaximaC,
                        humedadMinimaPct,
                        humedadMaximaPct,
                        indiceUvMaximo,
                        bloquearTormentaMedia,
                        duracionMinimaHoras,
                        mensajeFavorable,
                        mensajeNoFavorable
                    )
                    VALUES
                    (
                        N'APLICACION_FOLIAR',
                        N'Aplicación foliar',
                        N'Aplicación de nutrientes o productos sobre el follaje.',
                        N'fa-solid fa-spray-can-sparkles',
                        10,
                        1,
                        30,
                        0.50,
                        15,
                        25,
                        18,
                        30,
                        45,
                        85,
                        7,
                        1,
                        3,
                        N'Ventana favorable para una aplicación foliar.',
                        N'Evite aplicar por lluvia, viento, calor o tormenta.'
                    ),
                    (
                        N'FERTILIZACION_SUELO',
                        N'Fertilización al suelo',
                        N'Aplicación de fertilizantes granulados u orgánicos.',
                        N'fa-solid fa-seedling',
                        20,
                        1,
                        45,
                        2.00,
                        25,
                        35,
                        16,
                        32,
                        NULL,
                        92,
                        9,
                        1,
                        3,
                        N'Condiciones adecuadas para fertilizar el suelo.',
                        N'Reprograme para reducir pérdidas por lluvia o escorrentía.'
                    ),
                    (
                        N'PODA_Y_MANEJO',
                        N'Poda y manejo de sombra',
                        N'Labores manuales de poda, regulación y mantenimiento.',
                        N'fa-solid fa-scissors',
                        30,
                        1,
                        55,
                        3.00,
                        25,
                        40,
                        15,
                        32,
                        NULL,
                        95,
                        10,
                        0,
                        3,
                        N'Periodo apropiado para labores de poda y manejo.',
                        N'Extreme precauciones o reprograme por las condiciones previstas.'
                    ),
                    (
                        N'COSECHA',
                        N'Cosecha',
                        N'Recolección y traslado de café dentro de la finca.',
                        N'fa-solid fa-basket-shopping',
                        40,
                        1,
                        35,
                        1.00,
                        30,
                        45,
                        15,
                        34,
                        NULL,
                        95,
                        10,
                        1,
                        3,
                        N'Ventana favorable para cosecha y traslado.',
                        N'La lluvia o tormenta puede afectar la cosecha y el acceso.'
                    ),
                    (
                        N'SECADO_CAFE',
                        N'Secado de café',
                        N'Secado al sol y manejo de café en patios o camas.',
                        N'fa-solid fa-sun',
                        50,
                        1,
                        15,
                        0.20,
                        25,
                        40,
                        18,
                        36,
                        NULL,
                        70,
                        10,
                        1,
                        3,
                        N'Condiciones favorables para secado al aire libre.',
                        N'Proteja el café y evite exponerlo a humedad o lluvia.'
                    ),
                    (
                        N'CONTROL_MALEZAS',
                        N'Control de malezas',
                        N'Aplicación dirigida o labor manual para controlar malezas.',
                        N'fa-solid fa-leaf',
                        60,
                        1,
                        35,
                        1.00,
                        18,
                        30,
                        16,
                        30,
                        40,
                        85,
                        7,
                        1,
                        3,
                        N'Ventana favorable para el control de malezas.',
                        N'Evite aplicar con viento, lluvia, calor o humedad excesiva.'
                    );
                END;

                DECLARE @interfazId INT;

                SELECT @interfazId = interfazId
                FROM dbo.interfaz
                WHERE nombreInterfaz =
                    N'ReglasAgricolasClimaPage';

                IF @interfazId IS NULL
                BEGIN
                    INSERT INTO dbo.interfaz
                    (
                        nombreInterfaz,
                        nombreAmigableInterfaz,
                        descripcionInterfaz,
                        activo
                    )
                    VALUES
                    (
                        N'ReglasAgricolasClimaPage',
                        N'Reglas agrícolas del clima',
                        N'Permite configurar umbrales para ventanas de labores.',
                        1
                    );

                    SET @interfazId =
                        CONVERT(INT, SCOPE_IDENTITY());
                END
                ELSE
                BEGIN
                    UPDATE dbo.interfaz
                    SET
                        nombreAmigableInterfaz =
                            N'Reglas agrícolas del clima',
                        descripcionInterfaz =
                            N'Permite configurar umbrales para ventanas de labores.',
                        activo = 1
                    WHERE interfazId = @interfazId;
                END;

                MERGE dbo.RolInterfaz AS destino
                USING
                (
                    SELECT
                        rol.rolId,
                        @interfazId AS interfazId
                    FROM dbo.rol rol
                    WHERE rol.activo = 1
                      AND UPPER(LTRIM(RTRIM(
                            rol.nombreRol))) =
                            N'ADMINISTRADOR'
                ) AS origen
                ON destino.rolId = origen.rolId
                   AND destino.interfazId =
                       origen.interfazId
                WHEN MATCHED THEN
                    UPDATE SET
                        leer = 1,
                        agregar = 1,
                        actualizar = 1,
                        eliminar = 1
                WHEN NOT MATCHED THEN
                    INSERT
                    (
                        rolId,
                        interfazId,
                        leer,
                        agregar,
                        actualizar,
                        eliminar
                    )
                    VALUES
                    (
                        origen.rolId,
                        origen.interfazId,
                        1,
                        1,
                        1,
                        1
                    );
                """;

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
        finally
        {
            Inicializacion.Release();
        }
    }

    private async Task<IActionResult?> ValidarPermisoAsync(
        TipoPermisoApi permiso,
        CancellationToken cancellationToken)
    {
        ResultadoPermisoApi resultado =
            await permisos.ValidarAsync(
                ObtenerUsuarioId(),
                Interfaz,
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

    private static string? Validar(
        ReglaAgricolaClimaGuardarDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Clave))
            return "La clave de la regla es obligatoria.";

        if (string.IsNullOrWhiteSpace(dto.Nombre))
            return "El nombre de la regla es obligatorio.";

        if (string.IsNullOrWhiteSpace(dto.Descripcion))
            return "La descripción es obligatoria.";

        if (dto.TemperaturaMinimaC.HasValue &&
            dto.TemperaturaMaximaC.HasValue &&
            dto.TemperaturaMinimaC >
            dto.TemperaturaMaximaC)
        {
            return
                "La temperatura mínima no puede superar la máxima.";
        }

        if (dto.HumedadMinimaPct.HasValue &&
            dto.HumedadMaximaPct.HasValue &&
            dto.HumedadMinimaPct >
            dto.HumedadMaximaPct)
        {
            return
                "La humedad mínima no puede superar la máxima.";
        }

        return null;
    }

    private async Task<bool> ExisteClaveAsync(
        string clave,
        int? exceptoId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(1)
            FROM dbo.reglaAgricolaClima
            WHERE clave = @clave
              AND (@exceptoId IS NULL
                   OR reglaAgricolaClimaId <> @exceptoId);
            """;

        long total = await EscalarLongAsync(
            sql,
            command =>
            {
                AgregarParametro(
                    command,
                    "@clave",
                    clave);

                AgregarParametro(
                    command,
                    "@exceptoId",
                    exceptoId);
            },
            cancellationToken);

        return total > 0;
    }

    private async Task<ReglaAgricolaClimaDto?> ObtenerPorIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                reglaAgricolaClimaId,
                clave,
                nombre,
                descripcion,
                icono,
                orden,
                activo,
                probabilidadLluviaMaxima,
                precipitacionMaximaMm,
                vientoMaximoKmh,
                rafagaMaximaKmh,
                temperaturaMinimaC,
                temperaturaMaximaC,
                humedadMinimaPct,
                humedadMaximaPct,
                indiceUvMaximo,
                bloquearTormentaMedia,
                duracionMinimaHoras,
                mensajeFavorable,
                mensajeNoFavorable,
                fechaRegistroUtc,
                fechaActualizacionUtc
            FROM dbo.reglaAgricolaClima
            WHERE reglaAgricolaClimaId = @id;
            """;

        return (await ConsultarAsync(
            sql,
            command => AgregarParametro(
                command,
                "@id",
                id),
            Mapear,
            cancellationToken))
        .FirstOrDefault();
    }

    private static ReglaAgricolaClimaDto Mapear(
        DbDataReader reader) =>
        new()
        {
            ReglaAgricolaClimaId = reader.GetInt32(0),
            Clave = Texto(reader, 1),
            Nombre = Texto(reader, 2),
            Descripcion = Texto(reader, 3),
            Icono = Texto(reader, 4),
            Orden = reader.GetInt32(5),
            Activo = reader.GetBoolean(6),
            ProbabilidadLluviaMaxima =
                EnteroNullable(reader, 7),
            PrecipitacionMaximaMm =
                DecimalNullable(reader, 8),
            VientoMaximoKmh =
                DecimalNullable(reader, 9),
            RafagaMaximaKmh =
                DecimalNullable(reader, 10),
            TemperaturaMinimaC =
                DecimalNullable(reader, 11),
            TemperaturaMaximaC =
                DecimalNullable(reader, 12),
            HumedadMinimaPct =
                DecimalNullable(reader, 13),
            HumedadMaximaPct =
                DecimalNullable(reader, 14),
            IndiceUvMaximo =
                DecimalNullable(reader, 15),
            BloquearTormentaMedia =
                reader.GetBoolean(16),
            DuracionMinimaHoras =
                reader.GetInt32(17),
            MensajeFavorable =
                Texto(reader, 18),
            MensajeNoFavorable =
                Texto(reader, 19),
            FechaRegistroUtc =
                reader.GetDateTime(20),
            FechaActualizacionUtc =
                FechaNullable(reader, 21)
        };

    private static void ConfigurarParametros(
        DbCommand command,
        ReglaAgricolaClimaGuardarDto dto,
        string clave,
        int? usuarioId)
    {
        AgregarParametro(command, "@clave", clave);
        AgregarParametro(
            command,
            "@nombre",
            dto.Nombre.Trim());
        AgregarParametro(
            command,
            "@descripcion",
            dto.Descripcion.Trim());
        AgregarParametro(
            command,
            "@icono",
            string.IsNullOrWhiteSpace(dto.Icono)
                ? "fa-solid fa-seedling"
                : dto.Icono.Trim());
        AgregarParametro(command, "@orden", dto.Orden);
        AgregarParametro(command, "@activo", dto.Activo);
        AgregarParametro(
            command,
            "@probabilidadLluviaMaxima",
            dto.ProbabilidadLluviaMaxima);
        AgregarParametro(
            command,
            "@precipitacionMaximaMm",
            dto.PrecipitacionMaximaMm);
        AgregarParametro(
            command,
            "@vientoMaximoKmh",
            dto.VientoMaximoKmh);
        AgregarParametro(
            command,
            "@rafagaMaximaKmh",
            dto.RafagaMaximaKmh);
        AgregarParametro(
            command,
            "@temperaturaMinimaC",
            dto.TemperaturaMinimaC);
        AgregarParametro(
            command,
            "@temperaturaMaximaC",
            dto.TemperaturaMaximaC);
        AgregarParametro(
            command,
            "@humedadMinimaPct",
            dto.HumedadMinimaPct);
        AgregarParametro(
            command,
            "@humedadMaximaPct",
            dto.HumedadMaximaPct);
        AgregarParametro(
            command,
            "@indiceUvMaximo",
            dto.IndiceUvMaximo);
        AgregarParametro(
            command,
            "@bloquearTormentaMedia",
            dto.BloquearTormentaMedia);
        AgregarParametro(
            command,
            "@duracionMinimaHoras",
            dto.DuracionMinimaHoras);
        AgregarParametro(
            command,
            "@mensajeFavorable",
            dto.MensajeFavorable.Trim());
        AgregarParametro(
            command,
            "@mensajeNoFavorable",
            dto.MensajeNoFavorable.Trim());
        AgregarParametro(
            command,
            "@usuarioId",
            usuarioId);
    }

    private int? ObtenerUsuarioId()
    {
        string? valor =
            User.FindFirst("uid")?.Value ??
            User.FindFirst(
                ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst("sub")?.Value;

        return int.TryParse(
            valor,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int id) &&
            id > 0
                ? id
                : null;
    }

    private static string NormalizarClave(
        string valor)
    {
        var builder = new StringBuilder();

        foreach (char caracter in valor
                     .Trim()
                     .ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(caracter))
                builder.Append(caracter);
            else if (builder.Length > 0 &&
                     builder[^1] != '_')
                builder.Append('_');
        }

        return builder
            .ToString()
            .Trim('_');
    }

    private async Task<List<T>> ConsultarAsync<T>(
        string sql,
        Action<DbCommand> configurar,
        Func<DbDataReader, T> mapear,
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            db.Database.GetDbConnection();

        bool cerrar =
            connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText = sql;
            AsignarTransaccion(command);
            configurar(command);

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            var resultados = new List<T>();

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                resultados.Add(mapear(reader));
            }

            return resultados;
        }
        finally
        {
            if (cerrar &&
                db.Database.CurrentTransaction is null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> EjecutarAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            db.Database.GetDbConnection();

        bool cerrar =
            connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText = sql;
            AsignarTransaccion(command);
            configurar(command);

            return await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
        finally
        {
            if (cerrar &&
                db.Database.CurrentTransaction is null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> EscalarEnteroAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        object? valor = await EscalarAsync(
            sql,
            configurar,
            cancellationToken);

        return valor is null or DBNull
            ? 0
            : Convert.ToInt32(
                valor,
                CultureInfo.InvariantCulture);
    }

    private async Task<long> EscalarLongAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        object? valor = await EscalarAsync(
            sql,
            configurar,
            cancellationToken);

        return valor is null or DBNull
            ? 0
            : Convert.ToInt64(
                valor,
                CultureInfo.InvariantCulture);
    }

    private async Task<object?> EscalarAsync(
        string sql,
        Action<DbCommand> configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            db.Database.GetDbConnection();

        bool cerrar =
            connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText = sql;
            AsignarTransaccion(command);
            configurar(command);

            return await command.ExecuteScalarAsync(
                cancellationToken);
        }
        finally
        {
            if (cerrar &&
                db.Database.CurrentTransaction is null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private void AsignarTransaccion(
        DbCommand command)
    {
        IDbContextTransaction? transaccion =
            db.Database.CurrentTransaction;

        if (transaccion is not null)
        {
            command.Transaction =
                transaccion.GetDbTransaction();
        }
    }

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object? valor)
    {
        DbParameter parametro =
            command.CreateParameter();

        parametro.ParameterName = nombre;
        parametro.Value = valor ?? DBNull.Value;
        command.Parameters.Add(parametro);
    }

    private static string Texto(
        DbDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? string.Empty
            : reader.GetString(ordinal);

    private static int? EnteroNullable(
        DbDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);

    private static decimal? DecimalNullable(
        DbDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetDecimal(ordinal);

    private static DateTime? FechaNullable(
        DbDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetDateTime(ordinal);
}
