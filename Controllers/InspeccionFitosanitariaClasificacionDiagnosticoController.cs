using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Expone la clasificación del Álbum por diagnóstico individual dentro del
    /// módulo de Inspección Fitosanitaria.
    ///
    /// Las rutas históricas de clasificación por fotografía permanecen
    /// intactas para compatibilidad con clientes anteriores.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class
        InspeccionFitosanitariaClasificacionDiagnosticoController :
        ControllerBase
    {
        private static readonly SemaphoreSlim HistorialInicializacionLock =
            new(1, 1);
        private static volatile bool historialInicializado;

        private readonly DiagnosticoIADbContext db;
        private readonly AlbumJerarquiaDbContext albumDb;
        private readonly PermisoApiService permisos;
        private readonly
            InspeccionFitosanitariaClasificacionDiagnosticoDatabase
            clasificaciones;

        public
            InspeccionFitosanitariaClasificacionDiagnosticoController(
                DiagnosticoIADbContext db,
                AlbumJerarquiaDbContext albumDb,
                PermisoApiService permisos)
        {
            this.db = db;
            this.albumDb = albumDb;
            this.permisos = permisos;

            clasificaciones =
                new InspeccionFitosanitariaClasificacionDiagnosticoDatabase(
                    db,
                    albumDb);
        }

        [HttpGet("{id:int}/clasificaciones-diagnosticos")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso =
                await ValidarLecturaModuloAsync(
                    usuarioId,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await db.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "No se encontró la inspección indicada."));
            }

            List<ClasificacionDiagnosticoFitosanitarioRegistro> data =
                await clasificaciones.SincronizarYObtenerAsync(
                    id,
                    usuarioId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = data.Count == 0
                    ? "La inspección todavía no contiene diagnósticos clasificables."
                    : "Clasificaciones por diagnóstico sincronizadas correctamente.",
                data
            });
        }

        /// <summary>
        /// Resuelve una clasificación por diagnóstico. El contrato V2 permite
        /// que el analizador proponga una subcategoría que todavía no existe.
        /// El aprobador puede convertir esa propuesta en una subcategoría
        /// oficial o seleccionar una existente.
        /// </summary>
        [HttpPost(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "clasificaciones-diagnosticos/resolver")]
        public async Task<IActionResult> Resolver(
            int id,
            int fotografiaId,
            [FromBody]
                ResolverClasificacionDiagnosticoFitosanitarioV2Request request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            if (request == null ||
                string.IsNullOrWhiteSpace(request.DiagnosticoClave))
            {
                return BadRequest(Error(
                    "Seleccione el diagnóstico que desea clasificar."));
            }

            string etapa = NormalizarEtapa(request.Etapa);

            IActionResult? acceso =
                await ValidarActualizacionEtapaAsync(
                    usuarioId.Value,
                    etapa,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            bool pertenece = await db.Imagenes
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.DiagnosticoIAImagenId == fotografiaId,
                    cancellationToken);

            if (!pertenece)
            {
                return NotFound(Error(
                    "La fotografía no pertenece a la inspección."));
            }

            List<ClasificacionDiagnosticoFitosanitarioRegistro> actuales =
                await clasificaciones.SincronizarYObtenerAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken);

            ClasificacionDiagnosticoFitosanitarioRegistro? actual =
                actuales.FirstOrDefault(item =>
                    item.FotografiaId == fotografiaId &&
                    string.Equals(
                        item.DiagnosticoClave,
                        request.DiagnosticoClave.Trim(),
                        StringComparison.Ordinal));

            if (actual == null)
            {
                return NotFound(Error(
                    "No se encontró el diagnóstico indicado en la fotografía."));
            }

            if (string.Equals(
                    actual.Estado,
                    "RESUELTA_APROBADOR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(Error(
                    "La clasificación de este diagnóstico ya fue confirmada por el aprobador y quedó cerrada. La publicación puede administrarse por separado sin sobrescribirla."));
            }

            string accion = NormalizarAccion(request.Accion);
            bool descartar = accion == "DESCARTAR";
            string estadoAnterior = actual.Estado;

            if (descartar)
            {
                bool descartado = await clasificaciones.ResolverAsync(
                    id,
                    fotografiaId,
                    new ResolverClasificacionDiagnosticoFitosanitarioRequest
                    {
                        DiagnosticoClave = request.DiagnosticoClave,
                        Etapa = etapa,
                        Accion = "DESCARTAR"
                    },
                    usuarioId.Value,
                    cancellationToken);

                if (!descartado)
                {
                    return NotFound(Error(
                        "No se encontró el diagnóstico indicado en la fotografía."));
                }

                await RegistrarHistorialAsync(
                    fotografiaId,
                    request.DiagnosticoClave,
                    estadoAnterior,
                    "DESCARTADA_DIAGNOSTICO",
                    "DESCARTAR",
                    null,
                    null,
                    string.Empty,
                    string.Empty,
                    request.Motivo,
                    usuarioId.Value,
                    cancellationToken);

                return Ok(await ConstruirRespuestaActualizadaAsync(
                    id,
                    "El diagnóstico fue descartado para la clasificación del Álbum.",
                    cancellationToken));
            }

            if (request.CategoriaAlbumBotanicoId is not > 0)
            {
                return BadRequest(Error(
                    "Seleccione una categoría existente del Álbum Botánico."));
            }

            CategoriaAlbumJerarquia? categoria = await albumDb.Categorias
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        request.CategoriaAlbumBotanicoId.Value &&
                    item.Activo,
                    cancellationToken);

            if (categoria == null)
            {
                return BadRequest(Error(
                    "La categoría seleccionada no existe o está inactiva."));
            }

            if (request.ProponerSubcategoria)
            {
                string nombrePropuesto = Limitar(
                    request.Subcategoria,
                    200);

                if (nombrePropuesto.Length < 3)
                {
                    return BadRequest(Error(
                        "Ingrese un nombre válido para la subcategoría propuesta."));
                }

                if (etapa == "ANALIZADOR")
                {
                    await GuardarPropuestaAnalizadorAsync(
                        id,
                        fotografiaId,
                        request,
                        categoria,
                        accion,
                        usuarioId.Value,
                        cancellationToken);

                    await RegistrarHistorialAsync(
                        fotografiaId,
                        request.DiagnosticoClave,
                        estadoAnterior,
                        "PROPUESTA_ANALIZADOR",
                        accion,
                        categoria.CategoriaAlbumBotanicoId,
                        null,
                        categoria.NombreCategoria,
                        nombrePropuesto,
                        request.Motivo,
                        usuarioId.Value,
                        cancellationToken);

                    return Ok(await ConstruirRespuestaActualizadaAsync(
                        id,
                        "La subcategoría quedó propuesta para que el aprobador la confirme.",
                        cancellationToken));
                }

                if (etapa != "APROBADOR")
                {
                    return BadRequest(Error(
                        "La propuesta de una nueva subcategoría solo puede ser revisada por el analizador y confirmada por el aprobador."));
                }

                AlbumBotanicoCafeJerarquia subcategoria =
                    await ObtenerOCrearSubcategoriaAsync(
                        categoria,
                        request,
                        cancellationToken);

                bool resuelta = await ResolverExistenteAsync(
                    id,
                    fotografiaId,
                    request,
                    categoria,
                    subcategoria,
                    etapa,
                    accion,
                    usuarioId.Value,
                    cancellationToken);

                if (!resuelta)
                {
                    return NotFound(Error(
                        "No se encontró el diagnóstico indicado en la fotografía."));
                }

                await RegistrarHistorialAsync(
                    fotografiaId,
                    request.DiagnosticoClave,
                    estadoAnterior,
                    "RESUELTA_APROBADOR",
                    accion,
                    categoria.CategoriaAlbumBotanicoId,
                    subcategoria.AlbumBotanicoCafeId,
                    categoria.NombreCategoria,
                    subcategoria.Titulo,
                    request.Motivo,
                    usuarioId.Value,
                    cancellationToken);

                return Ok(await ConstruirRespuestaActualizadaAsync(
                    id,
                    "La propuesta fue confirmada y quedó como subcategoría oficial del Álbum Botánico.",
                    cancellationToken));
            }

            if (request.AlbumBotanicoCafeId is not > 0)
            {
                return BadRequest(Error(
                    "Seleccione una subcategoría existente o utilice la opción de propuesta."));
            }

            AlbumBotanicoCafeJerarquia? existente = await albumDb.Subcategorias
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId ==
                        request.AlbumBotanicoCafeId.Value &&
                    item.CategoriaAlbumBotanicoId ==
                        categoria.CategoriaAlbumBotanicoId &&
                    item.Activo &&
                    item.Categoria.Activo,
                    cancellationToken);

            if (existente == null)
            {
                return BadRequest(Error(
                    "La subcategoría seleccionada no pertenece a la categoría indicada o está inactiva."));
            }

            bool actualizado = await ResolverExistenteAsync(
                id,
                fotografiaId,
                request,
                categoria,
                existente,
                etapa,
                accion,
                usuarioId.Value,
                cancellationToken);

            if (!actualizado)
            {
                return NotFound(Error(
                    "No se encontró el diagnóstico indicado en la fotografía."));
            }

            string estadoNuevo = etapa switch
            {
                "APROBADOR" => "RESUELTA_APROBADOR",
                "ANALIZADOR" => "RESUELTA_ANALIZADOR",
                _ => "RESUELTA_TECNICO"
            };

            await RegistrarHistorialAsync(
                fotografiaId,
                request.DiagnosticoClave,
                estadoAnterior,
                estadoNuevo,
                accion,
                categoria.CategoriaAlbumBotanicoId,
                existente.AlbumBotanicoCafeId,
                categoria.NombreCategoria,
                existente.Titulo,
                request.Motivo,
                usuarioId.Value,
                cancellationToken);

            return Ok(await ConstruirRespuestaActualizadaAsync(
                id,
                "La clasificación del diagnóstico fue guardada.",
                cancellationToken));
        }

        private async Task GuardarPropuestaAnalizadorAsync(
            int inspeccionId,
            int fotografiaId,
            ResolverClasificacionDiagnosticoFitosanitarioV2Request request,
            CategoriaAlbumJerarquia categoria,
            string accion,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            string clave = Limitar(request.DiagnosticoClave, 180);
            string subcategoria = Limitar(request.Subcategoria, 200);

            int filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE c
SET
    CategoriaAlbumBotanicoIdSeleccionada =
        {categoria.CategoriaAlbumBotanicoId},
    AlbumBotanicoCafeIdSeleccionado = NULL,
    CategoriaSeleccionada = {categoria.NombreCategoria},
    SubcategoriaSeleccionada = {subcategoria},
    CategoriaSugerida = {categoria.NombreCategoria},
    SubcategoriaSugerida = {subcategoria},
    NombreCientificoSugerido = {Limitar(request.NombreCientifico, 200)},
    CoincideCatalogo = 0,
    AccionHumana = {accion},
    Estado = N'PROPUESTA_ANALIZADOR',
    RequiereDecision = 1,
    UsuarioActualizacionId = {usuarioId},
    FechaActualizacionUtc = SYSUTCDATETIME()
FROM dbo.diagnosticoIAImagenClasificacionDiagnostico c
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = c.DiagnosticoIAImagenId
WHERE i.DiagnosticoIAId = {inspeccionId}
  AND c.DiagnosticoIAImagenId = {fotografiaId}
  AND c.DiagnosticoClave = {clave}
  AND c.Activo = 1;
""", cancellationToken);

            if (filas <= 0)
            {
                throw new InvalidOperationException(
                    "No fue posible registrar la propuesta del diagnóstico.");
            }
        }

        private async Task<AlbumBotanicoCafeJerarquia>
            ObtenerOCrearSubcategoriaAsync(
                CategoriaAlbumJerarquia categoria,
                ResolverClasificacionDiagnosticoFitosanitarioV2Request request,
                CancellationToken cancellationToken)
        {
            string titulo = Limitar(request.Subcategoria, 200);

            AlbumBotanicoCafeJerarquia? existente =
                await albumDb.Subcategorias
                    .FirstOrDefaultAsync(item =>
                        item.CategoriaAlbumBotanicoId ==
                            categoria.CategoriaAlbumBotanicoId &&
                        item.Titulo == titulo,
                        cancellationToken);

            if (existente != null)
            {
                if (!existente.Activo)
                {
                    throw new InvalidOperationException(
                        "Ya existe una subcategoría con ese nombre, pero está inactiva. Actívela desde la administración del Álbum Botánico antes de continuar.");
                }

                return existente;
            }

            string descripcion = Limitar(request.Descripcion, 4000);
            if (descripcion.Length < 8)
            {
                descripcion =
                    $"Subcategoría fitosanitaria {titulo}, confirmada durante la revisión técnica.";
            }

            var nueva = new AlbumBotanicoCafeJerarquia
            {
                CategoriaAlbumBotanicoId =
                    categoria.CategoriaAlbumBotanicoId,
                Titulo = titulo,
                NombreCientifico =
                    LimitarNullable(request.NombreCientifico, 200),
                Descripcion = descripcion,
                Sintomas = LimitarNullable(request.Sintomas, 4000),
                Activo = true,
                FechaCreacion = DateTime.Now
            };

            albumDb.Subcategorias.Add(nueva);
            await albumDb.SaveChangesAsync(cancellationToken);
            return nueva;
        }

        private async Task<bool> ResolverExistenteAsync(
            int inspeccionId,
            int fotografiaId,
            ResolverClasificacionDiagnosticoFitosanitarioV2Request request,
            CategoriaAlbumJerarquia categoria,
            AlbumBotanicoCafeJerarquia subcategoria,
            string etapa,
            string accion,
            int usuarioId,
            CancellationToken cancellationToken) =>
            await clasificaciones.ResolverAsync(
                inspeccionId,
                fotografiaId,
                new ResolverClasificacionDiagnosticoFitosanitarioRequest
                {
                    DiagnosticoClave = request.DiagnosticoClave,
                    Etapa = etapa,
                    Accion = accion,
                    CategoriaAlbumBotanicoId =
                        categoria.CategoriaAlbumBotanicoId,
                    AlbumBotanicoCafeId =
                        subcategoria.AlbumBotanicoCafeId,
                    Categoria = categoria.NombreCategoria,
                    Subcategoria = subcategoria.Titulo
                },
                usuarioId,
                cancellationToken);

        private async Task<object> ConstruirRespuestaActualizadaAsync(
            int inspeccionId,
            string mensaje,
            CancellationToken cancellationToken)
        {
            List<ClasificacionDiagnosticoFitosanitarioRegistro> data =
                await clasificaciones.ObtenerPorInspeccionAsync(
                    inspeccionId,
                    cancellationToken);

            return new
            {
                success = true,
                message = mensaje,
                data
            };
        }

        private async Task AsegurarHistorialAsync(
            CancellationToken cancellationToken)
        {
            if (historialInicializado)
                return;

            await HistorialInicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (historialInicializado)
                    return;

                const string sql = """
IF OBJECT_ID(
    N'dbo.diagnosticoIAImagenClasificacionDiagnosticoHistorial',
    N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenClasificacionDiagnosticoHistorial
    (
        DiagnosticoIAImagenClasificacionDiagnosticoHistorialId
            INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        DiagnosticoClave NVARCHAR(180) NOT NULL,
        EstadoAnterior NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAClasDiagHist_ant DEFAULT(N''),
        EstadoNuevo NVARCHAR(40) NOT NULL,
        Accion NVARCHAR(30) NOT NULL,
        CategoriaAlbumBotanicoId INT NULL,
        AlbumBotanicoCafeId INT NULL,
        Categoria NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagIAClasDiagHist_cat DEFAULT(N''),
        Subcategoria NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagIAClasDiagHist_sub DEFAULT(N''),
        Motivo NVARCHAR(1200) NOT NULL
            CONSTRAINT DF_diagIAClasDiagHist_mot DEFAULT(N''),
        UsuarioId INT NOT NULL,
        FechaUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_diagIAClasDiagHist_fecha DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_diagIAImagenClasificacionDiagnosticoHistorial
            PRIMARY KEY
            (DiagnosticoIAImagenClasificacionDiagnosticoHistorialId),
        CONSTRAINT FK_diagIAClasDiagHist_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
            ON DELETE CASCADE
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(
        N'dbo.diagnosticoIAImagenClasificacionDiagnosticoHistorial')
      AND name = N'IX_diagIAClasDiagHist_fotoClaveFecha'
)
BEGIN
    CREATE INDEX IX_diagIAClasDiagHist_fotoClaveFecha
        ON dbo.diagnosticoIAImagenClasificacionDiagnosticoHistorial
        (
            DiagnosticoIAImagenId,
            DiagnosticoClave,
            FechaUtc DESC
        );
END;
""";

                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);
                historialInicializado = true;
            }
            catch
            {
                historialInicializado = false;
                throw;
            }
            finally
            {
                HistorialInicializacionLock.Release();
            }
        }

        private async Task RegistrarHistorialAsync(
            int fotografiaId,
            string diagnosticoClave,
            string estadoAnterior,
            string estadoNuevo,
            string accion,
            int? categoriaId,
            int? subcategoriaId,
            string categoria,
            string subcategoria,
            string motivo,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await AsegurarHistorialAsync(cancellationToken);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO dbo.diagnosticoIAImagenClasificacionDiagnosticoHistorial
(
    DiagnosticoIAImagenId,
    DiagnosticoClave,
    EstadoAnterior,
    EstadoNuevo,
    Accion,
    CategoriaAlbumBotanicoId,
    AlbumBotanicoCafeId,
    Categoria,
    Subcategoria,
    Motivo,
    UsuarioId,
    FechaUtc
)
VALUES
(
    {fotografiaId},
    {Limitar(diagnosticoClave, 180)},
    {Limitar(estadoAnterior, 40)},
    {Limitar(estadoNuevo, 40)},
    {Limitar(accion, 30)},
    {categoriaId},
    {subcategoriaId},
    {Limitar(categoria, 150)},
    {Limitar(subcategoria, 200)},
    {Limitar(motivo, 1200)},
    {usuarioId},
    SYSUTCDATETIME()
);
""", cancellationToken);
        }

        private async Task<IActionResult?> ValidarLecturaModuloAsync(
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            string[] interfaces =
            [
                DiagnosticoIAFlujo.InterfazSolicitud,
                DiagnosticoIAFlujo.InterfazAnalizador,
                DiagnosticoIAFlujo.InterfazAprobador,
                DiagnosticoIAFlujo.InterfazAlbum
            ];

            foreach (string interfaz in interfaces.Distinct())
            {
                ResultadoPermisoApi resultado =
                    await permisos.ValidarAsync(
                        usuarioId,
                        interfaz,
                        TipoPermisoApi.Leer,
                        cancellationToken);

                if (resultado.Permitido)
                    return null;
            }

            return StatusCode(
                StatusCodes.Status403Forbidden,
                Error(
                    "No tiene permisos para consultar la clasificación fitosanitaria."));
        }

        private async Task<IActionResult?>
            ValidarActualizacionEtapaAsync(
                int usuarioId,
                string etapa,
                CancellationToken cancellationToken)
        {
            string interfaz = etapa switch
            {
                "APROBADOR" =>
                    DiagnosticoIAFlujo.InterfazAprobador,
                "ANALIZADOR" =>
                    DiagnosticoIAFlujo.InterfazAnalizador,
                _ =>
                    DiagnosticoIAFlujo.InterfazSolicitud
            };

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    interfaz,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("usuarioId");

            return int.TryParse(valor, out int id)
                ? id
                : null;
        }

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

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private static string? LimitarNullable(
            string? valor,
            int maximo)
        {
            string texto = Limitar(valor, maximo);
            return string.IsNullOrWhiteSpace(texto)
                ? null
                : texto;
        }

        private static object Error(string? mensaje) => new
        {
            success = false,
            message = string.IsNullOrWhiteSpace(mensaje)
                ? "No fue posible completar la operación."
                : mensaje
        };
    }
}
