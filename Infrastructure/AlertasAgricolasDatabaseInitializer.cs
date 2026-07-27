using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    public sealed class AlertasAgricolasDatabaseInitializer
    {
        private readonly AlertasAgricolasDbContext db;
        private readonly ILogger<
            AlertasAgricolasDatabaseInitializer> logger;

        public AlertasAgricolasDatabaseInitializer(
            AlertasAgricolasDbContext db,
            ILogger<
                AlertasAgricolasDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[configuracionAlertaAgricola]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[configuracionAlertaAgricola]
    (
        [ConfiguracionAlertaAgricolaId]
            INT IDENTITY(1,1) NOT NULL,
        [Clave] NVARCHAR(80) NOT NULL,
        [Nombre] NVARCHAR(120) NOT NULL,
        [Valor] DECIMAL(12,4) NOT NULL,
        [Operador] NVARCHAR(30) NOT NULL,
        [Unidad] NVARCHAR(30) NOT NULL
            CONSTRAINT [DF_cfgAlerta_unidad]
            DEFAULT(N''),
        [Descripcion] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_cfgAlerta_descripcion]
            DEFAULT(N''),
        [Activo] BIT NOT NULL
            CONSTRAINT [DF_cfgAlerta_activo]
            DEFAULT(1),
        [FechaModificacionUtc] DATETIME2 NOT NULL,
        [UsuarioModificacionId] INT NULL,

        CONSTRAINT [PK_configuracionAlertaAgricola]
            PRIMARY KEY CLUSTERED
            ([ConfiguracionAlertaAgricolaId]),

        CONSTRAINT [UQ_configuracionAlertaAgricola_Clave]
            UNIQUE ([Clave])
    );
END;

IF OBJECT_ID(N'[dbo].[seguimientoAlertaAgricola]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[seguimientoAlertaAgricola]
    (
        [SeguimientoAlertaAgricolaId]
            INT IDENTITY(1,1) NOT NULL,
        [TerrenoId] INT NOT NULL,
        [TipoAlerta] NVARCHAR(80) NOT NULL,
        [Nivel] NVARCHAR(20) NOT NULL,
        [Estado] NVARCHAR(20) NOT NULL,
        [UsuarioAsignadoId] INT NULL,
        [Observacion] NVARCHAR(1000) NOT NULL
            CONSTRAINT [DF_seguimientoAlerta_observacion]
            DEFAULT(N''),
        [FechaCreacionUtc] DATETIME2 NOT NULL,
        [FechaUltimaModificacionUtc] DATETIME2 NOT NULL,
        [FechaCierreUtc] DATETIME2 NULL,
        [UsuarioCreacionId] INT NOT NULL,
        [UsuarioUltimaModificacionId] INT NOT NULL,
        [Activo] BIT NOT NULL
            CONSTRAINT [DF_seguimientoAlerta_activo]
            DEFAULT(1),

        CONSTRAINT [PK_seguimientoAlertaAgricola]
            PRIMARY KEY CLUSTERED
            ([SeguimientoAlertaAgricolaId]),

        CONSTRAINT [FK_seguimientoAlerta_terreno]
            FOREIGN KEY ([TerrenoId])
            REFERENCES [dbo].[terreno]([terrenoId])
    );

    CREATE INDEX [IX_seguimientoAlerta_busqueda]
        ON [dbo].[seguimientoAlertaAgricola]
        ([TerrenoId], [TipoAlerta], [Activo], [Estado]);
END;

IF OBJECT_ID(N'[dbo].[historialAlertaAgricola]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[historialAlertaAgricola]
    (
        [HistorialAlertaAgricolaId]
            INT IDENTITY(1,1) NOT NULL,
        [SeguimientoAlertaAgricolaId] INT NOT NULL,
        [Accion] NVARCHAR(40) NOT NULL,
        [Detalle] NVARCHAR(1000) NOT NULL
            CONSTRAINT [DF_historialAlerta_detalle]
            DEFAULT(N''),
        [UsuarioId] INT NOT NULL,
        [FechaUtc] DATETIME2 NOT NULL,

        CONSTRAINT [PK_historialAlertaAgricola]
            PRIMARY KEY CLUSTERED
            ([HistorialAlertaAgricolaId]),

        CONSTRAINT [FK_historialAlerta_seguimiento]
            FOREIGN KEY ([SeguimientoAlertaAgricolaId])
            REFERENCES [dbo].[seguimientoAlertaAgricola]
            ([SeguimientoAlertaAgricolaId])
    );

    CREATE INDEX [IX_historialAlerta_fecha]
        ON [dbo].[historialAlertaAgricola]
        ([SeguimientoAlertaAgricolaId], [FechaUtc] DESC);
END;

/*
 * Migra las claves anteriores sin perder los valores
 * que el usuario ya haya configurado.
 */
IF EXISTS
(
    SELECT 1
    FROM [dbo].[configuracionAlertaAgricola]
    WHERE [Clave] = N'PH_CRITICO_MAXIMO'
)
AND NOT EXISTS
(
    SELECT 1
    FROM [dbo].[configuracionAlertaAgricola]
    WHERE [Clave] = N'PH_BAJO_CRITICO_MAXIMO'
)
BEGIN
    UPDATE [dbo].[configuracionAlertaAgricola]
    SET
        [Clave] = N'PH_BAJO_CRITICO_MAXIMO',
        [Nombre] = N'pH bajo crítico máximo',
        [Descripcion] =
            N'Valor por debajo del cual el pH bajo se considera crítico.'
    WHERE [Clave] = N'PH_CRITICO_MAXIMO';
END;

IF EXISTS
(
    SELECT 1
    FROM [dbo].[configuracionAlertaAgricola]
    WHERE [Clave] = N'PH_ATENCION_MAXIMO'
)
AND NOT EXISTS
(
    SELECT 1
    FROM [dbo].[configuracionAlertaAgricola]
    WHERE [Clave] = N'PH_BAJO_ATENCION_MAXIMO'
)
BEGIN
    UPDATE [dbo].[configuracionAlertaAgricola]
    SET
        [Clave] = N'PH_BAJO_ATENCION_MAXIMO',
        [Nombre] = N'pH bajo de atención máximo',
        [Descripcion] =
            N'Valor por debajo del cual el pH bajo requiere atención.'
    WHERE [Clave] = N'PH_ATENCION_MAXIMO';
END;

MERGE [dbo].[configuracionAlertaAgricola] AS destino
USING
(
    VALUES
      (
        N'PH_BAJO_CRITICO_MAXIMO',
        N'pH bajo crítico máximo',
        CAST(5.50 AS DECIMAL(12,4)),
        N'MENOR_QUE',
        N'pH',
        N'Valor por debajo del cual el pH bajo se considera crítico.'
      ),
      (
        N'PH_BAJO_ATENCION_MAXIMO',
        N'pH bajo de atención máximo',
        CAST(6.00 AS DECIMAL(12,4)),
        N'MENOR_QUE',
        N'pH',
        N'Valor por debajo del cual el pH bajo requiere atención.'
      ),
      (
        N'PH_ALTO_ATENCION_MINIMO',
        N'pH alto de atención mínimo',
        CAST(6.50 AS DECIMAL(12,4)),
        N'MAYOR_O_IGUAL_QUE',
        N'pH',
        N'Valor desde el cual el pH alto requiere atención.'
      ),
      (
        N'PH_ALTO_CRITICO_MINIMO',
        N'pH alto crítico mínimo',
        CAST(7.00 AS DECIMAL(12,4)),
        N'MAYOR_O_IGUAL_QUE',
        N'pH',
        N'Valor desde el cual el pH alto se considera crítico.'
      ),
      (
        N'MATERIA_ORGANICA_BAJA_MAXIMA',
        N'Materia orgánica baja',
        CAST(3.00 AS DECIMAL(12,4)),
        N'MENOR_QUE',
        N'%',
        N'Valor por debajo del cual la materia orgánica se considera baja.'
      ),
      (
        N'ACIDEZ_ALTA_MINIMA',
        N'Acidez alta mínima',
        CAST(1.00 AS DECIMAL(12,4)),
        N'MAYOR_QUE',
        N'meq/100g',
        N'Valor por encima del cual la acidez se considera alta.'
      )
) AS origen
(
    [Clave],
    [Nombre],
    [Valor],
    [Operador],
    [Unidad],
    [Descripcion]
)
ON destino.[Clave] = origen.[Clave]

WHEN MATCHED THEN
    UPDATE SET
        destino.[Nombre] =
            origen.[Nombre],
        destino.[Operador] =
            origen.[Operador],
        destino.[Unidad] =
            origen.[Unidad],
        destino.[Descripcion] =
            origen.[Descripcion],
        destino.[Activo] = 1

WHEN NOT MATCHED THEN
    INSERT
    (
        [Clave],
        [Nombre],
        [Valor],
        [Operador],
        [Unidad],
        [Descripcion],
        [Activo],
        [FechaModificacionUtc]
    )
    VALUES
    (
        origen.[Clave],
        origen.[Nombre],
        origen.[Valor],
        origen.[Operador],
        origen.[Unidad],
        origen.[Descripcion],
        1,
        SYSUTCDATETIME()
    );

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] =
        N'alertasAgricolasPage'
)
BEGIN
    INSERT INTO [dbo].[interfaz]
    (
        [nombreInterfaz],
        [nombreAmigableInterfaz],
        [descripcionInterfaz],
        [activo]
    )
    VALUES
    (
        N'alertasAgricolasPage',
        N'Centro de alertas agrícolas',
        N'Centro de alertas agrícolas y seguimiento técnico.',
        1
    );
END
ELSE
BEGIN
    UPDATE [dbo].[interfaz]
    SET
        [nombreAmigableInterfaz] =
            N'Centro de alertas agrícolas',
        [descripcionInterfaz] =
            N'Centro de alertas agrícolas y seguimiento técnico.',
        [activo] = 1
    WHERE [nombreInterfaz] =
        N'alertasAgricolasPage';
END;
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);

            logger.LogInformation(
                "Estructura y umbrales de alertas agrícolas verificados correctamente.");
        }
    }
}
