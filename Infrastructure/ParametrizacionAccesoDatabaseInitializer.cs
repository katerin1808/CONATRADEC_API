using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure;

/// <summary>
/// Mantiene la estructura normalizada de acceso y propiedad.
///
/// La propiedad legal de una finca se representa mediante:
///
/// propietario -> propietarioTerreno -> terreno
///
/// La cuenta del portal se representa mediante:
///
/// usuario -> usuarioPropietario -> propietario
///
/// Las asignaciones operativas y coberturas territoriales son relaciones
/// independientes y nunca deben utilizarse para determinar propiedad.
/// </summary>
public sealed class ParametrizacionAccesoDatabaseInitializer
{
    public const string ParametrizacionAcceso =
        "ParametrizacionAccesoPage";

    public const string Propietarios =
        "PropietariosPage";

    public const string UsuarioPropietario =
        "UsuarioPropietarioPage";

    public const string AsignacionTerreno =
        "AsignacionTerrenoPage";

    public const string CoberturaTerritorial =
        "CoberturaTerritorialPage";

    public const string PortalPropietario =
        "PortalPropietarioPage";

    private readonly DBContext db;
    private readonly ILogger<
        ParametrizacionAccesoDatabaseInitializer> logger;

    public ParametrizacionAccesoDatabaseInitializer(
        DBContext db,
        ILogger<
            ParametrizacionAccesoDatabaseInitializer> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task InicializarAsync(
        CancellationToken cancellationToken = default)
    {
        await CrearEstructuraAsync(cancellationToken);

        await CrearPermisosAsync(cancellationToken);

        logger.LogInformation(
            "Estructura normalizada de propietarios y terrenos inicializada.");
    }

    private Task CrearEstructuraAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.propietario', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.propietario
                (
                    propietarioId INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT PK_propietario PRIMARY KEY,

                    identificacion NVARCHAR(50) NOT NULL,

                    identificacionNormalizada NVARCHAR(50) NOT NULL,

                    nombreCompleto NVARCHAR(150) NOT NULL,

                    telefono NVARCHAR(25) NULL,

                    correo NVARCHAR(150) NULL,

                    direccion NVARCHAR(300) NULL,

                    activo BIT NOT NULL
                        CONSTRAINT DF_propietario_activo
                        DEFAULT(1),

                    fechaRegistroUtc DATETIME2(0) NOT NULL
                        CONSTRAINT DF_propietario_fechaRegistroUtc
                        DEFAULT(SYSUTCDATETIME()),

                    fechaActualizacionUtc DATETIME2(0) NULL,

                    usuarioRegistroId INT NULL,

                    usuarioActualizacionId INT NULL
                );

                CREATE UNIQUE INDEX
                    UX_propietario_identificacionNormalizada
                    ON dbo.propietario(
                        identificacionNormalizada);
            END;

            IF OBJECT_ID(
                    N'dbo.propietarioTerreno',
                    N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.propietarioTerreno
                (
                    propietarioTerrenoId
                        INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT PK_propietarioTerreno
                        PRIMARY KEY,

                    propietarioId INT NOT NULL,

                    terrenoId INT NOT NULL,

                    activo BIT NOT NULL
                        CONSTRAINT
                            DF_propietarioTerreno_activo
                        DEFAULT(1),

                    fechaAsignacionUtc DATETIME2(0) NOT NULL
                        CONSTRAINT
                            DF_propietarioTerreno_fechaAsignacionUtc
                        DEFAULT(SYSUTCDATETIME()),

                    fechaDesasignacionUtc DATETIME2(0) NULL,

                    asignadoPorUsuarioId INT NULL,

                    desasignadoPorUsuarioId INT NULL,

                    CONSTRAINT
                        FK_propietarioTerreno_propietario
                        FOREIGN KEY(propietarioId)
                        REFERENCES dbo.propietario(
                            propietarioId),

                    CONSTRAINT
                        FK_propietarioTerreno_terreno
                        FOREIGN KEY(terrenoId)
                        REFERENCES dbo.terreno(
                            terrenoId)
                );

                CREATE UNIQUE INDEX
                    UX_propietarioTerreno_terreno_activo
                    ON dbo.propietarioTerreno(terrenoId)
                    WHERE activo = 1;

                CREATE INDEX
                    IX_propietarioTerreno_propietario
                    ON dbo.propietarioTerreno(
                        propietarioId,
                        activo);
            END;

            /*
             * Compatibilidad con bases de datos anteriores a la
             * normalización propietario -> propietarioTerreno -> terreno.
             *
             * Algunas instalaciones conservan en dbo.terreno las columnas
             * históricas del propietario como NOT NULL y, además, poseen
             * índices que dependen de ellas. No se modifica su nulabilidad ni
             * se eliminan índices: se agregan valores DEFAULT únicamente cuando
             * la columna sigue siendo obligatoria y todavía no tiene uno.
             *
             * De esta forma el INSERT actual puede omitir los campos antiguos y
             * la propiedad real continúa almacenándose en propietarioTerreno.
             */
            IF OBJECT_ID(N'dbo.terreno', N'U') IS NOT NULL
            BEGIN
                IF EXISTS
                (
                    SELECT 1
                    FROM sys.columns c
                    WHERE c.object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name =
                        N'identificacionPropietarioTerreno'
                      AND c.is_nullable = 0
                )
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name =
                        N'identificacionPropietarioTerreno'
                )
                BEGIN
                    ALTER TABLE dbo.terreno
                        ADD CONSTRAINT
                            DF_terreno_identificacionPropietario_Compat
                        DEFAULT(N'')
                        FOR identificacionPropietarioTerreno;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.columns c
                    WHERE c.object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name =
                        N'nombrePropietarioTerreno'
                      AND c.is_nullable = 0
                )
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name =
                        N'nombrePropietarioTerreno'
                )
                BEGIN
                    ALTER TABLE dbo.terreno
                        ADD CONSTRAINT
                            DF_terreno_nombrePropietario_Compat
                        DEFAULT(N'')
                        FOR nombrePropietarioTerreno;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.columns c
                    WHERE c.object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name = N'telefonoPropietario'
                      AND c.is_nullable = 0
                )
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id =
                        OBJECT_ID(N'dbo.terreno')
                      AND c.name = N'telefonoPropietario'
                )
                BEGIN
                    ALTER TABLE dbo.terreno
                        ADD CONSTRAINT
                            DF_terreno_telefonoPropietario_Compat
                        DEFAULT(0)
                        FOR telefonoPropietario;
                END;
            END;

            /*
             * Si propietarioTerreno ya existía antes de incorporar el historial
             * de asignaciones, completa únicamente las columnas faltantes. La
             * operación es idempotente y conserva todas las relaciones actuales.
             */
            IF OBJECT_ID(
                    N'dbo.propietarioTerreno',
                    N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(
                        N'dbo.propietarioTerreno',
                        N'fechaAsignacionUtc') IS NULL
                BEGIN
                    ALTER TABLE dbo.propietarioTerreno
                        ADD fechaAsignacionUtc DATETIME2(0) NOT NULL
                            CONSTRAINT
                                DF_propietarioTerreno_fechaAsignacionUtc
                            DEFAULT(SYSUTCDATETIME()) WITH VALUES;
                END;

                IF COL_LENGTH(
                        N'dbo.propietarioTerreno',
                        N'fechaDesasignacionUtc') IS NULL
                BEGIN
                    ALTER TABLE dbo.propietarioTerreno
                        ADD fechaDesasignacionUtc DATETIME2(0) NULL;
                END;

                IF COL_LENGTH(
                        N'dbo.propietarioTerreno',
                        N'asignadoPorUsuarioId') IS NULL
                BEGIN
                    ALTER TABLE dbo.propietarioTerreno
                        ADD asignadoPorUsuarioId INT NULL;
                END;

                IF COL_LENGTH(
                        N'dbo.propietarioTerreno',
                        N'desasignadoPorUsuarioId') IS NULL
                BEGIN
                    ALTER TABLE dbo.propietarioTerreno
                        ADD desasignadoPorUsuarioId INT NULL;
                END;
            END;

            IF OBJECT_ID(
                    N'dbo.usuarioPropietario',
                    N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.usuarioPropietario
                (
                    usuarioPropietarioId
                        INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT PK_usuarioPropietario
                        PRIMARY KEY,

                    usuarioId INT NOT NULL,

                    propietarioId INT NOT NULL,

                    activo BIT NOT NULL
                        CONSTRAINT
                            DF_usuarioPropietario_activo
                        DEFAULT(1),

                    fechaAsignacionUtc DATETIME2(0) NOT NULL
                        CONSTRAINT
                            DF_usuarioPropietario_fechaAsignacionUtc
                        DEFAULT(SYSUTCDATETIME()),

                    fechaDesasignacionUtc DATETIME2(0) NULL,

                    asignadoPorUsuarioId INT NULL,

                    desasignadoPorUsuarioId INT NULL,

                    CONSTRAINT
                        FK_usuarioPropietario_usuario
                        FOREIGN KEY(usuarioId)
                        REFERENCES dbo.usuario(UsuarioId),

                    CONSTRAINT
                        FK_usuarioPropietario_propietario
                        FOREIGN KEY(propietarioId)
                        REFERENCES dbo.propietario(
                            propietarioId)
                );

                CREATE UNIQUE INDEX
                    UX_usuarioPropietario_usuario_activo
                    ON dbo.usuarioPropietario(usuarioId)
                    WHERE activo = 1;

                CREATE UNIQUE INDEX
                    UX_usuarioPropietario_propietario_activo
                    ON dbo.usuarioPropietario(
                        propietarioId)
                    WHERE activo = 1;
            END;

            IF OBJECT_ID(
                    N'dbo.usuarioTerrenoAsignacion',
                    N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.usuarioTerrenoAsignacion
                (
                    usuarioTerrenoAsignacionId
                        INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT
                            PK_usuarioTerrenoAsignacion
                        PRIMARY KEY,

                    usuarioId INT NOT NULL,

                    terrenoId INT NOT NULL,

                    tipoAsignacion NVARCHAR(50) NOT NULL,

                    esResponsablePrincipal BIT NOT NULL
                        CONSTRAINT
                            DF_usuarioTerrenoAsignacion_principal
                        DEFAULT(0),

                    observacion NVARCHAR(500) NULL,

                    activo BIT NOT NULL
                        CONSTRAINT
                            DF_usuarioTerrenoAsignacion_activo
                        DEFAULT(1),

                    fechaInicioUtc DATETIME2(0) NOT NULL
                        CONSTRAINT
                            DF_usuarioTerrenoAsignacion_fechaInicioUtc
                        DEFAULT(SYSUTCDATETIME()),

                    fechaFinUtc DATETIME2(0) NULL,

                    asignadoPorUsuarioId INT NULL,

                    desasignadoPorUsuarioId INT NULL,

                    CONSTRAINT
                        FK_usuarioTerrenoAsignacion_usuario
                        FOREIGN KEY(usuarioId)
                        REFERENCES dbo.usuario(UsuarioId),

                    CONSTRAINT
                        FK_usuarioTerrenoAsignacion_terreno
                        FOREIGN KEY(terrenoId)
                        REFERENCES dbo.terreno(terrenoId)
                );

                CREATE UNIQUE INDEX
                    UX_usuarioTerrenoAsignacion_activa
                    ON dbo.usuarioTerrenoAsignacion
                    (
                        usuarioId,
                        terrenoId,
                        tipoAsignacion
                    )
                    WHERE activo = 1;

                CREATE INDEX
                    IX_usuarioTerrenoAsignacion_terreno
                    ON dbo.usuarioTerrenoAsignacion
                    (
                        terrenoId,
                        activo
                    );
            END;

            IF OBJECT_ID(
                    N'dbo.usuarioCoberturaTerritorial',
                    N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.usuarioCoberturaTerritorial
                (
                    usuarioCoberturaTerritorialId
                        INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT
                            PK_usuarioCoberturaTerritorial
                        PRIMARY KEY,

                    usuarioId INT NOT NULL,

                    tipoCobertura NVARCHAR(30) NOT NULL,

                    departamentoId INT NULL,

                    municipioId INT NULL,

                    observacion NVARCHAR(500) NULL,

                    activo BIT NOT NULL
                        CONSTRAINT
                            DF_usuarioCoberturaTerritorial_activo
                        DEFAULT(1),

                    fechaInicioUtc DATETIME2(0) NOT NULL
                        CONSTRAINT
                            DF_usuarioCoberturaTerritorial_fechaInicioUtc
                        DEFAULT(SYSUTCDATETIME()),

                    fechaFinUtc DATETIME2(0) NULL,

                    asignadoPorUsuarioId INT NULL,

                    desasignadoPorUsuarioId INT NULL,

                    CONSTRAINT
                        FK_usuarioCoberturaTerritorial_usuario
                        FOREIGN KEY(usuarioId)
                        REFERENCES dbo.usuario(UsuarioId),

                    CONSTRAINT
                        FK_usuarioCoberturaTerritorial_departamento
                        FOREIGN KEY(departamentoId)
                        REFERENCES dbo.departamento(
                            departamentoId),

                    CONSTRAINT
                        FK_usuarioCoberturaTerritorial_municipio
                        FOREIGN KEY(municipioId)
                        REFERENCES dbo.municipio(
                            municipioId),

                    CONSTRAINT
                        CK_usuarioCoberturaTerritorial_tipo
                        CHECK(
                            tipoCobertura IN
                            (
                                N'NACIONAL',
                                N'DEPARTAMENTO',
                                N'MUNICIPIO'
                            )
                        )
                );

                CREATE INDEX
                    IX_usuarioCoberturaTerritorial_usuario
                    ON dbo.usuarioCoberturaTerritorial
                    (
                        usuarioId,
                        activo
                    );
            END;
            """;

        return db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    private async Task CrearPermisosAsync(
        CancellationToken cancellationToken)
    {
        var definiciones = new[]
        {
            new Definicion(
                ParametrizacionAcceso,
                "Propietarios y acceso",
                "Permite abrir el módulo de parametrización " +
                "de acceso a terrenos."),

            new Definicion(
                Propietarios,
                "Propietarios",
                "Permite consultar y administrar propietarios."),

            new Definicion(
                UsuarioPropietario,
                "Vinculación de portal",
                "Permite vincular cuentas de usuario con " +
                "propietarios."),

            new Definicion(
                AsignacionTerreno,
                "Asignación de terrenos",
                "Permite asignar terrenos a técnicos, " +
                "supervisores u otros usuarios."),

            new Definicion(
                CoberturaTerritorial,
                "Cobertura territorial",
                "Permite definir acceso nacional, " +
                "departamental o municipal."),

            new Definicion(
                PortalPropietario,
                "Portal del propietario",
                "Permite ingresar al portal y consultar la " +
                "información propia.")
        };

        foreach (Definicion definicion in definiciones)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    item =>
                        item.nombreInterfaz ==
                        definicion.Codigo,
                    cancellationToken);

            if (interfaz is null)
            {
                db.Interfaz.Add(new Interfaz
                {
                    nombreInterfaz =
                        definicion.Codigo,
                    nombreAmigableInterfaz =
                        definicion.Nombre,
                    descripcionInterfaz =
                        definicion.Descripcion,
                    activo = true
                });
            }
            else
            {
                interfaz.nombreAmigableInterfaz =
                    definicion.Nombre;

                interfaz.descripcionInterfaz =
                    definicion.Descripcion;

                interfaz.activo = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        List<int> rolesAdministradores =
            await db.Roles
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.nombreRol
                        .Trim()
                        .ToUpper() ==
                    "ADMINISTRADOR")
                .Select(item => item.rolId)
                .ToListAsync(cancellationToken);

        string[] codigos =
            definiciones
                .Select(item => item.Codigo)
                .ToArray();

        List<Interfaz> interfaces =
            await db.Interfaz
                .Where(item =>
                    codigos.Contains(
                        item.nombreInterfaz))
                .ToListAsync(cancellationToken);

        foreach (int rolId in rolesAdministradores)
        {
            foreach (Interfaz interfaz in interfaces)
            {
                RolInterfaz? relacion =
                    await db.RolInterfaz
                        .FirstOrDefaultAsync(
                            item =>
                                item.rolId == rolId &&
                                item.interfazId ==
                                interfaz.interfazId,
                            cancellationToken);

                if (relacion is null)
                {
                    db.RolInterfaz.Add(
                        new RolInterfaz
                        {
                            rolId = rolId,
                            interfazId =
                                interfaz.interfazId,
                            leer = true,
                            agregar = true,
                            actualizar = true,
                            eliminar = true
                        });
                }
                else
                {
                    relacion.leer = true;
                    relacion.agregar = true;
                    relacion.actualizar = true;
                    relacion.eliminar = true;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record Definicion(
        string Codigo,
        string Nombre,
        string Descripcion);
}
