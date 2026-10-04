using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Persistencia aditiva de publicaciones del Álbum Botánico por diagnóstico.
    /// La fotografía física nunca se duplica: distintos diagnósticos pueden
    /// apuntar a la misma RutaRelativa de la evidencia original.
    /// </summary>
    public sealed class InspeccionFitosanitariaPublicacionDiagnosticoDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaPublicacionDiagnosticoDatabase(
            DiagnosticoIADbContext db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            if (inicializada)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (inicializada)
                    return;

                const string sql = """
SET NOCOUNT ON;

IF OBJECT_ID(
    N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2',
    N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
    (
        DiagnosticoIAAlbumPublicacionDiagnosticoId
            INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAId INT NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        DiagnosticoClave NVARCHAR(180) NOT NULL,
        DiagnosticoIdOrigenIA NVARCHAR(120) NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_origen DEFAULT(N''),
        OrdenDiagnostico INT NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_orden DEFAULT(1),
        EsPrincipal BIT NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_principal DEFAULT(0),
        DiagnosticoNombre NVARCHAR(300) NOT NULL,
        TerrenoId INT NULL,
        CodigoTerreno NVARCHAR(50) NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_codigoTerreno DEFAULT(N''),
        CategoriaAlbumBotanicoId INT NOT NULL,
        AlbumBotanicoCafeId INT NOT NULL,
        AlbumBotanicoCafeFotoId INT NOT NULL,
        DiagnosticoIAImagenAprobacionId INT NULL,
        ColorMarcador NVARCHAR(20) NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_color DEFAULT(N'#E53935'),
        LesionesJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_lesiones DEFAULT(N'[]'),
        AnchoImagen INT NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_ancho DEFAULT(0),
        AltoImagen INT NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_alto DEFAULT(0),
        DescripcionPublicacion NVARCHAR(1000) NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_descripcion DEFAULT(N''),
        UsuarioPublicacionId INT NOT NULL,
        FechaPublicacionUtc DATETIME2(0) NOT NULL,
        UsuarioRetiroId INT NULL,
        FechaRetiroUtc DATETIME2(0) NULL,
        Activo BIT NOT NULL
            CONSTRAINT DF_diagIAAlbumDiag_activo DEFAULT(1),
        CONSTRAINT PK_diagIAAlbumPublicacionDiagnosticoV2
            PRIMARY KEY
            (DiagnosticoIAAlbumPublicacionDiagnosticoId),
        CONSTRAINT FK_diagIAAlbumDiag_diagnostico
            FOREIGN KEY (DiagnosticoIAId)
            REFERENCES dbo.diagnosticoIA(DiagnosticoIAId),
        CONSTRAINT FK_diagIAAlbumDiag_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId),
        CONSTRAINT FK_diagIAAlbumDiag_categoria
            FOREIGN KEY (CategoriaAlbumBotanicoId)
            REFERENCES dbo.CategoriaAlbumBotanico(categoriaAlbumBotanicoId),
        CONSTRAINT FK_diagIAAlbumDiag_subcategoria
            FOREIGN KEY (AlbumBotanicoCafeId)
            REFERENCES dbo.AlbumBotanicoCafe(albumBotanicoCafeId),
        CONSTRAINT FK_diagIAAlbumDiag_fotoAlbum
            FOREIGN KEY (AlbumBotanicoCafeFotoId)
            REFERENCES dbo.AlbumBotanicoCafeFoto(albumBotanicoCafeFotoId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2')
      AND name = N'UX_diagIAAlbumDiag_fotoClaveActivo'
)
BEGIN
    CREATE UNIQUE INDEX UX_diagIAAlbumDiag_fotoClaveActivo
        ON dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
        (
            DiagnosticoIAImagenId,
            DiagnosticoClave
        )
        WHERE Activo = 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2')
      AND name = N'IX_diagIAAlbumDiag_albumActivo'
)
BEGIN
    CREATE INDEX IX_diagIAAlbumDiag_albumActivo
        ON dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
        (
            AlbumBotanicoCafeId,
            Activo,
            FechaPublicacionUtc DESC
        );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2')
      AND name = N'IX_diagIAAlbumDiag_albumFotoActivo'
)
BEGIN
    CREATE INDEX IX_diagIAAlbumDiag_albumFotoActivo
        ON dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
        (
            AlbumBotanicoCafeFotoId,
            Activo
        );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2')
      AND name = N'IX_diagIAAlbumDiag_terrenoDiagnostico'
)
BEGIN
    CREATE INDEX IX_diagIAAlbumDiag_terrenoDiagnostico
        ON dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
        (
            TerrenoId,
            DiagnosticoNombre,
            Activo,
            FechaPublicacionUtc DESC
        );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2')
      AND name = N'IX_diagIAAlbumDiag_inspeccionFoto'
)
BEGIN
    CREATE INDEX IX_diagIAAlbumDiag_inspeccionFoto
        ON dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
        (
            DiagnosticoIAId,
            DiagnosticoIAImagenId,
            Activo
        );
END;
""";

                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);
                inicializada = true;
            }
            catch
            {
                inicializada = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        public async Task<List<PublicacionDiagnosticoRegistro>>
            ObtenerPorFotografiaAsync(
                int fotografiaId,
                bool incluirInactivas,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string filtroActivo = incluirInactivas
                ? string.Empty
                : "AND Activo = 1";

            string sql = $"""
SELECT
    DiagnosticoIAAlbumPublicacionDiagnosticoId,
    DiagnosticoIAId,
    DiagnosticoIAImagenId,
    DiagnosticoClave,
    DiagnosticoIdOrigenIA,
    OrdenDiagnostico,
    EsPrincipal,
    DiagnosticoNombre,
    TerrenoId,
    CodigoTerreno,
    CategoriaAlbumBotanicoId,
    AlbumBotanicoCafeId,
    AlbumBotanicoCafeFotoId,
    DiagnosticoIAImagenAprobacionId,
    ColorMarcador,
    LesionesJson,
    AnchoImagen,
    AltoImagen,
    DescripcionPublicacion,
    UsuarioPublicacionId,
    FechaPublicacionUtc,
    UsuarioRetiroId,
    FechaRetiroUtc,
    Activo
FROM dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
WHERE DiagnosticoIAImagenId = @fotoId
  {filtroActivo}
ORDER BY
    Activo DESC,
    FechaPublicacionUtc DESC,
    DiagnosticoIAAlbumPublicacionDiagnosticoId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);

            var resultado = new List<PublicacionDiagnosticoRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(Leer(reader));

            return resultado;
        }

        public async Task<List<PublicacionDiagnosticoRegistro>>
            ObtenerPorAlbumAsync(
                int albumBotanicoCafeId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    DiagnosticoIAAlbumPublicacionDiagnosticoId,
    DiagnosticoIAId,
    DiagnosticoIAImagenId,
    DiagnosticoClave,
    DiagnosticoIdOrigenIA,
    OrdenDiagnostico,
    EsPrincipal,
    DiagnosticoNombre,
    TerrenoId,
    CodigoTerreno,
    CategoriaAlbumBotanicoId,
    AlbumBotanicoCafeId,
    AlbumBotanicoCafeFotoId,
    DiagnosticoIAImagenAprobacionId,
    ColorMarcador,
    LesionesJson,
    AnchoImagen,
    AltoImagen,
    DescripcionPublicacion,
    UsuarioPublicacionId,
    FechaPublicacionUtc,
    UsuarioRetiroId,
    FechaRetiroUtc,
    Activo
FROM dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
WHERE AlbumBotanicoCafeId = @albumId
  AND Activo = 1
ORDER BY
    AlbumBotanicoCafeFotoId,
    CASE WHEN EsPrincipal = 1 THEN 0 ELSE 1 END,
    OrdenDiagnostico;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@albumId", albumBotanicoCafeId);
            await AbrirAsync(comando.Connection!, cancellationToken);

            var resultado = new List<PublicacionDiagnosticoRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(Leer(reader));

            return resultado;
        }

        public async Task<int> InsertarAsync(
            NuevaPublicacionDiagnosticoRegistro registro,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
INSERT INTO dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
(
    DiagnosticoIAId,
    DiagnosticoIAImagenId,
    DiagnosticoClave,
    DiagnosticoIdOrigenIA,
    OrdenDiagnostico,
    EsPrincipal,
    DiagnosticoNombre,
    TerrenoId,
    CodigoTerreno,
    CategoriaAlbumBotanicoId,
    AlbumBotanicoCafeId,
    AlbumBotanicoCafeFotoId,
    DiagnosticoIAImagenAprobacionId,
    ColorMarcador,
    LesionesJson,
    AnchoImagen,
    AltoImagen,
    DescripcionPublicacion,
    UsuarioPublicacionId,
    FechaPublicacionUtc,
    Activo
)
VALUES
(
    @inspeccionId,
    @fotoId,
    @clave,
    @origen,
    @orden,
    @principal,
    @diagnostico,
    @terrenoId,
    @codigoTerreno,
    @categoriaId,
    @albumId,
    @albumFotoId,
    @aprobacionId,
    @color,
    @lesiones,
    @ancho,
    @alto,
    @descripcion,
    @usuarioId,
    SYSUTCDATETIME(),
    1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@inspeccionId", registro.InspeccionId);
            AgregarParametro(comando, "@fotoId", registro.FotografiaId);
            AgregarParametro(comando, "@clave", Limitar(registro.DiagnosticoClave, 180));
            AgregarParametro(comando, "@origen", Limitar(registro.DiagnosticoIdOrigenIA, 120));
            AgregarParametro(comando, "@orden", registro.OrdenDiagnostico);
            AgregarParametro(comando, "@principal", registro.EsPrincipal);
            AgregarParametro(comando, "@diagnostico", Limitar(registro.Diagnostico, 300));
            AgregarParametro(comando, "@terrenoId", registro.TerrenoId);
            AgregarParametro(comando, "@codigoTerreno", Limitar(registro.CodigoTerreno, 50));
            AgregarParametro(comando, "@categoriaId", registro.CategoriaAlbumBotanicoId);
            AgregarParametro(comando, "@albumId", registro.AlbumBotanicoCafeId);
            AgregarParametro(comando, "@albumFotoId", registro.AlbumBotanicoCafeFotoId);
            AgregarParametro(comando, "@aprobacionId", registro.AprobacionId);
            AgregarParametro(comando, "@color", Limitar(registro.ColorMarcador, 20));
            AgregarParametro(comando, "@lesiones", string.IsNullOrWhiteSpace(registro.LesionesJson) ? "[]" : registro.LesionesJson);
            AgregarParametro(comando, "@ancho", Math.Max(0, registro.AnchoImagen));
            AgregarParametro(comando, "@alto", Math.Max(0, registro.AltoImagen));
            AgregarParametro(comando, "@descripcion", Limitar(registro.Descripcion, 1000));
            AgregarParametro(comando, "@usuarioId", registro.UsuarioId);

            await AbrirAsync(comando.Connection!, cancellationToken);
            object? valor = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(valor ?? 0);
        }

        public async Task RetirarAsync(
            int publicacionId,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
SET
    Activo = 0,
    UsuarioRetiroId = @usuarioId,
    FechaRetiroUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAAlbumPublicacionDiagnosticoId = @id
  AND Activo = 1;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@id", publicacionId);
            AgregarParametro(comando, "@usuarioId", usuarioId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> ExisteOtraActivaParaFotoAlbumAsync(
            int albumBotanicoCafeFotoId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAAlbumPublicacionDiagnosticoV2
    WHERE AlbumBotanicoCafeFotoId = @albumFotoId
      AND Activo = 1
)
THEN 1 ELSE 0 END;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@albumFotoId", albumBotanicoCafeFotoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            object? valor = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(valor ?? 0) == 1;
        }

        private DbCommand CrearComando(string sql)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            DbCommand comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 180;
            comando.Transaction =
                db.Database.CurrentTransaction?.GetDbTransaction();
            return comando;
        }

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor)
        {
            DbParameter parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }

        private static async Task AbrirAsync(
            DbConnection conexion,
            CancellationToken cancellationToken)
        {
            if (conexion.State != ConnectionState.Open)
                await conexion.OpenAsync(cancellationToken);
        }

        private static PublicacionDiagnosticoRegistro Leer(
            DbDataReader reader) =>
            new()
            {
                Id = reader.GetInt32(0),
                InspeccionId = reader.GetInt32(1),
                FotografiaId = reader.GetInt32(2),
                DiagnosticoClave = reader.GetString(3),
                DiagnosticoIdOrigenIA = reader.GetString(4),
                OrdenDiagnostico = reader.GetInt32(5),
                EsPrincipal = reader.GetBoolean(6),
                Diagnostico = reader.GetString(7),
                TerrenoId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                CodigoTerreno = reader.GetString(9),
                CategoriaAlbumBotanicoId = reader.GetInt32(10),
                AlbumBotanicoCafeId = reader.GetInt32(11),
                AlbumBotanicoCafeFotoId = reader.GetInt32(12),
                AprobacionId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                ColorMarcador = reader.GetString(14),
                LesionesJson = reader.GetString(15),
                AnchoImagen = reader.GetInt32(16),
                AltoImagen = reader.GetInt32(17),
                Descripcion = reader.GetString(18),
                UsuarioPublicacionId = reader.GetInt32(19),
                FechaPublicacionUtc = reader.GetDateTime(20),
                UsuarioRetiroId = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                FechaRetiroUtc = reader.IsDBNull(22) ? null : reader.GetDateTime(22),
                Activo = reader.GetBoolean(23)
            };

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }

    public sealed class PublicacionDiagnosticoRegistro
    {
        public int Id { get; set; }
        public int InspeccionId { get; set; }
        public int FotografiaId { get; set; }
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string DiagnosticoIdOrigenIA { get; set; } = string.Empty;
        public int OrdenDiagnostico { get; set; }
        public bool EsPrincipal { get; set; }
        public string Diagnostico { get; set; } = string.Empty;
        public int? TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public int CategoriaAlbumBotanicoId { get; set; }
        public int AlbumBotanicoCafeId { get; set; }
        public int AlbumBotanicoCafeFotoId { get; set; }
        public int? AprobacionId { get; set; }
        public string ColorMarcador { get; set; } = "#E53935";
        public string LesionesJson { get; set; } = "[]";
        public int AnchoImagen { get; set; }
        public int AltoImagen { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public int UsuarioPublicacionId { get; set; }
        public DateTime FechaPublicacionUtc { get; set; }
        public int? UsuarioRetiroId { get; set; }
        public DateTime? FechaRetiroUtc { get; set; }
        public bool Activo { get; set; }
    }

    public sealed class NuevaPublicacionDiagnosticoRegistro
    {
        public int InspeccionId { get; set; }
        public int FotografiaId { get; set; }
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string DiagnosticoIdOrigenIA { get; set; } = string.Empty;
        public int OrdenDiagnostico { get; set; }
        public bool EsPrincipal { get; set; }
        public string Diagnostico { get; set; } = string.Empty;
        public int? TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public int CategoriaAlbumBotanicoId { get; set; }
        public int AlbumBotanicoCafeId { get; set; }
        public int AlbumBotanicoCafeFotoId { get; set; }
        public int? AprobacionId { get; set; }
        public string ColorMarcador { get; set; } = "#E53935";
        public string LesionesJson { get; set; } = "[]";
        public int AnchoImagen { get; set; }
        public int AltoImagen { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
    }
}
