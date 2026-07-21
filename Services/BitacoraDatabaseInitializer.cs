using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public sealed class BitacoraDatabaseInitializer
    {
        private readonly BitacoraDbContext db;
        private readonly ILogger<BitacoraDatabaseInitializer> logger;

        public BitacoraDatabaseInitializer(
            BitacoraDbContext db,
            ILogger<BitacoraDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[bitacora]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[bitacora]
    (
        [bitacoraId] UNIQUEIDENTIFIER NOT NULL,
        [fechaHoraUtc] DATETIME2(3) NOT NULL,
        [usuarioId] INT NULL,
        [usuarioNombre] NVARCHAR(150) NOT NULL CONSTRAINT [DF_bitacora_usuarioNombre] DEFAULT(''),
        [rolNombre] NVARCHAR(100) NOT NULL CONSTRAINT [DF_bitacora_rolNombre] DEFAULT(''),
        [modulo] NVARCHAR(120) NOT NULL,
        [accion] NVARCHAR(50) NOT NULL,
        [metodoHttp] NVARCHAR(10) NOT NULL,
        [endpoint] NVARCHAR(500) NOT NULL,
        [paginaOrigen] NVARCHAR(500) NOT NULL CONSTRAINT [DF_bitacora_paginaOrigen] DEFAULT(''),
        [descripcion] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_bitacora_descripcion] DEFAULT(''),
        [parametros] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_bitacora_parametros] DEFAULT(''),
        [direccionIp] NVARCHAR(100) NOT NULL CONSTRAINT [DF_bitacora_direccionIp] DEFAULT(''),
        [dispositivo] NVARCHAR(200) NOT NULL CONSTRAINT [DF_bitacora_dispositivo] DEFAULT(''),
        [plataforma] NVARCHAR(100) NOT NULL CONSTRAINT [DF_bitacora_plataforma] DEFAULT(''),
        [versionApp] NVARCHAR(50) NOT NULL CONSTRAINT [DF_bitacora_versionApp] DEFAULT(''),
        [correlationId] NVARCHAR(100) NOT NULL CONSTRAINT [DF_bitacora_correlationId] DEFAULT(''),
        [codigoEstado] INT NOT NULL,
        [exitoso] BIT NOT NULL,
        [duracionMs] BIGINT NOT NULL,
        [error] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_bitacora_error] DEFAULT(''),
        CONSTRAINT [PK_bitacora] PRIMARY KEY ([bitacoraId])
    );
END;

IF OBJECT_ID(N'[dbo].[bitacoraDetalle]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[bitacoraDetalle]
    (
        [bitacoraDetalleId] BIGINT IDENTITY(1,1) NOT NULL,
        [bitacoraId] UNIQUEIDENTIFIER NOT NULL,
        [fechaHoraUtc] DATETIME2(3) NOT NULL,
        [entidad] NVARCHAR(150) NOT NULL,
        [entidadId] NVARCHAR(300) NOT NULL CONSTRAINT [DF_bitacoraDetalle_entidadId] DEFAULT(''),
        [operacion] NVARCHAR(30) NOT NULL,
        [valoresAnteriores] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_bitacoraDetalle_anteriores] DEFAULT(''),
        [valoresNuevos] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_bitacoraDetalle_nuevos] DEFAULT(''),
        [propiedadesModificadas] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_bitacoraDetalle_propiedades] DEFAULT(''),
        CONSTRAINT [PK_bitacoraDetalle] PRIMARY KEY ([bitacoraDetalleId]),
        CONSTRAINT [FK_bitacoraDetalle_bitacora]
            FOREIGN KEY ([bitacoraId]) REFERENCES [dbo].[bitacora]([bitacoraId])
            ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_bitacora_fechaHoraUtc'
      AND object_id = OBJECT_ID(N'[dbo].[bitacora]'))
BEGIN
    CREATE INDEX [IX_bitacora_fechaHoraUtc]
        ON [dbo].[bitacora]([fechaHoraUtc] DESC);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_bitacora_usuarioId'
      AND object_id = OBJECT_ID(N'[dbo].[bitacora]'))
BEGIN
    CREATE INDEX [IX_bitacora_usuarioId]
        ON [dbo].[bitacora]([usuarioId]);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_bitacora_modulo_accion'
      AND object_id = OBJECT_ID(N'[dbo].[bitacora]'))
BEGIN
    CREATE INDEX [IX_bitacora_modulo_accion]
        ON [dbo].[bitacora]([modulo], [accion]);
END;

IF OBJECT_ID(N'[dbo].[interfaz]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM [dbo].[interfaz]
       WHERE [nombreInterfaz] = N'bitacoraPage')
BEGIN
    INSERT INTO [dbo].[interfaz]
        ([nombreInterfaz], [descripcionInterfaz], [activo])
    VALUES
        (N'bitacoraPage', N'Consulta de la bitácora general del sistema', 1);
END;

IF OBJECT_ID(N'[dbo].[rolInterfaz]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[Rol]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[interfaz]', N'U') IS NOT NULL
BEGIN
    DECLARE @interfazBitacoraId INT =
        (SELECT TOP 1 [interfazId]
         FROM [dbo].[interfaz]
         WHERE [nombreInterfaz] = N'bitacoraPage');

    IF @interfazBitacoraId IS NOT NULL
    BEGIN
        INSERT INTO [dbo].[rolInterfaz]
            ([leer], [agregar], [actualizar], [eliminar], [rolId], [interfazId])
        SELECT
            1, 0, 0, 0, r.[rolId], @interfazBitacoraId
        FROM [dbo].[Rol] r
        WHERE r.[activo] = 1
          AND UPPER(r.[nombreRol]) LIKE N'%ADMIN%'
          AND NOT EXISTS
          (
              SELECT 1
              FROM [dbo].[rolInterfaz] ri
              WHERE ri.[rolId] = r.[rolId]
                AND ri.[interfazId] = @interfazBitacoraId
          );
    END;
END;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.LogInformation(
                "La estructura de bitácora fue verificada correctamente.");
        }
    }
}
