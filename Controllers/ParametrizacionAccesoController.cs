using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers;

[ApiController]
[Authorize]
[Route("api/parametrizacion-acceso")]
public sealed class ParametrizacionAccesoController : ControllerBase
{
    private readonly DBContext db;
    private readonly PermisoApiService permisos;

    public ParametrizacionAccesoController(
        DBContext db,
        PermisoApiService permisos)
    {
        this.db = db;
        this.permisos = permisos;
    }

    [HttpGet("propietarios")]
    public async Task<IActionResult> ListarPropietarios(
        [FromQuery] string? buscar,
        [FromQuery] bool incluirInactivos = false,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso =
            await ValidarLecturaPropietariosOVinculacionAsync(
                cancellationToken);

        if (acceso is not null)
            return acceso;

        string texto = (buscar ?? string.Empty).Trim();

        const string sql = """
            SELECT
                p.propietarioId,
                p.identificacion,
                p.nombreCompleto,
                p.telefono,
                p.correo,
                p.direccion,
                p.activo,
                p.fechaRegistroUtc,
                COUNT(DISTINCT CASE WHEN pt.activo = 1
                    THEN pt.terrenoId END) AS totalTerrenos,
                MAX(CASE WHEN up.activo = 1
                    THEN up.usuarioId END) AS usuarioPortalId,
                MAX(CASE WHEN up.activo = 1
                    THEN u.nombreUsuario END) AS usuarioPortal
            FROM dbo.propietario p
            LEFT JOIN dbo.propietarioTerreno pt
                ON pt.propietarioId = p.propietarioId
            LEFT JOIN dbo.usuarioPropietario up
                ON up.propietarioId = p.propietarioId
               AND up.activo = 1
            LEFT JOIN dbo.usuario u
                ON u.UsuarioId = up.usuarioId
            WHERE (@incluirInactivos = 1 OR p.activo = 1)
              AND (
                    @buscar = N''
                    OR p.identificacion LIKE N'%' + @buscar + N'%'
                    OR p.nombreCompleto LIKE N'%' + @buscar + N'%'
                    OR ISNULL(p.correo, N'') LIKE N'%' + @buscar + N'%'
                  )
            GROUP BY
                p.propietarioId,
                p.identificacion,
                p.nombreCompleto,
                p.telefono,
                p.correo,
                p.direccion,
                p.activo,
                p.fechaRegistroUtc
            ORDER BY p.nombreCompleto, p.identificacion;
            """;

        var items = await ConsultarAsync(
            sql,
            command =>
            {
                AgregarParametro(command, "@buscar", texto);
                AgregarParametro(
                    command,
                    "@incluirInactivos",
                    incluirInactivos);
            },
            reader => new
            {
                propietarioId = reader.GetInt32(0),
                identificacion = Texto(reader, 1),
                nombreCompleto = Texto(reader, 2),
                telefono = TextoNullable(reader, 3),
                correo = TextoNullable(reader, 4),
                direccion = TextoNullable(reader, 5),
                activo = reader.GetBoolean(6),
                fechaRegistroUtc = reader.GetDateTime(7),
                totalTerrenos = reader.GetInt32(8),
                usuarioPortalId = EnteroNullable(reader, 9),
                usuarioPortal = TextoNullable(reader, 10)
            },
            cancellationToken);

        return Ok(items);
    }

