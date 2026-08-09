/*
    CONATRADEC
    Compatibilidad del guardado de terrenos con bases de datos existentes.

    Objetivo:
    1. Mantener intactas las columnas históricas del propietario en dbo.terreno,
       incluso cuando siguen siendo NOT NULL y participan en índices existentes.
    2. Permitir que el modelo normalizado actual omita esas columnas agregando
       un DEFAULT solamente cuando la columna es obligatoria y no posee uno.
    3. Completar las columnas de historial de dbo.propietarioTerreno cuando la
       tabla proviene de una versión anterior.

    El script es idempotente. No elimina registros, columnas, índices ni
    restricciones existentes.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.terreno', N'U') IS NULL
    BEGIN
        THROW 51000, 'No existe la tabla dbo.terreno.', 1;
    END;

    IF OBJECT_ID(N'dbo.propietarioTerreno', N'U') IS NULL
    BEGIN
        THROW 51001, 'No existe la tabla dbo.propietarioTerreno. Publique primero la API para crear la estructura normalizada.', 1;
    END;

    /*
        Las columnas siguientes pertenecen al esquema histórico de terreno.
        Se conservan con su tipo, nulabilidad e índices actuales. Cuando siguen
        siendo NOT NULL y no tienen DEFAULT, se agrega un valor compatible para
        que los INSERT del modelo normalizado puedan omitirlas.
    */
    IF EXISTS
    (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.terreno')
          AND c.name = N'identificacionPropietarioTerreno'
          AND c.is_nullable = 0
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.terreno')
          AND c.name = N'identificacionPropietarioTerreno'
    )
    BEGIN
        ALTER TABLE dbo.terreno
            ADD CONSTRAINT DF_terreno_identificacionPropietario_Compat
            DEFAULT(N'') FOR identificacionPropietarioTerreno;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.terreno')
          AND c.name = N'nombrePropietarioTerreno'
          AND c.is_nullable = 0
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.terreno')
          AND c.name = N'nombrePropietarioTerreno'
    )
    BEGIN
        ALTER TABLE dbo.terreno
            ADD CONSTRAINT DF_terreno_nombrePropietario_Compat
            DEFAULT(N'') FOR nombrePropietarioTerreno;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.terreno')
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
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.terreno')
          AND c.name = N'telefonoPropietario'
    )
    BEGIN
        ALTER TABLE dbo.terreno
            ADD CONSTRAINT DF_terreno_telefonoPropietario_Compat
            DEFAULT(0) FOR telefonoPropietario;
    END;

    /*
        Completa el historial de asignación para instalaciones cuyo
        propietarioTerreno ya existía antes de incorporar estas columnas.
    */
    IF COL_LENGTH(
            N'dbo.propietarioTerreno',
            N'fechaAsignacionUtc') IS NULL
    BEGIN
        ALTER TABLE dbo.propietarioTerreno
            ADD fechaAsignacionUtc DATETIME2(0) NOT NULL
                CONSTRAINT DF_propietarioTerreno_fechaAsignacionUtc
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

    COMMIT TRANSACTION;

    SELECT
        CAST(1 AS BIT) AS Exito,
        N'Compatibilidad del guardado de terrenos aplicada correctamente sin modificar índices.'
            AS Mensaje;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
