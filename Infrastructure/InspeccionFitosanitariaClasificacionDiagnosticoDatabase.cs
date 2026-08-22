using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Persistencia aditiva de clasificaciones del Álbum por diagnóstico
    /// individual dentro de una fotografía fitosanitaria.
    ///
    /// No sustituye diagnosticoIAClasificacionJerarquia, que se conserva como
    /// compatibilidad histórica para la clasificación principal por fotografía.
    /// </summary>
    public sealed class InspeccionFitosanitariaClasificacionDiagnosticoDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly DiagnosticoIADbContext db;
        private readonly AlbumJerarquiaDbContext albumDb;
        private readonly InspeccionFitosanitariaDatabase flujo;

        public InspeccionFitosanitariaClasificacionDiagnosticoDatabase(
            DiagnosticoIADbContext db,
            AlbumJerarquiaDbContext albumDb)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.albumDb = albumDb ??
                throw new ArgumentNullException(nameof(albumDb));
            flujo = new InspeccionFitosanitariaDatabase(db);
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
    N'dbo.diagnosticoIAImagenClasificacionDiagnostico',
    N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenClasificacionDiagnostico
    (
        DiagnosticoIAImagenClasificacionDiagnosticoId
            INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        DiagnosticoClave NVARCHAR(180) NOT NULL,
        DiagnosticoIdOrigenIA NVARCHAR(120) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_origen DEFAULT(N''),
        OrdenDiagnostico INT NOT NULL
            CONSTRAINT DF_diagIAClasDiag_orden DEFAULT(1),
        EsPrincipal BIT NOT NULL
            CONSTRAINT DF_diagIAClasDiag_principal DEFAULT(0),
        DiagnosticoNombre NVARCHAR(300) NOT NULL,
        CategoriaIA NVARCHAR(100) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_categoriaIA DEFAULT(N''),
        TipoDiagnosticoIA NVARCHAR(100) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_tipoIA DEFAULT(N''),

        CategoriaAlbumBotanicoIdSugerida INT NULL,
        AlbumBotanicoCafeIdSugerido INT NULL,
        CategoriaSugerida NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_catSug DEFAULT(N''),
        SubcategoriaSugerida NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_subSug DEFAULT(N''),
        NombreCientificoSugerido NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_cientifico DEFAULT(N''),
        CoincideCatalogo BIT NOT NULL
            CONSTRAINT DF_diagIAClasDiag_coincide DEFAULT(0),
        RequiereDecision BIT NOT NULL
            CONSTRAINT DF_diagIAClasDiag_decision DEFAULT(1),

        CategoriaAlbumBotanicoIdSeleccionada INT NULL,
        AlbumBotanicoCafeIdSeleccionado INT NULL,
        CategoriaSeleccionada NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_catSel DEFAULT(N''),
        SubcategoriaSeleccionada NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_subSel DEFAULT(N''),

        AccionHumana NVARCHAR(30) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_accion DEFAULT(N''),
        Estado NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_estado DEFAULT(N'SUGERIDA_IA'),
        FuenteVigente NVARCHAR(30) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_fuente DEFAULT(N'IA'),
        UsuarioActualizacionId INT NULL,
        FechaActualizacionUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_diagIAClasDiag_fecha DEFAULT(SYSUTCDATETIME()),
        Activo BIT NOT NULL
            CONSTRAINT DF_diagIAClasDiag_activo DEFAULT(1),

        CONSTRAINT PK_diagIAImagenClasificacionDiagnostico
            PRIMARY KEY
            (DiagnosticoIAImagenClasificacionDiagnosticoId),

        CONSTRAINT FK_diagIAClasDiag_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen
                (DiagnosticoIAImagenId)
            ON DELETE CASCADE
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
        OBJECT_ID(N'dbo.diagnosticoIAImagenClasificacionDiagnostico')
      AND name = N'UX_diagIAClasDiag_fotoClave'
)
BEGIN
    CREATE UNIQUE INDEX UX_diagIAClasDiag_fotoClave
        ON dbo.diagnosticoIAImagenClasificacionDiagnostico
        (
            DiagnosticoIAImagenId,
            DiagnosticoClave
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
        OBJECT_ID(N'dbo.diagnosticoIAImagenClasificacionDiagnostico')
      AND name = N'IX_diagIAClasDiag_fotoActivo'
)
BEGIN
    CREATE INDEX IX_diagIAClasDiag_fotoActivo
        ON dbo.diagnosticoIAImagenClasificacionDiagnostico
        (
            DiagnosticoIAImagenId,
            Activo,
            OrdenDiagnostico
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

        /// <summary>
        /// Sincroniza la clasificación persistida con la fuente vigente del
        /// expediente. Prioridad:
        /// APROBACION -> ANALIZADOR -> IA.
        /// </summary>
        public async Task<List<ClasificacionDiagnosticoFitosanitarioRegistro>>
            SincronizarYObtenerAsync(
                int inspeccionId,
                int? usuarioId = null,
                CancellationToken cancellationToken = default)
        {
            if (inspeccionId <= 0)
                return [];

            await InicializarAsync(cancellationToken);
            await flujo.InicializarAsync(cancellationToken);

            List<FotoMetadatos> fotos =
                (await flujo.ObtenerFotosAsync(
                    inspeccionId,
                    cancellationToken))
                .Where(item => item.Activo && !item.Descartada)
                .ToList();

            if (fotos.Count == 0)
                return [];

            Dictionary<int, ResultadoVisualRegistro> visuales =
                await flujo.ObtenerResultadosVisualesVigentesAsync(
                    inspeccionId,
                    cancellationToken);

            Dictionary<int, AnalisisHumanoRegistro> humanos =
                await flujo.ObtenerUltimosAnalisisHumanosAsync(
                    inspeccionId,
                    cancellationToken);

            Dictionary<int, AprobacionRegistro> aprobaciones =
                await flujo.ObtenerUltimasAprobacionesAsync(
                    inspeccionId,
                    cancellationToken);

            List<AlbumBotanicoCafeJerarquia> catalogo =
                await albumDb.Subcategorias
                    .AsNoTracking()
                    .Include(item => item.Categoria)
                    .Where(item =>
                        item.Activo &&
                        item.Categoria.Activo)
                    .OrderBy(item => item.Categoria.NombreCategoria)
                    .ThenBy(item => item.Titulo)
                    .ToListAsync(cancellationToken);

            int[] idsFotos = fotos
                .Select(item => item.FotografiaId)
                .Distinct()
                .ToArray();

            Dictionary<int, DiagnosticoIAClasificacionJerarquia> historicas =
                await albumDb.ClasificacionesJerarquia
                    .AsNoTracking()
                    .Where(item =>
                        idsFotos.Contains(item.DiagnosticoIAImagenId))
                    .ToDictionaryAsync(
                        item => item.DiagnosticoIAImagenId,
                        cancellationToken);

            Dictionary<(int FotoId, string Clave),
                ClasificacionDiagnosticoFitosanitarioRegistro> existentes =
                await ObtenerExistentesAsync(
                    idsFotos,
                    cancellationToken);

            var activas = new HashSet<(int FotoId, string Clave)>();

            foreach (FotoMetadatos foto in fotos)
            {
                (string json, string fuente) = ResolverFuente(
                    foto.FotografiaId,
                    visuales,
                    humanos,
                    aprobaciones);

                List<DiagnosticoPersistenciaDto> diagnosticos =
                    DeserializarDiagnosticos(json)
                        .Where(item => !string.Equals(
                            item.AccionHumana,
                            "DESCARTAR",
                            StringComparison.OrdinalIgnoreCase))
                        .Take(8)
                        .ToList();

                if (diagnosticos.Count == 0)
                    continue;

                bool existePrincipal =
                    diagnosticos.Any(item => item.EsPrincipal);

                for (int indice = 0;
                     indice < diagnosticos.Count;
                     indice++)
                {
                    DiagnosticoPersistenciaDto diagnostico =
                        diagnosticos[indice];

                    bool esPrincipal =
                        diagnostico.EsPrincipal ||
                        (!existePrincipal && indice == 0);

                    string clave = CrearClaveDiagnostico(
                        diagnostico,
                        indice);

                    if (!activas.Add((foto.FotografiaId, clave)))
                    {
                        clave = Limitar(
                            $"{clave}:{indice + 1}",
                            180);
                        activas.Add((foto.FotografiaId, clave));
                    }

                    string categoria =
                        MapearCategoriaDiagnostico(
                            diagnostico);

                    string nombreDiagnostico =
                        LimpiarNombreDiagnostico(
                            diagnostico.Diagnostico);

                    AlbumBotanicoCafeJerarquia? coincidencia =
                        BuscarCoincidenciaCatalogo(
                            catalogo,
                            categoria,
                            nombreDiagnostico);

                    DiagnosticoIAClasificacionJerarquia?
                        historica = null;

                    if (esPrincipal &&
                        historicas.TryGetValue(
                            foto.FotografiaId,
                            out DiagnosticoIAClasificacionJerarquia?
                                valorHistorico) &&
                        EsCompatibleConHistorica(
                            valorHistorico,
                            nombreDiagnostico))
                    {
                        historica = valorHistorico;
                    }

                    existentes.TryGetValue(
                        (foto.FotografiaId, clave),
                        out ClasificacionDiagnosticoFitosanitarioRegistro?
                            existente);

                    ClasificacionDiagnosticoFitosanitarioRegistro registro =
                        ConstruirRegistro(
                            foto.FotografiaId,
                            clave,
                            diagnostico,
                            indice + 1,
                            esPrincipal,
                            categoria,
                            nombreDiagnostico,
                            coincidencia,
                            historica,
                            existente,
                            fuente,
                            usuarioId);

                    await GuardarRegistroAsync(
                        registro,
                        cancellationToken);
                }
            }

            await DesactivarObsoletasAsync(
                idsFotos,
                activas,
                usuarioId,
                cancellationToken);

            return await ObtenerPorInspeccionAsync(
                inspeccionId,
                cancellationToken);
        }

        public async Task<List<ClasificacionDiagnosticoFitosanitarioRegistro>>
            ObtenerPorInspeccionAsync(
                int inspeccionId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    c.DiagnosticoIAImagenClasificacionDiagnosticoId,
    c.DiagnosticoIAImagenId,
    c.DiagnosticoClave,
    c.DiagnosticoIdOrigenIA,
    c.OrdenDiagnostico,
    c.EsPrincipal,
    c.DiagnosticoNombre,
    c.CategoriaIA,
    c.TipoDiagnosticoIA,
    c.CategoriaAlbumBotanicoIdSugerida,
    c.AlbumBotanicoCafeIdSugerido,
    c.CategoriaSugerida,
    c.SubcategoriaSugerida,
    c.NombreCientificoSugerido,
    c.CoincideCatalogo,
    c.RequiereDecision,
    c.CategoriaAlbumBotanicoIdSeleccionada,
    c.AlbumBotanicoCafeIdSeleccionado,
    c.CategoriaSeleccionada,
    c.SubcategoriaSeleccionada,
    c.AccionHumana,
    c.Estado,
    c.FuenteVigente,
    c.UsuarioActualizacionId,
    c.FechaActualizacionUtc,
    c.Activo
FROM dbo.diagnosticoIAImagenClasificacionDiagnostico c
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = c.DiagnosticoIAImagenId
WHERE i.DiagnosticoIAId = @inspeccionId
  AND c.Activo = 1
ORDER BY
    i.Orden,
    CASE WHEN c.EsPrincipal = 1 THEN 0 ELSE 1 END,
    c.OrdenDiagnostico,
    c.DiagnosticoIAImagenClasificacionDiagnosticoId;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(
                comando,
                "@inspeccionId",
                inspeccionId);

            await AbrirAsync(
                comando.Connection!,
                cancellationToken);

            var resultado =
                new List<ClasificacionDiagnosticoFitosanitarioRegistro>();

            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(LeerRegistro(reader));

            return resultado;
        }

        public async Task<bool> ResolverAsync(
            int inspeccionId,
            int fotografiaId,
            ResolverClasificacionDiagnosticoFitosanitarioRequest request,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (inspeccionId <= 0 ||
                fotografiaId <= 0 ||
                string.IsNullOrWhiteSpace(request.DiagnosticoClave))
            {
                return false;
            }

            await SincronizarYObtenerAsync(
                inspeccionId,
                usuarioId,
                cancellationToken);

            const string sql = """
UPDATE c
SET
    CategoriaAlbumBotanicoIdSeleccionada =
        @categoriaId,
    AlbumBotanicoCafeIdSeleccionado =
        @subcategoriaId,
    CategoriaSeleccionada =
        @categoria,
    SubcategoriaSeleccionada =
        @subcategoria,
    AccionHumana =
        @accion,
    Estado =
        @estado,
    RequiereDecision =
        @requiereDecision,
    UsuarioActualizacionId =
        @usuarioId,
    FechaActualizacionUtc =
        SYSUTCDATETIME()
FROM dbo.diagnosticoIAImagenClasificacionDiagnostico c
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = c.DiagnosticoIAImagenId
WHERE i.DiagnosticoIAId = @inspeccionId
  AND c.DiagnosticoIAImagenId = @fotoId
  AND c.DiagnosticoClave = @clave
  AND c.Activo = 1;

SELECT @@ROWCOUNT;
""";

            string accion = NormalizarAccion(request.Accion);
            bool descartar = accion == "DESCARTAR";

            string estado = descartar
                ? "DESCARTADA_DIAGNOSTICO"
                : NormalizarEtapa(request.Etapa) switch
                {
                    "APROBADOR" => "RESUELTA_APROBADOR",
                    "ANALIZADOR" => "RESUELTA_ANALIZADOR",
                    _ => "RESUELTA_TECNICO"
                };

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(
                comando,
                "@inspeccionId",
                inspeccionId);
            AgregarParametro(
                comando,
                "@fotoId",
                fotografiaId);
            AgregarParametro(
                comando,
                "@clave",
                Limitar(request.DiagnosticoClave, 180));
            AgregarParametro(
                comando,
                "@categoriaId",
                descartar
                    ? null
                    : request.CategoriaAlbumBotanicoId);
            AgregarParametro(
                comando,
                "@subcategoriaId",
                descartar
                    ? null
                    : request.AlbumBotanicoCafeId);
            AgregarParametro(
                comando,
                "@categoria",
                descartar
                    ? string.Empty
                    : Limitar(request.Categoria, 150));
            AgregarParametro(
                comando,
                "@subcategoria",
                descartar
                    ? string.Empty
                    : Limitar(request.Subcategoria, 200));
            AgregarParametro(
                comando,
                "@accion",
                accion);
            AgregarParametro(
                comando,
                "@estado",
                estado);
            AgregarParametro(
                comando,
                "@requiereDecision",
                false);
            AgregarParametro(
                comando,
                "@usuarioId",
                usuarioId);

            await AbrirAsync(
                comando.Connection!,
                cancellationToken);

            object? valor =
                await comando.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt32(valor ?? 0) > 0;
        }

        private static (string Json, string Fuente) ResolverFuente(
            int fotografiaId,
            IReadOnlyDictionary<int, ResultadoVisualRegistro> visuales,
            IReadOnlyDictionary<int, AnalisisHumanoRegistro> humanos,
            IReadOnlyDictionary<int, AprobacionRegistro> aprobaciones)
        {
            if (aprobaciones.TryGetValue(
                    fotografiaId,
                    out AprobacionRegistro? aprobacion) &&
                TieneDiagnosticos(aprobacion.DiagnosticosFinalesJson))
            {
                return (
                    aprobacion.DiagnosticosFinalesJson,
                    "APROBACION");
            }

            if (humanos.TryGetValue(
                    fotografiaId,
                    out AnalisisHumanoRegistro? humano) &&
                TieneDiagnosticos(humano.DiagnosticosJson))
            {
                return (
                    humano.DiagnosticosJson,
                    "ANALIZADOR");
            }

            if (visuales.TryGetValue(
                    fotografiaId,
                    out ResultadoVisualRegistro? visual) &&
                TieneDiagnosticos(visual.DiagnosticosJson))
            {
                return (
                    visual.DiagnosticosJson,
                    "IA");
            }

            return ("[]", "IA");
        }

        private ClasificacionDiagnosticoFitosanitarioRegistro ConstruirRegistro(
            int fotografiaId,
            string clave,
            DiagnosticoPersistenciaDto diagnostico,
            int orden,
            bool esPrincipal,
            string categoria,
            string nombreDiagnostico,
            AlbumBotanicoCafeJerarquia? coincidencia,
            DiagnosticoIAClasificacionJerarquia? historica,
            ClasificacionDiagnosticoFitosanitarioRegistro? existente,
            string fuente,
            int? usuarioId)
        {
            int? categoriaIdSugerida =
                coincidencia?.CategoriaAlbumBotanicoId;

            int? subcategoriaIdSugerida =
                coincidencia?.AlbumBotanicoCafeId;

            string categoriaSugerida =
                coincidencia?.Categoria?.NombreCategoria ??
                categoria;

            string subcategoriaSugerida =
                coincidencia?.Titulo ??
                nombreDiagnostico;

            string cientifico =
                coincidencia?.NombreCientifico ??
                string.Empty;

            bool coincide = coincidencia != null;
            bool requiereDecision = !coincide;

            int? categoriaSeleccionada =
                existente?.CategoriaAlbumBotanicoIdSeleccionada;

            int? subcategoriaSeleccionada =
                existente?.AlbumBotanicoCafeIdSeleccionado;

            string categoriaSeleccionadaTexto =
                existente?.CategoriaSeleccionada ??
                string.Empty;

            string subcategoriaSeleccionadaTexto =
                existente?.SubcategoriaSeleccionada ??
                string.Empty;

            string accionHumana =
                existente?.AccionHumana ??
                diagnostico.AccionHumana ??
                string.Empty;

            string estado =
                existente?.Estado ??
                "SUGERIDA_IA";

            if (existente == null &&
                historica != null)
            {
                categoriaIdSugerida =
                    historica.CategoriaAlbumBotanicoIdSugerida ??
                    categoriaIdSugerida;

                subcategoriaIdSugerida =
                    historica.AlbumBotanicoCafeIdSugerido ??
                    subcategoriaIdSugerida;

                categoriaSugerida =
                    PrimerTexto(
                        historica.CategoriaSugerida,
                        categoriaSugerida);

                subcategoriaSugerida =
                    PrimerTexto(
                        historica.SubcategoriaSugerida,
                        subcategoriaSugerida);

                cientifico =
                    PrimerTexto(
                        historica.NombreCientificoSugerido,
                        cientifico);

                categoriaSeleccionada =
                    historica.CategoriaAlbumBotanicoIdSeleccionada;

                subcategoriaSeleccionada =
                    historica.AlbumBotanicoCafeIdSeleccionado;

                categoriaSeleccionadaTexto =
                    historica.CategoriaSeleccionada ??
                    string.Empty;

                subcategoriaSeleccionadaTexto =
                    historica.SubcategoriaSeleccionada ??
                    string.Empty;

                if (!string.IsNullOrWhiteSpace(historica.Estado))
                    estado = historica.Estado;

                coincide =
                    subcategoriaIdSugerida is > 0 ||
                    subcategoriaSeleccionada is > 0;

                requiereDecision =
                    subcategoriaSeleccionada is not > 0 &&
                    !coincide;
            }

            if (existente != null &&
                existente.Estado is
                    "RESUELTA_ANALIZADOR" or
                    "RESUELTA_APROBADOR" or
                    "DESCARTADA_DIAGNOSTICO")
            {
                requiereDecision = false;
            }

            return new ClasificacionDiagnosticoFitosanitarioRegistro
            {
                Id =
                    existente?.Id ?? 0,
                FotografiaId =
                    fotografiaId,
                DiagnosticoClave =
                    clave,
                DiagnosticoIdOrigenIA =
                    PrimerTexto(
                        diagnostico.IdOrigenIA,
                        diagnostico.Id),
                OrdenDiagnostico =
                    orden,
                EsPrincipal =
                    esPrincipal,
                Diagnostico =
                    Limitar(diagnostico.Diagnostico, 300),
                CategoriaIA =
                    Limitar(diagnostico.Categoria, 100),
                TipoDiagnosticoIA =
                    Limitar(diagnostico.TipoDiagnostico, 100),

                CategoriaAlbumBotanicoIdSugerida =
                    categoriaIdSugerida,
                AlbumBotanicoCafeIdSugerido =
                    subcategoriaIdSugerida,
                CategoriaSugerida =
                    Limitar(categoriaSugerida, 150),
                SubcategoriaSugerida =
                    Limitar(subcategoriaSugerida, 200),
                NombreCientificoSugerido =
                    Limitar(cientifico, 200),
                CoincideCatalogo =
                    coincide,
                RequiereDecision =
                    requiereDecision,

                CategoriaAlbumBotanicoIdSeleccionada =
                    categoriaSeleccionada,
                AlbumBotanicoCafeIdSeleccionado =
                    subcategoriaSeleccionada,
                CategoriaSeleccionada =
                    Limitar(categoriaSeleccionadaTexto, 150),
                SubcategoriaSeleccionada =
                    Limitar(subcategoriaSeleccionadaTexto, 200),

                AccionHumana =
                    Limitar(accionHumana, 30),
                Estado =
                    Limitar(estado, 40),
                FuenteVigente =
                    Limitar(fuente, 30),
                UsuarioActualizacionId =
                    usuarioId,
                FechaActualizacionUtc =
                    DateTime.UtcNow,
                Activo =
                    true
            };
        }

        private async Task GuardarRegistroAsync(
            ClasificacionDiagnosticoFitosanitarioRegistro registro,
            CancellationToken cancellationToken)
        {
            const string sql = """
MERGE dbo.diagnosticoIAImagenClasificacionDiagnostico AS destino
USING
(
    SELECT
        @fotoId AS DiagnosticoIAImagenId,
        @clave AS DiagnosticoClave
) AS origen
ON destino.DiagnosticoIAImagenId =
       origen.DiagnosticoIAImagenId
AND destino.DiagnosticoClave =
       origen.DiagnosticoClave

WHEN MATCHED THEN
    UPDATE SET
        DiagnosticoIdOrigenIA = @origenIA,
        OrdenDiagnostico = @orden,
        EsPrincipal = @principal,
        DiagnosticoNombre = @diagnostico,
        CategoriaIA = @categoriaIA,
        TipoDiagnosticoIA = @tipoIA,

        CategoriaAlbumBotanicoIdSugerida =
            @categoriaSugId,
        AlbumBotanicoCafeIdSugerido =
            @subcategoriaSugId,
        CategoriaSugerida =
            @categoriaSug,
        SubcategoriaSugerida =
            @subcategoriaSug,
        NombreCientificoSugerido =
            @cientificoSug,
        CoincideCatalogo =
            @coincide,
        RequiereDecision =
            CASE
                WHEN destino.Estado IN
                    (
                        N'RESUELTA_ANALIZADOR',
                        N'RESUELTA_APROBADOR',
                        N'DESCARTADA_DIAGNOSTICO'
                    )
                    THEN destino.RequiereDecision
                ELSE @requiereDecision
            END,

        AccionHumana =
            CASE
                WHEN LEN(LTRIM(RTRIM(destino.AccionHumana))) > 0
                    THEN destino.AccionHumana
                ELSE @accionHumana
            END,
        FuenteVigente = @fuente,
        UsuarioActualizacionId =
            COALESCE(@usuarioId, destino.UsuarioActualizacionId),
        FechaActualizacionUtc =
            SYSUTCDATETIME(),
        Activo = 1

WHEN NOT MATCHED THEN
    INSERT
    (
        DiagnosticoIAImagenId,
        DiagnosticoClave,
        DiagnosticoIdOrigenIA,
        OrdenDiagnostico,
        EsPrincipal,
        DiagnosticoNombre,
        CategoriaIA,
        TipoDiagnosticoIA,
        CategoriaAlbumBotanicoIdSugerida,
        AlbumBotanicoCafeIdSugerido,
        CategoriaSugerida,
        SubcategoriaSugerida,
        NombreCientificoSugerido,
        CoincideCatalogo,
        RequiereDecision,
        CategoriaAlbumBotanicoIdSeleccionada,
        AlbumBotanicoCafeIdSeleccionado,
        CategoriaSeleccionada,
        SubcategoriaSeleccionada,
        AccionHumana,
        Estado,
        FuenteVigente,
        UsuarioActualizacionId,
        FechaActualizacionUtc,
        Activo
    )
    VALUES
    (
        @fotoId,
        @clave,
        @origenIA,
        @orden,
        @principal,
        @diagnostico,
        @categoriaIA,
        @tipoIA,
        @categoriaSugId,
        @subcategoriaSugId,
        @categoriaSug,
        @subcategoriaSug,
        @cientificoSug,
        @coincide,
        @requiereDecision,
        @categoriaSelId,
        @subcategoriaSelId,
        @categoriaSel,
        @subcategoriaSel,
        @accionHumana,
        @estado,
        @fuente,
        @usuarioId,
        SYSUTCDATETIME(),
        1
    );
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", registro.FotografiaId);
            AgregarParametro(comando, "@clave", registro.DiagnosticoClave);
            AgregarParametro(
                comando,
                "@origenIA",
                registro.DiagnosticoIdOrigenIA);
            AgregarParametro(
                comando,
                "@orden",
                registro.OrdenDiagnostico);
            AgregarParametro(
                comando,
                "@principal",
                registro.EsPrincipal);
            AgregarParametro(
                comando,
                "@diagnostico",
                registro.Diagnostico);
            AgregarParametro(
                comando,
                "@categoriaIA",
                registro.CategoriaIA);
            AgregarParametro(
                comando,
                "@tipoIA",
                registro.TipoDiagnosticoIA);
            AgregarParametro(
                comando,
                "@categoriaSugId",
                registro.CategoriaAlbumBotanicoIdSugerida);
            AgregarParametro(
                comando,
                "@subcategoriaSugId",
                registro.AlbumBotanicoCafeIdSugerido);
            AgregarParametro(
                comando,
                "@categoriaSug",
                registro.CategoriaSugerida);
            AgregarParametro(
                comando,
                "@subcategoriaSug",
                registro.SubcategoriaSugerida);
            AgregarParametro(
                comando,
                "@cientificoSug",
                registro.NombreCientificoSugerido);
            AgregarParametro(
                comando,
                "@coincide",
                registro.CoincideCatalogo);
            AgregarParametro(
                comando,
                "@requiereDecision",
                registro.RequiereDecision);
            AgregarParametro(
                comando,
                "@categoriaSelId",
                registro.CategoriaAlbumBotanicoIdSeleccionada);
            AgregarParametro(
                comando,
                "@subcategoriaSelId",
                registro.AlbumBotanicoCafeIdSeleccionado);
            AgregarParametro(
                comando,
                "@categoriaSel",
                registro.CategoriaSeleccionada);
            AgregarParametro(
                comando,
                "@subcategoriaSel",
                registro.SubcategoriaSeleccionada);
            AgregarParametro(
                comando,
                "@accionHumana",
                registro.AccionHumana);
            AgregarParametro(
                comando,
                "@estado",
                registro.Estado);
            AgregarParametro(
                comando,
                "@fuente",
                registro.FuenteVigente);
            AgregarParametro(
                comando,
                "@usuarioId",
                registro.UsuarioActualizacionId);

            await AbrirAsync(
                comando.Connection!,
                cancellationToken);

            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task DesactivarObsoletasAsync(
            IReadOnlyCollection<int> fotografiaIds,
            IReadOnlyCollection<(int FotoId, string Clave)> activas,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (fotografiaIds.Count == 0)
                return;

            Dictionary<(int FotoId, string Clave),
                ClasificacionDiagnosticoFitosanitarioRegistro> existentes =
                await ObtenerExistentesAsync(
                    fotografiaIds,
                    cancellationToken);

            foreach (KeyValuePair<
                         (int FotoId, string Clave),
                         ClasificacionDiagnosticoFitosanitarioRegistro> par
                     in existentes)
            {
                int fotoId = par.Key.FotoId;
                string clave = par.Key.Clave;
                ClasificacionDiagnosticoFitosanitarioRegistro registro =
                    par.Value;

                if (activas.Contains((fotoId, clave)) ||
                    !registro.Activo)
                {
                    continue;
                }

                const string sql = """
UPDATE dbo.diagnosticoIAImagenClasificacionDiagnostico
SET
    Activo = 0,
    UsuarioActualizacionId =
        COALESCE(@usuarioId, UsuarioActualizacionId),
    FechaActualizacionUtc =
        SYSUTCDATETIME()
WHERE DiagnosticoIAImagenId = @fotoId
  AND DiagnosticoClave = @clave;
""";

                await using DbCommand comando = CrearComando(sql);
                AgregarParametro(comando, "@fotoId", fotoId);
                AgregarParametro(comando, "@clave", clave);
                AgregarParametro(comando, "@usuarioId", usuarioId);

                await AbrirAsync(
                    comando.Connection!,
                    cancellationToken);

                await comando.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task<Dictionary<
            (int FotoId, string Clave),
            ClasificacionDiagnosticoFitosanitarioRegistro>>
            ObtenerExistentesAsync(
                IReadOnlyCollection<int> fotografiaIds,
                CancellationToken cancellationToken)
        {
            var resultado =
                new Dictionary<
                    (int FotoId, string Clave),
                    ClasificacionDiagnosticoFitosanitarioRegistro>();

            if (fotografiaIds.Count == 0)
                return resultado;

            string parametros = string.Join(
                ",",
                fotografiaIds.Select(
                    (_, indice) => $"@foto{indice}"));

            string sql = $"""
SELECT
    DiagnosticoIAImagenClasificacionDiagnosticoId,
    DiagnosticoIAImagenId,
    DiagnosticoClave,
    DiagnosticoIdOrigenIA,
    OrdenDiagnostico,
    EsPrincipal,
    DiagnosticoNombre,
    CategoriaIA,
    TipoDiagnosticoIA,
    CategoriaAlbumBotanicoIdSugerida,
    AlbumBotanicoCafeIdSugerido,
    CategoriaSugerida,
    SubcategoriaSugerida,
    NombreCientificoSugerido,
    CoincideCatalogo,
    RequiereDecision,
    CategoriaAlbumBotanicoIdSeleccionada,
    AlbumBotanicoCafeIdSeleccionado,
    CategoriaSeleccionada,
    SubcategoriaSeleccionada,
    AccionHumana,
    Estado,
    FuenteVigente,
    UsuarioActualizacionId,
    FechaActualizacionUtc,
    Activo
FROM dbo.diagnosticoIAImagenClasificacionDiagnostico
WHERE DiagnosticoIAImagenId IN ({parametros});
""";

            await using DbCommand comando = CrearComando(sql);

            int indice = 0;
            foreach (int fotografiaId in fotografiaIds)
            {
                AgregarParametro(
                    comando,
                    $"@foto{indice++}",
                    fotografiaId);
            }

            await AbrirAsync(
                comando.Connection!,
                cancellationToken);

            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                ClasificacionDiagnosticoFitosanitarioRegistro registro =
                    LeerRegistro(reader);

                resultado[
                    (registro.FotografiaId, registro.DiagnosticoClave)] =
                    registro;
            }

            return resultado;
        }

        private static List<DiagnosticoPersistenciaDto>
            DeserializarDiagnosticos(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<
                    List<DiagnosticoPersistenciaDto>>(
                        json,
                        JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static bool TieneDiagnosticos(string? json) =>
            DeserializarDiagnosticos(json).Count > 0;

        private static AlbumBotanicoCafeJerarquia?
            BuscarCoincidenciaCatalogo(
                IReadOnlyCollection<AlbumBotanicoCafeJerarquia> catalogo,
                string categoria,
                string diagnostico)
        {
            string categoriaClave = NormalizarClave(categoria);
            string diagnosticoClave =
                NormalizarClave(
                    LimpiarNombreDiagnostico(diagnostico));

            if (string.IsNullOrWhiteSpace(diagnosticoClave))
                return null;

            AlbumBotanicoCafeJerarquia? exacta =
                catalogo.FirstOrDefault(item =>
                    NormalizarClave(
                        item.Categoria.NombreCategoria) ==
                        categoriaClave &&
                    NormalizarClave(
                        LimpiarNombreDiagnostico(item.Titulo)) ==
                        diagnosticoClave);

            if (exacta != null)
                return exacta;

            List<AlbumBotanicoCafeJerarquia> unicas =
                catalogo
                    .Where(item =>
                        NormalizarClave(
                            LimpiarNombreDiagnostico(item.Titulo)) ==
                            diagnosticoClave)
                    .Take(2)
                    .ToList();

            return unicas.Count == 1
                ? unicas[0]
                : null;
        }

        private static string MapearCategoriaDiagnostico(
            DiagnosticoPersistenciaDto diagnostico)
        {
            string fuente = NormalizarClave(
                $"{diagnostico.Categoria} " +
                $"{diagnostico.TipoDiagnostico}");

            if (fuente.Contains("PLAGA", StringComparison.Ordinal) ||
                fuente.Contains("INSECT", StringComparison.Ordinal) ||
                fuente.Contains("ACARO", StringComparison.Ordinal))
            {
                return "Plagas";
            }

            if (fuente.Contains("ENFERMED", StringComparison.Ordinal) ||
                fuente.Contains("HONGO", StringComparison.Ordinal) ||
                fuente.Contains("FUNG", StringComparison.Ordinal) ||
                fuente.Contains("BACTER", StringComparison.Ordinal) ||
                fuente.Contains("VIRUS", StringComparison.Ordinal))
            {
                return "Enfermedades";
            }

            if (fuente.Contains("NUTRIC", StringComparison.Ordinal) ||
                fuente.Contains("DEFICI", StringComparison.Ordinal) ||
                fuente.Contains("ALTERACION", StringComparison.Ordinal))
            {
                return "Alteraciones nutricionales";
            }

            if (fuente.Contains("ESTRES", StringComparison.Ordinal) ||
                fuente.Contains("ABIOT", StringComparison.Ordinal))
            {
                return "Estrés abiótico";
            }

            if (fuente.Contains("MECAN", StringComparison.Ordinal))
                return "Daños mecánicos";

            return FormatearCodigo(
                PrimerTexto(
                    diagnostico.Categoria,
                    diagnostico.TipoDiagnostico,
                    "Clasificación pendiente"));
        }

        private static bool EsCompatibleConHistorica(
            DiagnosticoIAClasificacionJerarquia historica,
            string diagnostico)
        {
            string clave = NormalizarClave(
                LimpiarNombreDiagnostico(diagnostico));

            if (string.IsNullOrWhiteSpace(clave))
                return false;

            return new[]
                {
                    historica.SubcategoriaSeleccionada,
                    historica.SubcategoriaSugerida
                }
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Any(item =>
                    NormalizarClave(
                        LimpiarNombreDiagnostico(item)) ==
                    clave);
        }

        private static string CrearClaveDiagnostico(
            DiagnosticoPersistenciaDto diagnostico,
            int indice)
        {
            string fuente = PrimerTexto(
                diagnostico.IdOrigenIA,
                diagnostico.Id);

            if (!string.IsNullOrWhiteSpace(fuente))
            {
                return Limitar(
                    $"ID:{NormalizarClave(fuente)}",
                    180);
            }

            string nombre = NormalizarClave(
                diagnostico.Diagnostico);

            if (string.IsNullOrWhiteSpace(nombre))
                nombre = "SIN NOMBRE";

            return Limitar(
                $"N:{nombre}:{indice + 1}",
                180);
        }

        private DbCommand CrearComando(string sql)
        {
            DbConnection conexion =
                db.Database.GetDbConnection();

            DbCommand comando =
                conexion.CreateCommand();

            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 180;
            comando.Transaction =
                db.Database.CurrentTransaction?
                    .GetDbTransaction();

            return comando;
        }

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor)
        {
            DbParameter parametro =
                comando.CreateParameter();

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

        private static ClasificacionDiagnosticoFitosanitarioRegistro
            LeerRegistro(DbDataReader reader) =>
            new()
            {
                Id = reader.GetInt32(0),
                FotografiaId = reader.GetInt32(1),
                DiagnosticoClave = reader.GetString(2),
                DiagnosticoIdOrigenIA = reader.GetString(3),
                OrdenDiagnostico = reader.GetInt32(4),
                EsPrincipal = reader.GetBoolean(5),
                Diagnostico = reader.GetString(6),
                CategoriaIA = reader.GetString(7),
                TipoDiagnosticoIA = reader.GetString(8),
                CategoriaAlbumBotanicoIdSugerida =
                    reader.IsDBNull(9)
                        ? null
                        : reader.GetInt32(9),
                AlbumBotanicoCafeIdSugerido =
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetInt32(10),
                CategoriaSugerida = reader.GetString(11),
                SubcategoriaSugerida = reader.GetString(12),
                NombreCientificoSugerido = reader.GetString(13),
                CoincideCatalogo = reader.GetBoolean(14),
                RequiereDecision = reader.GetBoolean(15),
                CategoriaAlbumBotanicoIdSeleccionada =
                    reader.IsDBNull(16)
                        ? null
                        : reader.GetInt32(16),
                AlbumBotanicoCafeIdSeleccionado =
                    reader.IsDBNull(17)
                        ? null
                        : reader.GetInt32(17),
                CategoriaSeleccionada = reader.GetString(18),
                SubcategoriaSeleccionada = reader.GetString(19),
                AccionHumana = reader.GetString(20),
                Estado = reader.GetString(21),
                FuenteVigente = reader.GetString(22),
                UsuarioActualizacionId =
                    reader.IsDBNull(23)
                        ? null
                        : reader.GetInt32(23),
                FechaActualizacionUtc = reader.GetDateTime(24),
                Activo = reader.GetBoolean(25)
            };

        private static string NormalizarEtapa(string? valor)
        {
            string etapa = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return etapa switch
            {
                "APROBADOR" => "APROBADOR",
                "ANALIZADOR" => "ANALIZADOR",
                _ => "TECNICO"
            };
        }

        private static string NormalizarAccion(string? valor)
        {
            string accion = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return accion switch
            {
                "CORREGIR" => "CORREGIR",
                "DESCARTAR" => "DESCARTAR",
                "AGREGAR" => "AGREGAR",
                _ => "CONFIRMAR"
            };
        }

        private static string LimpiarNombreDiagnostico(
            string? valor)
        {
            string texto = (valor ?? string.Empty).Trim();

            int parentesis = texto.IndexOf('(');
            if (parentesis > 0)
                texto = texto[..parentesis].Trim();

            int separador = texto.IndexOf(
                " - ",
                StringComparison.Ordinal);

            if (separador > 0)
                texto = texto[..separador].Trim();

            return texto;
        }

        private static string FormatearCodigo(string? valor)
        {
            string texto = (valor ?? string.Empty)
                .Trim()
                .Replace('_', ' ');

            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            return CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(texto.ToLowerInvariant());
        }

        private static string NormalizarClave(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return string.Empty;

            string texto = valor
                .Trim()
                .ToUpperInvariant()
                .Normalize(NormalizationForm.FormD);

            var builder =
                new StringBuilder(texto.Length);

            bool espacioPendiente = false;

            foreach (char caracter in texto)
            {
                UnicodeCategory categoria =
                    CharUnicodeInfo.GetUnicodeCategory(
                        caracter);

                if (categoria ==
                    UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(caracter))
                {
                    if (espacioPendiente &&
                        builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(caracter);
                    espacioPendiente = false;
                }
                else if (builder.Length > 0)
                {
                    espacioPendiente = true;
                }
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Trim();
        }

        private static string PrimerTexto(
            params string?[] valores) =>
            valores.FirstOrDefault(valor =>
                !string.IsNullOrWhiteSpace(valor))?
                .Trim() ?? string.Empty;

        private static string Limitar(
            string? valor,
            int maximo)
        {
            string texto =
                (valor ?? string.Empty).Trim();

            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private sealed class DiagnosticoPersistenciaDto
        {
            public string Id { get; set; } = string.Empty;
            public string IdOrigenIA { get; set; } = string.Empty;
            public string AccionHumana { get; set; } = string.Empty;
            public string Diagnostico { get; set; } = string.Empty;
            public string Categoria { get; set; } = string.Empty;
            public string TipoDiagnostico { get; set; } = string.Empty;
            public bool EsPrincipal { get; set; }
        }
    }

    public sealed class ClasificacionDiagnosticoFitosanitarioRegistro
    {
        public int Id { get; set; }
        public int FotografiaId { get; set; }
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string DiagnosticoIdOrigenIA { get; set; } = string.Empty;
        public int OrdenDiagnostico { get; set; }
        public bool EsPrincipal { get; set; }
        public string Diagnostico { get; set; } = string.Empty;
        public string CategoriaIA { get; set; } = string.Empty;
        public string TipoDiagnosticoIA { get; set; } = string.Empty;

        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }
        public string CategoriaSugerida { get; set; } = string.Empty;
        public string SubcategoriaSugerida { get; set; } = string.Empty;
        public string NombreCientificoSugerido { get; set; } = string.Empty;
        public bool CoincideCatalogo { get; set; }
        public bool RequiereDecision { get; set; }

        public int? CategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? AlbumBotanicoCafeIdSeleccionado { get; set; }
        public string CategoriaSeleccionada { get; set; } = string.Empty;
        public string SubcategoriaSeleccionada { get; set; } = string.Empty;

        public string AccionHumana { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string FuenteVigente { get; set; } = string.Empty;
        public int? UsuarioActualizacionId { get; set; }
        public DateTime FechaActualizacionUtc { get; set; }
        public bool Activo { get; set; }

        public string Rol => EsPrincipal
            ? "Diagnóstico principal"
            : "Diagnóstico adicional";

        public string CategoriaMostrar =>
            !string.IsNullOrWhiteSpace(CategoriaSeleccionada)
                ? CategoriaSeleccionada
                : CategoriaSugerida;

        public string SubcategoriaMostrar =>
            !string.IsNullOrWhiteSpace(SubcategoriaSeleccionada)
                ? SubcategoriaSeleccionada
                : SubcategoriaSugerida;
    }

    public sealed class ResolverClasificacionDiagnosticoFitosanitarioRequest
    {
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string Etapa { get; set; } = "ANALIZADOR";
        public string Accion { get; set; } = "CONFIRMAR";
        public int? CategoriaAlbumBotanicoId { get; set; }
        public int? AlbumBotanicoCafeId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
    }
}