    [HttpPost("propietarios")]
    public async Task<IActionResult> CrearPropietario(
        [FromBody] PropietarioGuardarDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Agregar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string identificacion = dto.Identificacion.Trim();
        string normalizada = NormalizarIdentificacion(identificacion);
        string nombre = dto.NombreCompleto.Trim();

        if (string.IsNullOrWhiteSpace(normalizada))
            return BadRequest(Respuesta("La identificación no es válida."));

        int? existente = await EscalarEnteroAsync(
            """
            SELECT propietarioId
            FROM dbo.propietario
            WHERE identificacionNormalizada = @normalizada;
            """,
            command => AgregarParametro(
                command,
                "@normalizada",
                normalizada),
            cancellationToken);

        if (existente.HasValue)
        {
            return Conflict(Respuesta(
                "Ya existe un propietario con esa identificación."));
        }

        int usuarioId = ObtenerUsuarioId();

        int? propietarioId = await EscalarEnteroAsync(
            """
            INSERT INTO dbo.propietario
            (
                identificacion,
                identificacionNormalizada,
                nombreCompleto,
                telefono,
                correo,
                direccion,
                activo,
                fechaRegistroUtc,
                usuarioRegistroId
            )
            VALUES
            (
                @identificacion,
                @normalizada,
                @nombre,
                @telefono,
                @correo,
                @direccion,
                @activo,
                SYSUTCDATETIME(),
                @usuarioId
            );

            SELECT CONVERT(INT, SCOPE_IDENTITY());
            """,
            command =>
            {
                AgregarParametro(command, "@identificacion", identificacion);
                AgregarParametro(command, "@normalizada", normalizada);
                AgregarParametro(command, "@nombre", nombre);
                AgregarParametro(command, "@telefono", Limpiar(dto.Telefono));
                AgregarParametro(command, "@correo", Limpiar(dto.Correo));
                AgregarParametro(command, "@direccion", Limpiar(dto.Direccion));
                AgregarParametro(command, "@activo", dto.Activo);
                AgregarParametro(command, "@usuarioId", usuarioId);
            },
            cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Propietario creado correctamente.",
            data = new { propietarioId }
        });
    }

    [HttpPut("propietarios/{propietarioId:int}")]
    public async Task<IActionResult> ActualizarPropietario(
        int propietarioId,
        [FromBody] PropietarioGuardarDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Actualizar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string identificacion = dto.Identificacion.Trim();
        string normalizada = NormalizarIdentificacion(identificacion);

        int? duplicado = await EscalarEnteroAsync(
            """
            SELECT propietarioId
            FROM dbo.propietario
            WHERE identificacionNormalizada = @normalizada
              AND propietarioId <> @propietarioId;
            """,
            command =>
            {
                AgregarParametro(command, "@normalizada", normalizada);
                AgregarParametro(command, "@propietarioId", propietarioId);
            },
            cancellationToken);

        if (duplicado.HasValue)
        {
            return Conflict(Respuesta(
                "Otra persona ya utiliza esa identificación."));
        }

        int filas = await EjecutarAsync(
            """
            UPDATE dbo.propietario
            SET identificacion = @identificacion,
                identificacionNormalizada = @normalizada,
                nombreCompleto = @nombre,
                telefono = @telefono,
                correo = @correo,
                direccion = @direccion,
                activo = @activo,
                fechaActualizacionUtc = SYSUTCDATETIME(),
                usuarioActualizacionId = @usuarioId
            WHERE propietarioId = @propietarioId;
            """,
            command =>
            {
                AgregarParametro(command, "@identificacion", identificacion);
                AgregarParametro(command, "@normalizada", normalizada);
                AgregarParametro(command, "@nombre", dto.NombreCompleto.Trim());
                AgregarParametro(command, "@telefono", Limpiar(dto.Telefono));
                AgregarParametro(command, "@correo", Limpiar(dto.Correo));
                AgregarParametro(command, "@direccion", Limpiar(dto.Direccion));
                AgregarParametro(command, "@activo", dto.Activo);
                AgregarParametro(command, "@usuarioId", ObtenerUsuarioId());
                AgregarParametro(command, "@propietarioId", propietarioId);
            },
            cancellationToken);

        return filas == 0
            ? NotFound(Respuesta("No se encontró el propietario."))
            : Ok(Respuesta("Propietario actualizado correctamente.", true));
    }

    [HttpGet("propietarios/{propietarioId:int}")]
    public async Task<IActionResult> ObtenerPropietario(
        int propietarioId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        var propietario = await ConsultarAsync(
            """
            SELECT
                p.propietarioId,
                p.identificacion,
                p.nombreCompleto,
                p.telefono,
                p.correo,
                p.direccion,
                p.activo,
                up.usuarioId,
                u.nombreUsuario,
                u.nombreCompletoUsuario
            FROM dbo.propietario p
            LEFT JOIN dbo.usuarioPropietario up
                ON up.propietarioId = p.propietarioId
               AND up.activo = 1
            LEFT JOIN dbo.usuario u
                ON u.UsuarioId = up.usuarioId
            WHERE p.propietarioId = @propietarioId;
            """,
            command => AgregarParametro(
                command,
                "@propietarioId",
                propietarioId),
            reader => new
            {
                propietarioId = reader.GetInt32(0),
                identificacion = Texto(reader, 1),
                nombreCompleto = Texto(reader, 2),
                telefono = TextoNullable(reader, 3),
                correo = TextoNullable(reader, 4),
                direccion = TextoNullable(reader, 5),
                activo = reader.GetBoolean(6),
                usuarioPortalId = EnteroNullable(reader, 7),
                usuarioPortal = TextoNullable(reader, 8),
                nombreUsuarioPortal = TextoNullable(reader, 9)
            },
            cancellationToken);

        if (propietario.Count == 0)
            return NotFound(Respuesta("No se encontró el propietario."));

        var terrenos = await ConsultarAsync(
            """
            SELECT
                t.terrenoId,
                t.codigoTerreno,
                t.direccionTerreno,
                t.extensionManzanaTerreno,
                t.cantidadQuintalesOro,
                t.activo,
                pt.fechaAsignacionUtc
            FROM dbo.propietarioTerreno pt
            INNER JOIN dbo.terreno t
                ON t.terrenoId = pt.terrenoId
            WHERE pt.propietarioId = @propietarioId
              AND pt.activo = 1
            ORDER BY t.codigoTerreno;
            """,
            command => AgregarParametro(
                command,
                "@propietarioId",
                propietarioId),
            reader => new
            {
                terrenoId = reader.GetInt32(0),
                codigoTerreno = Texto(reader, 1),
                direccionTerreno = Texto(reader, 2),
                extensionManzanas = reader.GetDecimal(3),
                quintalesOro = reader.GetDecimal(4),
                activo = reader.GetBoolean(5),
                fechaAsignacionUtc = reader.GetDateTime(6)
            },
            cancellationToken);

        return Ok(new
        {
            propietario = propietario[0],
            terrenos
        });
    }

    [HttpPost("propietarios/{propietarioId:int}/terrenos")]
    public async Task<IActionResult> VincularTerreno(
        int propietarioId,
        [FromBody] VincularTerrenoPropietarioDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Actualizar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        if (!await ExistePropietarioAsync(propietarioId, cancellationToken))
            return NotFound(Respuesta("No se encontró el propietario."));

        if (!await db.Terreno.AsNoTracking().AnyAsync(
                x => x.terrenoId == dto.TerrenoId,
                cancellationToken))
        {
            return NotFound(Respuesta("No se encontró el terreno."));
        }

        int usuarioId = ObtenerUsuarioId();
        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await EjecutarAsync(
            """
            UPDATE dbo.propietarioTerreno
            SET activo = 0,
                fechaDesasignacionUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioId
            WHERE terrenoId = @terrenoId
              AND activo = 1
              AND propietarioId <> @propietarioId;
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", usuarioId);
                AgregarParametro(command, "@terrenoId", dto.TerrenoId);
                AgregarParametro(command, "@propietarioId", propietarioId);
            },
            cancellationToken);

        int? existente = await EscalarEnteroAsync(
            """
            SELECT propietarioTerrenoId
            FROM dbo.propietarioTerreno
            WHERE propietarioId = @propietarioId
              AND terrenoId = @terrenoId
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(command, "@propietarioId", propietarioId);
                AgregarParametro(command, "@terrenoId", dto.TerrenoId);
            },
            cancellationToken);

        if (!existente.HasValue)
        {
            await EjecutarAsync(
                """
                INSERT INTO dbo.propietarioTerreno
                (
                    propietarioId,
                    terrenoId,
                    activo,
                    fechaAsignacionUtc,
                    asignadoPorUsuarioId
                )
                VALUES
                (
                    @propietarioId,
                    @terrenoId,
                    1,
                    SYSUTCDATETIME(),
                    @usuarioId
                );
                """,
                command =>
                {
                    AgregarParametro(command, "@propietarioId", propietarioId);
                    AgregarParametro(command, "@terrenoId", dto.TerrenoId);
                    AgregarParametro(command, "@usuarioId", usuarioId);
                },
                cancellationToken);
        }

        await transaccion.CommitAsync(cancellationToken);

        return Ok(Respuesta(
            "Terreno vinculado al propietario correctamente.",
            true));
    }

    [HttpDelete("propietarios/{propietarioId:int}/terrenos/{terrenoId:int}")]
    public async Task<IActionResult> DesvincularTerreno(
        int propietarioId,
        int terrenoId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        int filas = await EjecutarAsync(
            """
            UPDATE dbo.propietarioTerreno
            SET activo = 0,
                fechaDesasignacionUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioId
            WHERE propietarioId = @propietarioId
              AND terrenoId = @terrenoId
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", ObtenerUsuarioId());
                AgregarParametro(command, "@propietarioId", propietarioId);
                AgregarParametro(command, "@terrenoId", terrenoId);
            },
            cancellationToken);

        return filas == 0
            ? NotFound(Respuesta("No existe una vinculación activa."))
            : Ok(Respuesta("Terreno desvinculado correctamente.", true));
    }

    [HttpPost("usuario-propietario")]
    public async Task<IActionResult> VincularUsuarioPropietario(
        [FromBody] VincularUsuarioPropietarioDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.UsuarioPropietario,
            TipoPermisoApi.AgregarOActualizar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        if (!await db.Usuarios.AsNoTracking().AnyAsync(
                x => x.UsuarioId == dto.UsuarioId && x.activo,
                cancellationToken))
        {
            return NotFound(Respuesta("No se encontró un usuario activo."));
        }

        if (!await ExistePropietarioAsync(dto.PropietarioId, cancellationToken))
            return NotFound(Respuesta("No se encontró el propietario."));

        int usuarioSesionId = ObtenerUsuarioId();

        List<int> usuariosAfectados = await ConsultarAsync(
            """
            SELECT DISTINCT usuarioId
            FROM dbo.usuarioPropietario
            WHERE activo = 1
              AND (usuarioId = @usuarioId
                   OR propietarioId = @propietarioId);
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@propietarioId", dto.PropietarioId);
            },
            reader => reader.GetInt32(0),
            cancellationToken);

        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await EjecutarAsync(
            """
            UPDATE dbo.usuarioPropietario
            SET activo = 0,
                fechaDesasignacionUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioSesionId
            WHERE activo = 1
              AND (usuarioId = @usuarioId
                   OR propietarioId = @propietarioId);
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioSesionId", usuarioSesionId);
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@propietarioId", dto.PropietarioId);
            },
            cancellationToken);

        await EjecutarAsync(
            """
            INSERT INTO dbo.usuarioPropietario
            (
                usuarioId,
                propietarioId,
                activo,
                fechaAsignacionUtc,
                asignadoPorUsuarioId
            )
            VALUES
            (
                @usuarioId,
                @propietarioId,
                1,
                SYSUTCDATETIME(),
                @usuarioSesionId
            );
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@propietarioId", dto.PropietarioId);
                AgregarParametro(command, "@usuarioSesionId", usuarioSesionId);
            },
            cancellationToken);

        usuariosAfectados.Add(dto.UsuarioId);
        foreach (int usuarioAfectado in usuariosAfectados.Distinct())
            await IncrementarVersionSesionAsync(
                usuarioAfectado,
                cancellationToken);

        await transaccion.CommitAsync(cancellationToken);

        return Ok(Respuesta(
            "Usuario vinculado al propietario correctamente.",
            true));
    }

    [HttpDelete("usuario-propietario/{usuarioId:int}")]
    public async Task<IActionResult> DesvincularUsuarioPropietario(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.UsuarioPropietario,
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        int filas = await EjecutarAsync(
            """
            UPDATE dbo.usuarioPropietario
            SET activo = 0,
                fechaDesasignacionUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioSesionId
            WHERE usuarioId = @usuarioId
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioSesionId", ObtenerUsuarioId());
                AgregarParametro(command, "@usuarioId", usuarioId);
            },
            cancellationToken);

        if (filas > 0)
            await IncrementarVersionSesionAsync(usuarioId, cancellationToken);

        return filas == 0
            ? NotFound(Respuesta("El usuario no tiene propietario vinculado."))
            : Ok(Respuesta("Usuario desvinculado correctamente.", true));
    }

    [HttpGet("asignaciones")]
    public async Task<IActionResult> ListarAsignaciones(
        [FromQuery] int? usuarioId,
        [FromQuery] int? terrenoId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.AsignacionTerreno,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        var items = await ConsultarAsync(
            """
            SELECT
                a.usuarioTerrenoAsignacionId,
                a.usuarioId,
                u.nombreUsuario,
                u.nombreCompletoUsuario,
                a.terrenoId,
                t.codigoTerreno,
                ISNULL(p.nombreCompleto, N''),
                a.tipoAsignacion,
                a.esResponsablePrincipal,
                a.observacion,
                a.fechaInicioUtc
            FROM dbo.usuarioTerrenoAsignacion a
            INNER JOIN dbo.usuario u
                ON u.UsuarioId = a.usuarioId
            INNER JOIN dbo.terreno t
                ON t.terrenoId = a.terrenoId
            LEFT JOIN dbo.propietarioTerreno pt
                ON pt.terrenoId = t.terrenoId
               AND pt.activo = 1
            LEFT JOIN dbo.propietario p
                ON p.propietarioId = pt.propietarioId
               AND p.activo = 1
            WHERE a.activo = 1
              AND (@usuarioId IS NULL OR a.usuarioId = @usuarioId)
              AND (@terrenoId IS NULL OR a.terrenoId = @terrenoId)
            ORDER BY
                u.nombreCompletoUsuario,
                t.codigoTerreno,
                a.tipoAsignacion;
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", usuarioId);
                AgregarParametro(command, "@terrenoId", terrenoId);
            },
            reader => new
            {
                usuarioTerrenoAsignacionId = reader.GetInt32(0),
                usuarioId = reader.GetInt32(1),
                nombreUsuario = Texto(reader, 2),
                nombreCompletoUsuario = Texto(reader, 3),
                terrenoId = reader.GetInt32(4),
                codigoTerreno = Texto(reader, 5),
                propietario = Texto(reader, 6),
                tipoAsignacion = Texto(reader, 7),
                esResponsablePrincipal = reader.GetBoolean(8),
                observacion = TextoNullable(reader, 9),
                fechaInicioUtc = reader.GetDateTime(10)
            },
            cancellationToken);

        return Ok(items);
    }

    [HttpPost("asignaciones")]
    public async Task<IActionResult> AsignarTerreno(
        [FromBody] AsignarUsuarioTerrenoDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.AsignacionTerreno,
            TipoPermisoApi.Agregar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string tipo = NormalizarTipo(dto.TipoAsignacion);

        if (!await db.Usuarios.AsNoTracking().AnyAsync(
                x => x.UsuarioId == dto.UsuarioId && x.activo,
                cancellationToken))
        {
            return NotFound(Respuesta("No se encontró un usuario activo."));
        }

        if (!await db.Terreno.AsNoTracking().AnyAsync(
                x => x.terrenoId == dto.TerrenoId && x.activo,
                cancellationToken))
        {
            return NotFound(Respuesta("No se encontró un terreno activo."));
        }

        int? existente = await EscalarEnteroAsync(
            """
            SELECT usuarioTerrenoAsignacionId
            FROM dbo.usuarioTerrenoAsignacion
            WHERE usuarioId = @usuarioId
              AND terrenoId = @terrenoId
              AND tipoAsignacion = @tipo
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@terrenoId", dto.TerrenoId);
                AgregarParametro(command, "@tipo", tipo);
            },
            cancellationToken);

        if (existente.HasValue)
            return Conflict(Respuesta("La asignación ya se encuentra activa."));

        await EjecutarAsync(
            """
            INSERT INTO dbo.usuarioTerrenoAsignacion
            (
                usuarioId,
                terrenoId,
                tipoAsignacion,
                esResponsablePrincipal,
                observacion,
                activo,
                fechaInicioUtc,
                asignadoPorUsuarioId
            )
            VALUES
            (
                @usuarioId,
                @terrenoId,
                @tipo,
                @principal,
                @observacion,
                1,
                SYSUTCDATETIME(),
                @usuarioSesionId
            );
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@terrenoId", dto.TerrenoId);
                AgregarParametro(command, "@tipo", tipo);
                AgregarParametro(
                    command,
                    "@principal",
                    dto.EsResponsablePrincipal);
                AgregarParametro(
                    command,
                    "@observacion",
                    Limpiar(dto.Observacion));
                AgregarParametro(
                    command,
                    "@usuarioSesionId",
                    ObtenerUsuarioId());
            },
            cancellationToken);

        await IncrementarVersionSesionAsync(
            dto.UsuarioId,
            cancellationToken);

        return Ok(Respuesta("Terreno asignado correctamente.", true));
    }

    [HttpDelete("asignaciones/{asignacionId:int}")]
    public async Task<IActionResult> DesactivarAsignacion(
        int asignacionId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.AsignacionTerreno,
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        int? usuarioId = await EscalarEnteroAsync(
            """
            SELECT usuarioId
            FROM dbo.usuarioTerrenoAsignacion
            WHERE usuarioTerrenoAsignacionId = @asignacionId
              AND activo = 1;
            """,
            command => AgregarParametro(
                command,
                "@asignacionId",
                asignacionId),
            cancellationToken);

        if (!usuarioId.HasValue)
            return NotFound(Respuesta("No se encontró la asignación activa."));

        await EjecutarAsync(
            """
            UPDATE dbo.usuarioTerrenoAsignacion
            SET activo = 0,
                fechaFinUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioSesionId
            WHERE usuarioTerrenoAsignacionId = @asignacionId
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(
                    command,
                    "@usuarioSesionId",
                    ObtenerUsuarioId());
                AgregarParametro(command, "@asignacionId", asignacionId);
            },
            cancellationToken);

        await IncrementarVersionSesionAsync(
            usuarioId.Value,
            cancellationToken);

        return Ok(Respuesta("Asignación desactivada correctamente.", true));
    }

    [HttpGet("coberturas")]
    public async Task<IActionResult> ListarCoberturas(
        [FromQuery] int? usuarioId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.CoberturaTerritorial,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        var items = await ConsultarAsync(
            """
            SELECT
                c.usuarioCoberturaTerritorialId,
                c.usuarioId,
                u.nombreUsuario,
                u.nombreCompletoUsuario,
                c.tipoCobertura,
                c.departamentoId,
                d.nombreDepartamento,
                c.municipioId,
                m.nombreMunicipio,
                c.observacion,
                c.fechaInicioUtc
            FROM dbo.usuarioCoberturaTerritorial c
            INNER JOIN dbo.usuario u
                ON u.UsuarioId = c.usuarioId
            LEFT JOIN dbo.departamento d
                ON d.departamentoId = c.departamentoId
            LEFT JOIN dbo.municipio m
                ON m.municipioId = c.municipioId
            WHERE c.activo = 1
              AND (@usuarioId IS NULL OR c.usuarioId = @usuarioId)
            ORDER BY u.nombreCompletoUsuario, c.tipoCobertura;
            """,
            command => AgregarParametro(
                command,
                "@usuarioId",
                usuarioId),
            reader => new
            {
                usuarioCoberturaTerritorialId = reader.GetInt32(0),
                usuarioId = reader.GetInt32(1),
                nombreUsuario = Texto(reader, 2),
                nombreCompletoUsuario = Texto(reader, 3),
                tipoCobertura = Texto(reader, 4),
                departamentoId = EnteroNullable(reader, 5),
                departamento = TextoNullable(reader, 6),
                municipioId = EnteroNullable(reader, 7),
                municipio = TextoNullable(reader, 8),
                observacion = TextoNullable(reader, 9),
                fechaInicioUtc = reader.GetDateTime(10)
            },
            cancellationToken);

        return Ok(items);
    }

    [HttpPost("coberturas")]
    public async Task<IActionResult> GuardarCobertura(
        [FromBody] GuardarCoberturaTerritorialDto dto,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.CoberturaTerritorial,
            TipoPermisoApi.Agregar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        string tipo;

        try
        {
            tipo = NormalizarCobertura(dto.TipoCobertura);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Respuesta(ex.Message));
        }

        if (tipo == "DEPARTAMENTO" && !dto.DepartamentoId.HasValue)
            return BadRequest(Respuesta("Debe seleccionar un departamento."));

        if (tipo == "MUNICIPIO" && !dto.MunicipioId.HasValue)
            return BadRequest(Respuesta("Debe seleccionar un municipio."));

        if (tipo == "NACIONAL")
        {
            dto.DepartamentoId = null;
            dto.MunicipioId = null;
        }

        if (!await db.Usuarios.AsNoTracking().AnyAsync(
                x => x.UsuarioId == dto.UsuarioId && x.activo,
                cancellationToken))
        {
            return NotFound(Respuesta("No se encontró un usuario activo."));
        }

        await EjecutarAsync(
            """
            INSERT INTO dbo.usuarioCoberturaTerritorial
            (
                usuarioId,
                tipoCobertura,
                departamentoId,
                municipioId,
                observacion,
                activo,
                fechaInicioUtc,
                asignadoPorUsuarioId
            )
            VALUES
            (
                @usuarioId,
                @tipo,
                @departamentoId,
                @municipioId,
                @observacion,
                1,
                SYSUTCDATETIME(),
                @usuarioSesionId
            );
            """,
            command =>
            {
                AgregarParametro(command, "@usuarioId", dto.UsuarioId);
                AgregarParametro(command, "@tipo", tipo);
                AgregarParametro(
                    command,
                    "@departamentoId",
                    dto.DepartamentoId);
                AgregarParametro(command, "@municipioId", dto.MunicipioId);
                AgregarParametro(
                    command,
                    "@observacion",
                    Limpiar(dto.Observacion));
                AgregarParametro(
                    command,
                    "@usuarioSesionId",
                    ObtenerUsuarioId());
            },
            cancellationToken);

        await IncrementarVersionSesionAsync(
            dto.UsuarioId,
            cancellationToken);

        return Ok(Respuesta("Cobertura agregada correctamente.", true));
    }

    [HttpDelete("coberturas/{coberturaId:int}")]
    public async Task<IActionResult> DesactivarCobertura(
        int coberturaId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarPermisoAsync(
            ParametrizacionAccesoDatabaseInitializer.CoberturaTerritorial,
            TipoPermisoApi.Eliminar,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        int? usuarioId = await EscalarEnteroAsync(
            """
            SELECT usuarioId
            FROM dbo.usuarioCoberturaTerritorial
            WHERE usuarioCoberturaTerritorialId = @coberturaId
              AND activo = 1;
            """,
            command => AgregarParametro(
                command,
                "@coberturaId",
                coberturaId),
            cancellationToken);

        if (!usuarioId.HasValue)
            return NotFound(Respuesta("No se encontró la cobertura activa."));

        await EjecutarAsync(
            """
            UPDATE dbo.usuarioCoberturaTerritorial
            SET activo = 0,
                fechaFinUtc = SYSUTCDATETIME(),
                desasignadoPorUsuarioId = @usuarioSesionId
            WHERE usuarioCoberturaTerritorialId = @coberturaId
              AND activo = 1;
            """,
            command =>
            {
                AgregarParametro(
                    command,
                    "@usuarioSesionId",
                    ObtenerUsuarioId());
                AgregarParametro(command, "@coberturaId", coberturaId);
            },
            cancellationToken);

        await IncrementarVersionSesionAsync(
            usuarioId.Value,
            cancellationToken);

        return Ok(Respuesta("Cobertura desactivada correctamente.", true));
    }

    [HttpGet("catalogos/usuarios")]
    public async Task<IActionResult> CatalogoUsuarios(
        CancellationToken cancellationToken = default)
    {
        if (!await PuedeConsultarAlgunaParametrizacionAsync(cancellationToken))
            return Forbid();

        var items = await db.Usuarios
            .AsNoTracking()
            .Where(x => x.activo)
            .OrderBy(x => x.nombreCompletoUsuario)
            .Select(x => new
            {
                usuarioId = x.UsuarioId,
                x.nombreUsuario,
                x.nombreCompletoUsuario,
                x.correoUsuario,
                rol = x.Rol.nombreRol
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("catalogos/terrenos")]
    public async Task<IActionResult> CatalogoTerrenos(
        CancellationToken cancellationToken = default)
    {
        if (!await PuedeConsultarAlgunaParametrizacionAsync(cancellationToken))
            return Forbid();

        var items = await db.Terreno
            .AsNoTracking()
            .Where(x => x.activo)
            .OrderBy(x => x.codigoTerreno)
            .Select(x => new
            {
                terrenoId = x.terrenoId,
                codigoTerreno = x.codigoTerreno,
                propietarioActual =
                    x.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            relacion.Propietario.nombreCompleto)
                        .FirstOrDefault() ??
                    string.Empty,
                direccion = x.direccionTerreno
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("catalogos/departamentos")]
    public async Task<IActionResult> CatalogoDepartamentos(
        CancellationToken cancellationToken = default)
    {
        if (!await PuedeConsultarAlgunaParametrizacionAsync(cancellationToken))
            return Forbid();

        var items = await db.Departamento
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.NombreDepartamento)
            .Select(x => new
            {
                departamentoId = x.DepartamentoId,
                nombre = x.NombreDepartamento
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("catalogos/municipios")]
    public async Task<IActionResult> CatalogoMunicipios(
        [FromQuery] int? departamentoId,
        CancellationToken cancellationToken = default)
    {
        if (!await PuedeConsultarAlgunaParametrizacionAsync(cancellationToken))
            return Forbid();

        var consulta = db.Municipios
            .AsNoTracking()
            .Where(x => x.Activo);

        if (departamentoId.HasValue)
        {
            consulta = consulta.Where(
                x => x.DepartamentoId == departamentoId.Value);
        }

        var items = await consulta
            .OrderBy(x => x.NombreMunicipio)
            .Select(x => new
            {
                municipioId = x.MunicipioId,
                departamentoId = x.DepartamentoId,
                nombre = x.NombreMunicipio
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("mi-acceso/terrenos")]
    public async Task<IActionResult> MisTerrenos(
        CancellationToken cancellationToken = default)
    {
        int usuarioId = ObtenerUsuarioId();

        var items = await ConsultarAsync(
            """
            ;WITH TerrenosAutorizados AS
            (
                SELECT pt.terrenoId
                FROM dbo.usuarioPropietario up
                INNER JOIN dbo.propietarioTerreno pt
                    ON pt.propietarioId = up.propietarioId
                   AND pt.activo = 1
                WHERE up.usuarioId = @usuarioId
                  AND up.activo = 1

                UNION

                SELECT a.terrenoId
                FROM dbo.usuarioTerrenoAsignacion a
                WHERE a.usuarioId = @usuarioId
                  AND a.activo = 1

                UNION

                SELECT t.terrenoId
                FROM dbo.usuarioCoberturaTerritorial c
                INNER JOIN dbo.terreno t
                    ON c.tipoCobertura = N'NACIONAL'
                    OR (
                        c.tipoCobertura = N'DEPARTAMENTO'
                        AND EXISTS
                        (
                            SELECT 1
                            FROM dbo.municipio m
                            WHERE m.municipioId = t.municipioId
                              AND m.departamentoId = c.departamentoId
                        )
                    )
                    OR (
                        c.tipoCobertura = N'MUNICIPIO'
                        AND t.municipioId = c.municipioId
                    )
                WHERE c.usuarioId = @usuarioId
                  AND c.activo = 1
            )
            SELECT DISTINCT
                t.terrenoId,
                t.codigoTerreno,
                ISNULL(p.nombreCompleto, N''),
                t.direccionTerreno,
                t.extensionManzanaTerreno,
                t.cantidadQuintalesOro,
                t.latitud,
                t.longitud
            FROM TerrenosAutorizados a
            INNER JOIN dbo.terreno t
                ON t.terrenoId = a.terrenoId
            LEFT JOIN dbo.propietarioTerreno pt
                ON pt.terrenoId = t.terrenoId
               AND pt.activo = 1
            LEFT JOIN dbo.propietario p
                ON p.propietarioId = pt.propietarioId
               AND p.activo = 1
            WHERE t.activo = 1
            ORDER BY t.codigoTerreno;
            """,
            command => AgregarParametro(command, "@usuarioId", usuarioId),
            reader => new
            {
                terrenoId = reader.GetInt32(0),
                codigoTerreno = Texto(reader, 1),
                propietario = Texto(reader, 2),
                direccion = Texto(reader, 3),
                extensionManzanas = reader.GetDecimal(4),
                quintalesOro = reader.GetDecimal(5),
                latitud = reader.GetDecimal(6),
                longitud = reader.GetDecimal(7)
            },
            cancellationToken);

        return Ok(items);
    }

    private async Task<IActionResult?>
        ValidarLecturaPropietariosOVinculacionAsync(
            CancellationToken cancellationToken)
    {
        int usuarioId = ObtenerUsuarioId();

        ResultadoPermisoApi propietarios = await permisos.ValidarAsync(
            usuarioId,
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (propietarios.Permitido)
            return null;

        ResultadoPermisoApi vinculaciones = await permisos.ValidarAsync(
            usuarioId,
            ParametrizacionAccesoDatabaseInitializer.UsuarioPropietario,
            TipoPermisoApi.Leer,
            cancellationToken);

        if (vinculaciones.Permitido)
            return null;

        int codigo = propietarios.CodigoEstado ==
            StatusCodes.Status401Unauthorized
                ? propietarios.CodigoEstado
                : vinculaciones.CodigoEstado;

        return StatusCode(
            codigo,
            Respuesta(
                "No tiene permiso para consultar propietarios o vinculaciones."));
    }

    private async Task<IActionResult?> ValidarPermisoAsync(
        string interfaz,
        TipoPermisoApi tipo,
        CancellationToken cancellationToken)
    {
        int usuarioId = ObtenerUsuarioId();

        ResultadoPermisoApi resultado = await permisos.ValidarAsync(
            usuarioId,
            interfaz,
            tipo,
            cancellationToken);

        if (resultado.Permitido)
            return null;

        return StatusCode(
            resultado.CodigoEstado,
            Respuesta(resultado.Mensaje));
    }

    private async Task<bool> PuedeConsultarAlgunaParametrizacionAsync(
        CancellationToken cancellationToken)
    {
        int usuarioId = ObtenerUsuarioId();

        string[] interfaces =
        [
            ParametrizacionAccesoDatabaseInitializer.Propietarios,
            ParametrizacionAccesoDatabaseInitializer.UsuarioPropietario,
            ParametrizacionAccesoDatabaseInitializer.AsignacionTerreno,
            ParametrizacionAccesoDatabaseInitializer.CoberturaTerritorial
        ];

        foreach (string interfaz in interfaces)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (resultado.Permitido)
                return true;
        }

        return false;
    }

    private int ObtenerUsuarioId()
    {
        string? valor = User.FindFirstValue("uid")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Request.Headers["X-Usuario-Id"].FirstOrDefault();

        return int.TryParse(valor, out int usuarioId) && usuarioId > 0
            ? usuarioId
            : throw new UnauthorizedAccessException(
                "No se encontró la identidad del usuario autenticado.");
    }

    private async Task<bool> ExistePropietarioAsync(
        int propietarioId,
        CancellationToken cancellationToken)
    {
        int? resultado = await EscalarEnteroAsync(
            """
            SELECT propietarioId
            FROM dbo.propietario
            WHERE propietarioId = @propietarioId;
            """,
            command => AgregarParametro(
                command,
                "@propietarioId",
                propietarioId),
            cancellationToken);

        return resultado.HasValue;
    }

    private async Task IncrementarVersionSesionAsync(
        int usuarioId,
        CancellationToken cancellationToken)
    {
        await EjecutarAsync(
            """
            UPDATE dbo.usuario
            SET versionSesion = versionSesion + 1
            WHERE UsuarioId = @usuarioId;
            """,
            command => AgregarParametro(command, "@usuarioId", usuarioId),
            cancellationToken);
    }

    private async Task<List<T>> ConsultarAsync<T>(
        string sql,
        Action<DbCommand>? configurar,
        Func<DbDataReader, T> mapear,
        CancellationToken cancellationToken)
    {
        var resultado = new List<T>();
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        try
        {
            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                db.Database.CurrentTransaction?.GetDbTransaction();
            configurar?.Invoke(command);

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(mapear(reader));
        }
        finally
        {
            if (cerrar && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return resultado;
    }

    private async Task<int?> EscalarEnteroAsync(
        string sql,
        Action<DbCommand>? configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        try
        {
            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                db.Database.CurrentTransaction?.GetDbTransaction();
            configurar?.Invoke(command);

            object? valor = await command.ExecuteScalarAsync(cancellationToken);

            return valor is null || valor == DBNull.Value
                ? null
                : Convert.ToInt32(valor);
        }
        finally
        {
            if (cerrar && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    private async Task<int> EjecutarAsync(
        string sql,
        Action<DbCommand>? configurar,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        try
        {
            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                db.Database.CurrentTransaction?.GetDbTransaction();
            configurar?.Invoke(command);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (cerrar && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
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

    private static string Texto(DbDataReader reader, int indice) =>
        reader.IsDBNull(indice)
            ? string.Empty
            : Convert.ToString(reader.GetValue(indice)) ?? string.Empty;

    private static string? TextoNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToString(reader.GetValue(indice));

    private static int? EnteroNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToInt32(reader.GetValue(indice));

    private static string? Limpiar(string? valor)
    {
        string limpio = (valor ?? string.Empty)
            .ReplaceLineEndings(" ")
            .Trim();

        return string.IsNullOrWhiteSpace(limpio)
            ? null
            : limpio;
    }

    private static string NormalizarIdentificacion(string valor) =>
        new(
            valor
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

    private static string NormalizarTipo(string valor)
    {
        string tipo = (valor ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        return string.IsNullOrWhiteSpace(tipo)
            ? "TECNICO"
            : tipo;
    }

    private static string NormalizarCobertura(string valor)
    {
        string tipo = (valor ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        return tipo switch
        {
            "NACIONAL" => "NACIONAL",
            "DEPARTAMENTO" => "DEPARTAMENTO",
            "MUNICIPIO" => "MUNICIPIO",
            _ => throw new ArgumentException(
                "El tipo de cobertura no es válido.")
        };
    }

    private static object Respuesta(
        string mensaje,
        bool success = false) =>
        new
        {
            success,
            message = mensaje
        };
}
