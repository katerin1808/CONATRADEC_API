using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Búsqueda paginada de inspecciones con cursor estable. La consulta trae
    /// una página completa mediante un solo comando SQL y evita cargar todas
    /// las fotografías o ejecutar una consulta adicional por tarjeta.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaBandejaController : ControllerBase
    {
        private static readonly SemaphoreSlim IndicesLock = new(1, 1);
        private static volatile bool indicesInicializados;

        private readonly DBContext db;
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;

        public InspeccionFitosanitariaBandejaController(
            DBContext db,
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.db = db;
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.control = control;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
        }

        [HttpGet("bandeja-paginada")]
        public async Task<IActionResult> ObtenerPaginada(
            [FromQuery] string modo = "mis",
            [FromQuery] string? buscar = null,
            [FromQuery] string? propietario = null,
            [FromQuery] string? departamento = null,
            [FromQuery] string? tipoFotografia = null,
            [FromQuery] string? estado = null,
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            [FromQuery] int desfaseHorarioMinutos = 0,
            [FromQuery] DateTime? ultimaFechaUtc = null,
            [FromQuery] int? ultimoId = null,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            if (!usuarioId.HasValue)
                return Forbid();

            tamanoPagina = Math.Clamp(tamanoPagina, 10, 50);
            desfaseHorarioMinutos = Math.Clamp(
                desfaseHorarioMinutos,
                -840,
                840);

            DateTime hoyLocal = DateTime.UtcNow
                .AddMinutes(desfaseHorarioMinutos)
                .Date;

            DateTime? desdeLocal = fechaDesde?.Date;
            DateTime? hastaLocal = fechaHasta?.Date;

            if (desdeLocal.HasValue && desdeLocal.Value > hoyLocal)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fecha inicial no puede estar en el futuro."
                });
            }

            if (hastaLocal.HasValue && hastaLocal.Value > hoyLocal)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fecha final no puede estar en el futuro."
                });
            }

            if (desdeLocal.HasValue &&
                hastaLocal.HasValue &&
                desdeLocal.Value > hastaLocal.Value)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fecha inicial debe ser anterior o igual a la fecha final."
                });
            }

            /*
             * FechaSolicitudUtc se almacena en UTC. El usuario, sin embargo,
             * selecciona días locales. Convertimos los límites antes de consultar
             * para que, por ejemplo, 04/08 incluya todo el 4 de agosto en su zona.
             */
            DateTime? desde = desdeLocal?.AddMinutes(
                -desfaseHorarioMinutos);
            DateTime? hastaExclusiva = hastaLocal?
                .AddDays(1)
                .AddMinutes(-desfaseHorarioMinutos);

            if (ultimaFechaUtc.HasValue != ultimoId.HasValue)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El cursor de paginación está incompleto."
                });
            }

            string modoNormalizado = Normalizar(modo).ToLowerInvariant();
            bool modoHistorial = modoNormalizado == "historial";
            bool modoDecisiones = modoNormalizado == "decisiones";
            bool soloPropias = !modoHistorial;

            string estadoNormalizado = modoDecisiones
                ? string.Empty
                : NormalizarCodigo(estado);
            string tipoNormalizado = NormalizarCodigo(tipoFotografia);

            HashSet<string> estadosValidos =
            [
                InspeccionFitosanitariaFlujo.InspeccionEstados.Borrador,
                InspeccionFitosanitariaFlujo.InspeccionEstados.EnProceso,
                InspeccionFitosanitariaFlujo.InspeccionEstados.EnProcesoConErrores,
                InspeccionFitosanitariaFlujo.InspeccionEstados.PendienteRevision,
                InspeccionFitosanitariaFlujo.InspeccionEstados.PendienteAprobacion,
                InspeccionFitosanitariaFlujo.InspeccionEstados.Finalizada,
                InspeccionFitosanitariaFlujo.InspeccionEstados.FinalizadaParcialmente
            ];

            if (!string.IsNullOrWhiteSpace(estadoNormalizado) &&
                !estadosValidos.Contains(estadoNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El estado indicado no es válido."
                });
            }

            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await AsegurarIndicesAsync(cancellationToken);

            List<InspeccionFitosanitariaBandejaItemDto> items =
                await ConsultarAsync(
                    usuarioId.Value,
                    soloPropias,
                    modoDecisiones,
                    modoHistorial,
                    Normalizar(buscar),
                    Normalizar(propietario),
                    Normalizar(departamento),
                    tipoNormalizado,
                    estadoNormalizado,
                    desde,
                    hastaExclusiva,
                    ultimaFechaUtc?.ToUniversalTime(),
                    ultimoId,
                    tamanoPagina + 1,
                    cancellationToken);

            bool hayMas = items.Count > tamanoPagina;
            if (hayMas)
                items.RemoveAt(items.Count - 1);

            InspeccionFitosanitariaBandejaItemDto? ultimo =
                items.LastOrDefault();

            var pagina = new InspeccionFitosanitariaBandejaPaginaDto
            {
                Items = items,
                HayMas = hayMas,
                SiguienteFechaUtc = hayMas
                    ? ultimo?.FechaRegistroSistemaUtc
                    : null,
                SiguienteId = hayMas
                    ? ultimo?.InspeccionId
                    : null
            };

            return Ok(new
            {
                success = true,
                message = modoDecisiones
                    ? "Inspecciones con decisiones técnicas pendientes obtenidas correctamente."
                    : "Inspecciones obtenidas correctamente.",
                data = pagina
            });
        }

        private async Task<List<InspeccionFitosanitariaBandejaItemDto>>
            ConsultarAsync(
                int usuarioId,
                bool soloPropias,
                bool modoDecisiones,
                bool modoHistorial,
                string buscar,
                string propietario,
                string departamento,
                string tipoFotografia,
                string estado,
                DateTime? fechaDesde,
                DateTime? fechaHastaExclusiva,
                DateTime? ultimaFechaUtc,
                int? ultimoId,
                int limite,
                CancellationToken cancellationToken)
        {
            const string sql = """
WITH bandejaBase AS
(
    SELECT
        d.DiagnosticoIAId AS InspeccionId,
        ISNULL(NULLIF(LTRIM(RTRIM(d.NombreInspeccion)), N''),
               N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId))
            AS NombreInspeccion,
        CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0))
            AS EtapaTecnicaFinalizada,
        CONVERT(BIT,
            CASE
                WHEN ISNULL(d.CerradaDefinitiva, 0) = 1
                     OR ISNULL(d.CerradaTecnico, 0) = 1
                    THEN 1
                ELSE 0
            END) AS CerradaDefinitiva,
        ISNULL(d.CodigoTerreno, N'') AS CodigoTerreno,
        d.FechaSolicitudUtc AS FechaRegistroSistemaUtc,
        ISNULL(propietarioActual.NombreCompleto, N'') AS Propietario,
        ISNULL(m.NombreMunicipio, N'') AS Municipio,
        ISNULL(dep.NombreDepartamento, N'') AS Departamento,
        CONVERT(INT, ISNULL(resumen.TotalFotografias, 0)) AS TotalFotografias,
        CONVERT(INT, ISNULL(resumen.Pendientes, 0)) AS Pendientes,
        CONVERT(INT, ISNULL(resumen.ConError, 0)) AS ConError,
        CONVERT(INT, ISNULL(resumen.Finalizadas, 0)) AS Finalizadas,
        CONVERT(INT, ISNULL(resumen.PendienteDecisionTecnico, 0))
            AS RequierenDecisionTecnico,
        CONVERT(INT, ISNULL(resumen.EnviadasRevision, 0))
            AS EnviadasRevision,
        CONVERT(INT, ISNULL(resumen.Procesando, 0)) AS Procesando,
        CONVERT(INT, ISNULL(resumen.Descartadas, 0)) AS Descartadas,
        ISNULL(portada.UrlImagen, N'') AS UrlMiniatura,
        CASE
            WHEN ISNULL(d.CerradaDefinitiva, 0) = 1
                 OR ISNULL(d.CerradaTecnico, 0) = 1
                THEN CASE
                    WHEN ISNULL(resumen.TotalFotografias, 0) > 0
                         AND ISNULL(resumen.TotalFotografias, 0) =
                             ISNULL(resumen.Finalizadas, 0)
                         AND ISNULL(resumen.FinalizadasExitosas, 0) =
                             ISNULL(resumen.TotalFotografias, 0)
                        THEN N'FINALIZADA'
                    ELSE N'FINALIZADA_PARCIALMENTE'
                END
            WHEN ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
                THEN CASE
                    WHEN ISNULL(resumen.PendienteAprobacion, 0) > 0
                        THEN N'PENDIENTE_APROBACION'
                    ELSE N'PENDIENTE_REVISION'
                END
            WHEN ISNULL(resumen.TotalFotografias, 0) = 0
                 OR ISNULL(resumen.BorradorOPendienteIA, 0) =
                    ISNULL(resumen.TotalFotografias, 0)
                THEN N'BORRADOR'
            WHEN ISNULL(resumen.ConError, 0) > 0
                THEN N'EN_PROCESO_CON_ERRORES'
            ELSE N'EN_PROCESO'
        END AS EstadoCalculado
    FROM dbo.diagnosticoIA d
    LEFT JOIN dbo.terreno t
        ON t.terrenoId = d.TerrenoId
    LEFT JOIN dbo.municipio m
        ON m.MunicipioId = t.municipioId
    LEFT JOIN dbo.departamento dep
        ON dep.DepartamentoId = m.DepartamentoId
    OUTER APPLY
    (
        SELECT TOP(1)
            p.nombreCompleto AS NombreCompleto
        FROM dbo.propietarioTerreno pt
        INNER JOIN dbo.propietario p
            ON p.propietarioId = pt.propietarioId
        WHERE pt.terrenoId = t.terrenoId
          AND pt.activo = 1
          AND p.activo = 1
        ORDER BY
            pt.fechaAsignacionUtc DESC,
            pt.propietarioTerrenoId DESC
    ) propietarioActual
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(1) AS TotalFotografias,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'BORRADOR', N'PENDIENTE_IA')
                    THEN 1 ELSE 0 END) AS BorradorOPendienteIA,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) =
                    N'PENDIENTE_DECISION_TECNICO'
                    THEN 1 ELSE 0 END) AS PendienteDecisionTecnico,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) =
                    N'PENDIENTE_APROBACION'
                    THEN 1 ELSE 0 END) AS PendienteAprobacion,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTO_PARA_CORRECCION', N'DEVUELTA_AL_ANALIZADOR')
                    THEN 1 ELSE 0 END) AS PendienteRevision,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'PENDIENTE_IA', N'ANALIZANDO_IA')
                    THEN 1 ELSE 0 END) AS Procesando,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'ERROR_IA'
                    THEN 1 ELSE 0 END) AS ConError,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
                    (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                     N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM',
                     N'ERROR_IA')
                    THEN 1 ELSE 0 END) AS Pendientes,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                     N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM')
                    THEN 1 ELSE 0 END) AS Finalizadas,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'APROBADA', N'APROBADA_CON_CORRECCION',
                     N'DESCARTADA', N'PUBLICADA_ALBUM')
                    THEN 1 ELSE 0 END) AS FinalizadasExitosas,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTO_PARA_CORRECCION', N'DEVUELTA_AL_ANALIZADOR',
                     N'PENDIENTE_APROBACION', N'APROBADA',
                     N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                     N'NO_CONCLUYENTE', N'PUBLICADA_ALBUM')
                    THEN 1 ELSE 0 END) AS EnviadasRevision,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'DESCARTADA'
                    THEN 1 ELSE 0 END) AS Descartadas
        FROM dbo.diagnosticoIAImagen i
        WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
          AND ISNULL(i.Activo, 1) = 1
    ) resumen
    OUTER APPLY
    (
        SELECT TOP(1)
            i.UrlImagen
        FROM dbo.diagnosticoIAImagen i
        WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
          AND ISNULL(i.Activo, 1) = 1
        ORDER BY i.Orden, i.DiagnosticoIAImagenId
    ) portada
    WHERE d.Activo = 1
      AND (@soloPropias = 0 OR d.UsuarioSolicitanteId = @usuarioId)
      AND (@fechaDesde IS NULL OR d.FechaSolicitudUtc >= @fechaDesde)
      AND (@fechaHasta IS NULL OR d.FechaSolicitudUtc < @fechaHasta)
      AND
      (
          @ultimaFechaUtc IS NULL
          OR d.FechaSolicitudUtc < @ultimaFechaUtc
          OR
          (
              d.FechaSolicitudUtc = @ultimaFechaUtc
              AND d.DiagnosticoIAId < @ultimoId
          )
      )
      AND
      (
          @buscar = N''
          OR ISNULL(d.NombreInspeccion, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(d.CodigoTerreno, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(t.direccionTerreno, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(m.NombreMunicipio, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(dep.NombreDepartamento, N'') LIKE N'%' + @buscar + N'%'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.propietarioTerreno ptBuscar
              INNER JOIN dbo.propietario pBuscar
                  ON pBuscar.propietarioId = ptBuscar.propietarioId
              WHERE ptBuscar.terrenoId = t.terrenoId
                AND ptBuscar.activo = 1
                AND pBuscar.activo = 1
                AND
                (
                    pBuscar.nombreCompleto LIKE N'%' + @buscar + N'%'
                    OR pBuscar.identificacion LIKE N'%' + @buscar + N'%'
                )
          )
          OR EXISTS
          (
              SELECT 1
              FROM dbo.diagnosticoIAImagen iBuscar
              WHERE iBuscar.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(iBuscar.Activo, 1) = 1
                AND ISNULL(iBuscar.NombreArchivoOriginal, N'')
                    LIKE N'%' + @buscar + N'%'
          )
      )
      AND
      (
          @propietario = N''
          OR EXISTS
          (
              SELECT 1
              FROM dbo.propietarioTerreno ptFiltro
              INNER JOIN dbo.propietario pFiltro
                  ON pFiltro.propietarioId = ptFiltro.propietarioId
              WHERE ptFiltro.terrenoId = t.terrenoId
                AND ptFiltro.activo = 1
                AND pFiltro.activo = 1
                AND
                (
                    pFiltro.nombreCompleto
                        LIKE N'%' + @propietario + N'%'
                    OR pFiltro.identificacion
                        LIKE N'%' + @propietario + N'%'
                )
          )
      )
      AND
      (
          @departamento = N''
          OR ISNULL(dep.NombreDepartamento, N'')
                LIKE N'%' + @departamento + N'%'
      )
      AND
      (
          @tipoFotografia = N''
          OR EXISTS
          (
              SELECT 1
              FROM dbo.diagnosticoIAImagen iTipo
              WHERE iTipo.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(iTipo.Activo, 1) = 1
                AND UPPER(ISNULL(iTipo.TipoFotografia, N'')) =
                    @tipoFotografia
          )
      )
)
SELECT TOP(@limite)
    InspeccionId,
    NombreInspeccion,
    EtapaTecnicaFinalizada AS CerradaTecnico,
    EtapaTecnicaFinalizada,
    CerradaDefinitiva,
    CodigoTerreno,
    Propietario,
    Municipio,
    Departamento,
    FechaRegistroSistemaUtc,
    EstadoCalculado,
    TotalFotografias,
    Pendientes,
    ConError,
    Finalizadas,
    RequierenDecisionTecnico,
    EnviadasRevision,
    Procesando,
    Descartadas,
    UrlMiniatura
FROM bandejaBase
WHERE (@estado = N'' OR EstadoCalculado = @estado)
  AND
  (
      @modoDecisiones = 0
      OR
      (
          EtapaTecnicaFinalizada = 0
          AND CerradaDefinitiva = 0
          AND RequierenDecisionTecnico > 0
      )
  )
  AND
  (
      @modoHistorial = 0
      OR CerradaDefinitiva = 1
  )
ORDER BY FechaRegistroSistemaUtc DESC, InspeccionId DESC;
""";

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrarConexion = conexion.State != ConnectionState.Open;

            if (cerrarConexion)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;

                AgregarParametro(
                    comando,
                    "@usuarioId",
                    usuarioId,
                    DbType.Int32);
                AgregarParametro(
                    comando,
                    "@soloPropias",
                    soloPropias,
                    DbType.Boolean);
                AgregarParametro(
                    comando,
                    "@modoDecisiones",
                    modoDecisiones,
                    DbType.Boolean);
                AgregarParametro(
                    comando,
                    "@modoHistorial",
                    modoHistorial,
                    DbType.Boolean);
                AgregarParametro(comando, "@buscar", buscar, DbType.String);
                AgregarParametro(
                    comando,
                    "@propietario",
                    propietario,
                    DbType.String);
                AgregarParametro(
                    comando,
                    "@departamento",
                    departamento,
                    DbType.String);
                AgregarParametro(
                    comando,
                    "@tipoFotografia",
                    tipoFotografia,
                    DbType.String);
                AgregarParametro(comando, "@estado", estado, DbType.String);
                AgregarParametro(
                    comando,
                    "@fechaDesde",
                    fechaDesde,
                    DbType.DateTime2);
                AgregarParametro(
                    comando,
                    "@fechaHasta",
                    fechaHastaExclusiva,
                    DbType.DateTime2);
                AgregarParametro(
                    comando,
                    "@ultimaFechaUtc",
                    ultimaFechaUtc,
                    DbType.DateTime2);
                AgregarParametro(
                    comando,
                    "@ultimoId",
                    ultimoId,
                    DbType.Int32);
                AgregarParametro(
                    comando,
                    "@limite",
                    limite,
                    DbType.Int32);

                var resultado =
                    new List<InspeccionFitosanitariaBandejaItemDto>(limite);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Add(new InspeccionFitosanitariaBandejaItemDto
                    {
                        InspeccionId = reader.GetInt32(0),
                        NombreInspeccion = Texto(reader, 1),
                        CerradaTecnico = reader.GetBoolean(2),
                        EtapaTecnicaFinalizada = reader.GetBoolean(3),
                        CerradaDefinitiva = reader.GetBoolean(4),
                        CodigoTerreno = Texto(reader, 5),
                        Propietario = Texto(reader, 6),
                        Municipio = Texto(reader, 7),
                        Departamento = Texto(reader, 8),
                        FechaRegistroSistemaUtc = DateTime.SpecifyKind(
                            reader.GetDateTime(9),
                            DateTimeKind.Utc),
                        Estado = Texto(reader, 10),
                        TotalFotografias = reader.GetInt32(11),
                        Pendientes = reader.GetInt32(12),
                        ConError = reader.GetInt32(13),
                        Finalizadas = reader.GetInt32(14),
                        RequierenDecisionTecnico = reader.GetInt32(15),
                        EnviadasRevision = reader.GetInt32(16),
                        Procesando = reader.GetInt32(17),
                        Descartadas = reader.GetInt32(18),
                        UrlMiniatura = Texto(reader, 19)
                    });
                }

                return resultado;
            }
            finally
            {
                if (cerrarConexion)
                    await conexion.CloseAsync();
            }
        }

        private async Task AsegurarIndicesAsync(
            CancellationToken cancellationToken)
        {
            if (indicesInicializados)
                return;

            await IndicesLock.WaitAsync(cancellationToken);

            try
            {
                if (indicesInicializados)
                    return;

                const string sqlIndices = """
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagnosticoIA_bandeja_cursor_usuario'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIA]')
)
BEGIN
    CREATE INDEX [IX_diagnosticoIA_bandeja_cursor_usuario]
        ON [dbo].[diagnosticoIA]
        (
            [UsuarioSolicitanteId],
            [Activo],
            [FechaSolicitudUtc] DESC,
            [DiagnosticoIAId] DESC
        )
        INCLUDE
        (
            [TerrenoId],
            [CodigoTerreno],
            [EtapaTecnicaFinalizada],
            [CerradaDefinitiva],
            [CerradaTecnico]
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagnosticoIA_bandeja_cursor_global'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIA]')
)
BEGIN
    CREATE INDEX [IX_diagnosticoIA_bandeja_cursor_global]
        ON [dbo].[diagnosticoIA]
        (
            [Activo],
            [FechaSolicitudUtc] DESC,
            [DiagnosticoIAId] DESC
        )
        INCLUDE
        (
            [TerrenoId],
            [CodigoTerreno],
            [UsuarioSolicitanteId],
            [EtapaTecnicaFinalizada],
            [CerradaDefinitiva],
            [CerradaTecnico]
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagnosticoIAImagen_bandeja_estado'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIAImagen]')
)
BEGIN
    CREATE INDEX [IX_diagnosticoIAImagen_bandeja_estado]
        ON [dbo].[diagnosticoIAImagen]
        (
            [DiagnosticoIAId],
            [Activo],
            [Estado]
        )
        INCLUDE
        (
            [Orden],
            [TipoFotografia],
            [NombreArchivoOriginal],
            [UrlImagen]
        );
END;
""";

                await diagnosticoDb.Database.ExecuteSqlRawAsync(
                    sqlIndices,
                    cancellationToken);

                indicesInicializados = true;
            }
            catch
            {
                indicesInicializados = false;
                throw;
            }
            finally
            {
                IndicesLock.Release();
            }
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) &&
                   usuarioId > 0
                ? usuarioId
                : null;
        }

        private static string Normalizar(string? valor) =>
            string.IsNullOrWhiteSpace(valor)
                ? string.Empty
                : valor.Trim();

        private static string NormalizarCodigo(string? valor) =>
            string.IsNullOrWhiteSpace(valor)
                ? string.Empty
                : valor.Trim().ToUpperInvariant().Replace(' ', '_');

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor,
            DbType tipo)
        {
            DbParameter parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.DbType = tipo;
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }

        private static string Texto(
            DbDataReader reader,
            int ordinal) =>
            reader.IsDBNull(ordinal)
                ? string.Empty
                : reader.GetString(ordinal);
    }
}
