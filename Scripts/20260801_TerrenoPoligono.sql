/*
    CONATRADEC
    Delimitación opcional de terrenos
    Fecha: 2026-08-01

    No modifica dbo.terreno. El punto principal continúa siendo obligatorio.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.terrenoPoligono', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.terrenoPoligono
    (
        terrenoPoligonoId INT IDENTITY(1,1) NOT NULL,
        terrenoId INT NOT NULL,
        geometriaGeoJson NVARCHAR(MAX) NOT NULL,
        areaMetrosCuadrados DECIMAL(18,2) NOT NULL,
        areaHectareas DECIMAL(18,4) NOT NULL,
        areaManzanasCalculada DECIMAL(18,4) NOT NULL,
        fechaCreacionUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_terrenoPoligono_fechaCreacionUtc
            DEFAULT SYSUTCDATETIME(),
        fechaActualizacionUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_terrenoPoligono_fechaActualizacionUtc
            DEFAULT SYSUTCDATETIME(),
        usuarioActualizacionId INT NULL,
        activo BIT NOT NULL
            CONSTRAINT DF_terrenoPoligono_activo DEFAULT (1),

        CONSTRAINT PK_terrenoPoligono
            PRIMARY KEY CLUSTERED (terrenoPoligonoId),

        CONSTRAINT FK_terrenoPoligono_terreno
            FOREIGN KEY (terrenoId)
            REFERENCES dbo.terreno(terrenoId),

        CONSTRAINT CK_terrenoPoligono_areaMetros
            CHECK (areaMetrosCuadrados > 0),

        CONSTRAINT CK_terrenoPoligono_areaHectareas
            CHECK (areaHectareas > 0),

        CONSTRAINT CK_terrenoPoligono_areaManzanas
            CHECK (areaManzanasCalculada > 0)
    );

    CREATE UNIQUE INDEX UX_terrenoPoligono_terrenoId
        ON dbo.terrenoPoligono(terrenoId);

    CREATE INDEX IX_terrenoPoligono_activo
        ON dbo.terrenoPoligono(activo, terrenoId);
END;

COMMIT TRANSACTION;
