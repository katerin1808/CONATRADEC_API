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

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Centro administrativo del módulo fitosanitario. Este controlador no
    /// sustituye el flujo técnico: supervisa, audita y permite acciones de
    /// control explícitas cuando el rol posee el permiso correspondiente.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/control-fitosanitario")]
    public sealed class InspeccionFitosanitariaAdministracionController :
        ControllerBase
    {
        private readonly DBContext db;
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase flujo;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly InspeccionFitosanitariaBloqueoDatabase bloqueos;
        private readonly InspeccionFitosanitariaAdministracionDatabaseInitializer
            administracion;

        public InspeccionFitosanitariaAdministracionController(
            DBContext db,
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos)
        {
            this.db = db;
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            flujo = new InspeccionFitosanitariaDatabase(diagnosticoDb);
            control = new InspeccionFitosanitariaControlDatabaseInitializer(
                diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(
                diagnosticoDb);
            bloqueos = new InspeccionFitosanitariaBloqueoDatabase(
                diagnosticoDb);
            administracion =
                new InspeccionFitosanitariaAdministracionDatabaseInitializer(
                    diagnosticoDb);
        }

        [HttpGet("resumen")]
        public async Task<IActionResult> ObtenerResumen(
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            (DateTime? desde, DateTime? hastaExclusiva, IActionResult? error) =
                ValidarFechas(fechaDesde, fechaHasta);
            if (error != null)
                return error;

            const string sql = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();
DECLARE @hace48 DATETIME2(0) = DATEADD(HOUR, -48, @ahora);

WITH base AS
(
    SELECT
        d.DiagnosticoIAId,
        d.FechaSolicitudUtc,
        d.EtapaTecnicaFinalizada,
        d.CerradaDefinitiva,
        d.FechaCierreDefinitivoUtc
    FROM dbo.diagnosticoIA d
    WHERE d.Activo = 1
      AND (@desde IS NULL OR d.FechaSolicitudUtc >= @desde)
      AND (@hasta IS NULL OR d.FechaSolicitudUtc < @hasta)
),
fotos AS
(
    SELECT
        i.DiagnosticoIAId,
        i.DiagnosticoIAImagenId,
        UPPER(ISNULL(i.Estado, N'BORRADOR')) AS Estado,
        i.FechaRegistroSistemaUtc
    FROM dbo.diagnosticoIAImagen i
    INNER JOIN base b ON b.DiagnosticoIAId = i.DiagnosticoIAId
    WHERE ISNULL(i.Activo, 1) = 1
)
SELECT
    (SELECT COUNT(1) FROM base) AS TotalInspecciones,
    (SELECT COUNT(1) FROM base WHERE ISNULL(CerradaDefinitiva, 0) = 0) AS Abiertas,
    (SELECT COUNT(1) FROM base
        WHERE ISNULL(EtapaTecnicaFinalizada, 0) = 0
          AND ISNULL(CerradaDefinitiva, 0) = 0) AS EtapaTecnicaAbierta,
    (SELECT COUNT(1) FROM base b
        WHERE ISNULL(b.CerradaDefinitiva, 0) = 0
          AND EXISTS
          (
              SELECT 1 FROM fotos f
              WHERE f.DiagnosticoIAId = b.DiagnosticoIAId
                AND f.Estado IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION')
          )) AS PendientesAnalizador,
    (SELECT COUNT(1) FROM base b
        WHERE ISNULL(b.CerradaDefinitiva, 0) = 0
          AND EXISTS
          (
              SELECT 1 FROM fotos f
              WHERE f.DiagnosticoIAId = b.DiagnosticoIAId
                AND f.Estado = N'PENDIENTE_APROBACION'
          )) AS PendientesAprobacion,
    (SELECT COUNT(1) FROM base WHERE ISNULL(CerradaDefinitiva, 0) = 1) AS Cerradas,
    (SELECT COUNT(1) FROM fotos) AS TotalFotografias,
    (SELECT COUNT(1) FROM fotos
        WHERE Estado IN (N'PENDIENTE_IA', N'ANALIZANDO_IA')) AS FotografiasProcesando,
    (SELECT COUNT(1) FROM fotos WHERE Estado = N'ERROR_IA') AS FotografiasErrorIA,
    (SELECT COUNT(1) FROM fotos
        WHERE Estado IN (N'DEVUELTA_AL_TECNICO', N'DEVUELTA_AL_ANALIZADOR',
                         N'DEVUELTO_PARA_CORRECCION')) AS FotografiasDevueltas,
    (SELECT COUNT(1) FROM fotos WHERE Estado = N'NO_CONCLUYENTE') AS FotografiasNoConcluyentes,
    (SELECT COUNT(1) FROM fotos WHERE Estado = N'RECHAZADA') AS FotografiasRechazadas,
    (SELECT COUNT(1) FROM fotos
        WHERE Estado IN (N'APROBADA', N'APROBADA_CON_CORRECCION',
                         N'PUBLICADA_ALBUM')) AS FotografiasAprobadas,
    (SELECT COUNT(1) FROM fotos WHERE Estado = N'PUBLICADA_ALBUM') AS FotografiasPublicadasAlbum,
    (SELECT COUNT(1) FROM base b
        WHERE ISNULL(b.CerradaDefinitiva, 0) = 0
          AND b.FechaSolicitudUtc <= @hace48
          AND EXISTS
          (
              SELECT 1 FROM fotos f
              WHERE f.DiagnosticoIAId = b.DiagnosticoIAId
                AND f.Estado IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION')
          )) AS Mas48HorasAnalizador,
    (SELECT COUNT(1) FROM base b
        WHERE ISNULL(b.CerradaDefinitiva, 0) = 0
          AND b.FechaSolicitudUtc <= @hace48
          AND EXISTS
          (
              SELECT 1 FROM fotos f
              WHERE f.DiagnosticoIAId = b.DiagnosticoIAId
                AND f.Estado = N'PENDIENTE_APROBACION'
          )) AS Mas48HorasAprobador,
    (SELECT COUNT(1)
        FROM dbo.diagnosticoIAEdicionBloqueo bl
        INNER JOIN base b ON b.DiagnosticoIAId = bl.DiagnosticoIAId
        WHERE bl.ExpiraUtc > @ahora) AS BloqueosActivos,
    CAST((SELECT AVG(CAST(DATEDIFF(MINUTE, FechaSolicitudUtc,
                      FechaCierreDefinitivoUtc) AS DECIMAL(18,2))) / 60.0
          FROM base
          WHERE CerradaDefinitiva = 1
            AND FechaCierreDefinitivoUtc IS NOT NULL) AS DECIMAL(18,2))
        AS PromedioHorasCierre;
""";

            ControlFitosanitarioResumenDto resumen =
                await EjecutarAsync(async conexion =>
                {
                    await using DbCommand comando = CrearComando(conexion, sql);
                    AgregarParametro(comando, "@desde", desde);
                    AgregarParametro(comando, "@hasta", hastaExclusiva);

                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);

                    return new ControlFitosanitarioResumenDto
                    {
                        TotalInspecciones = Entero(reader, 0),
                        Abiertas = Entero(reader, 1),
                        EtapaTecnicaAbierta = Entero(reader, 2),
                        PendientesAnalizador = Entero(reader, 3),
                        PendientesAprobacion = Entero(reader, 4),
                        Cerradas = Entero(reader, 5),
                        TotalFotografias = Entero(reader, 6),
                        FotografiasProcesando = Entero(reader, 7),
                        FotografiasErrorIA = Entero(reader, 8),
                        FotografiasDevueltas = Entero(reader, 9),
                        FotografiasNoConcluyentes = Entero(reader, 10),
                        FotografiasRechazadas = Entero(reader, 11),
                        FotografiasAprobadas = Entero(reader, 12),
                        FotografiasPublicadasAlbum = Entero(reader, 13),
                        Mas48HorasAnalizador = Entero(reader, 14),
                        Mas48HorasAprobador = Entero(reader, 15),
                        BloqueosActivos = Entero(reader, 16),
                        PromedioHorasCierre = reader.IsDBNull(17)
                            ? null
                            : Convert.ToDecimal(reader.GetValue(17))
                    };
                }, cancellationToken);

            return Ok(Exito(
                "Resumen fitosanitario obtenido correctamente.",
                resumen));
        }

        [HttpGet("inspecciones")]
        public async Task<IActionResult> ListarInspecciones(
            [FromQuery] string? buscar = null,
            [FromQuery] string? estado = null,
            [FromQuery] int? tecnicoId = null,
            [FromQuery] string? departamento = null,
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 25,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            (DateTime? desde, DateTime? hastaExclusiva, IActionResult? error) =
                ValidarFechas(fechaDesde, fechaHasta);
            if (error != null)
                return error;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);
            int offset = (pagina - 1) * tamanoPagina;
            string buscarNormalizado = (buscar ?? string.Empty).Trim();
            string estadoNormalizado = NormalizarCodigo(estado);
            string departamentoNormalizado = (departamento ?? string.Empty).Trim();

            const string sql = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();

WITH FotoResumen AS
(
    SELECT
        i.DiagnosticoIAId,
        COUNT_BIG(1) AS TotalFotografias,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
            (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
             N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM')
            THEN 1 ELSE 0 END) AS Pendientes,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'')) = N'ERROR_IA'
            THEN 1 ELSE 0 END) AS ConError,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'')) IN
            (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
             N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM')
            THEN 1 ELSE 0 END) AS Finalizadas
    FROM dbo.diagnosticoIAImagen i
    WHERE ISNULL(i.Activo, 1) = 1
    GROUP BY i.DiagnosticoIAId
),
UltimaActividad AS
(
    SELECT
        i.DiagnosticoIAId,
        MAX(h.FechaUtc) AS UltimaActividadUtc
    FROM dbo.diagnosticoIAImagenHistorialV2 h
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
    GROUP BY i.DiagnosticoIAId
),
Datos AS
(
    SELECT
        d.DiagnosticoIAId,
        ISNULL(NULLIF(LTRIM(RTRIM(d.NombreInspeccion)), N''),
            N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId)) AS NombreInspeccion,
        ISNULL(d.CodigoTerreno, N'') AS CodigoTerreno,
        ISNULL(propietarioActual.NombreCompleto, N'') AS Propietario,
        ISNULL(dep.NombreDepartamento, N'') AS Departamento,
        d.UsuarioSolicitanteId,
        ISNULL(NULLIF(LTRIM(RTRIM(tecnico.nombreCompletoUsuario)), N''),
            ISNULL(tecnico.nombreUsuario, N'')) AS Tecnico,
        d.FechaSolicitudUtc,
        UPPER(ISNULL(d.Estado, N'BORRADOR')) AS Estado,
        CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0)) AS EtapaTecnicaFinalizada,
        CONVERT(BIT, ISNULL(d.CerradaDefinitiva, 0)) AS CerradaDefinitiva,
        CONVERT(INT, ISNULL(fr.TotalFotografias, 0)) AS TotalFotografias,
        CONVERT(INT, ISNULL(fr.Pendientes, 0)) AS Pendientes,
        CONVERT(INT, ISNULL(fr.ConError, 0)) AS ConError,
        CONVERT(INT, ISNULL(fr.Finalizadas, 0)) AS Finalizadas,
        a.UsuarioAnalizadorId,
        ISNULL(NULLIF(LTRIM(RTRIM(ua.nombreCompletoUsuario)), N''),
            ISNULL(ua.nombreUsuario, N'')) AS Analizador,
        a.UsuarioAprobadorId,
        ISNULL(NULLIF(LTRIM(RTRIM(up.nombreCompletoUsuario)), N''),
            ISNULL(up.nombreUsuario, N'')) AS Aprobador,
        ba.UsuarioId AS BloqueoAnalizadorUsuarioId,
        ISNULL(NULLIF(LTRIM(RTRIM(uba.nombreCompletoUsuario)), N''),
            ISNULL(uba.nombreUsuario, N'')) AS BloqueoAnalizadorUsuario,
        ba.ExpiraUtc AS BloqueoAnalizadorExpiraUtc,
        bp.UsuarioId AS BloqueoAprobadorUsuarioId,
        ISNULL(NULLIF(LTRIM(RTRIM(ubp.nombreCompletoUsuario)), N''),
            ISNULL(ubp.nombreUsuario, N'')) AS BloqueoAprobadorUsuario,
        bp.ExpiraUtc AS BloqueoAprobadorExpiraUtc,
        ua2.UltimaActividadUtc
    FROM dbo.diagnosticoIA d
    LEFT JOIN dbo.terreno t ON t.terrenoId = d.TerrenoId
    LEFT JOIN dbo.municipio m ON m.MunicipioId = t.municipioId
    LEFT JOIN dbo.departamento dep ON dep.DepartamentoId = m.DepartamentoId
    LEFT JOIN dbo.usuario tecnico ON tecnico.UsuarioId = d.UsuarioSolicitanteId
    LEFT JOIN dbo.diagnosticoIAAsignacionFlujo a
        ON a.DiagnosticoIAId = d.DiagnosticoIAId
    LEFT JOIN dbo.usuario ua ON ua.UsuarioId = a.UsuarioAnalizadorId
    LEFT JOIN dbo.usuario up ON up.UsuarioId = a.UsuarioAprobadorId
    LEFT JOIN FotoResumen fr ON fr.DiagnosticoIAId = d.DiagnosticoIAId
    LEFT JOIN UltimaActividad ua2 ON ua2.DiagnosticoIAId = d.DiagnosticoIAId
    LEFT JOIN dbo.diagnosticoIAEdicionBloqueo ba
        ON ba.DiagnosticoIAId = d.DiagnosticoIAId
       AND ba.Etapa = N'ANALIZADOR'
       AND ba.ExpiraUtc > @ahora
    LEFT JOIN dbo.usuario uba ON uba.UsuarioId = ba.UsuarioId
    LEFT JOIN dbo.diagnosticoIAEdicionBloqueo bp
        ON bp.DiagnosticoIAId = d.DiagnosticoIAId
       AND bp.Etapa = N'APROBADOR'
       AND bp.ExpiraUtc > @ahora
    LEFT JOIN dbo.usuario ubp ON ubp.UsuarioId = bp.UsuarioId
    OUTER APPLY
    (
        SELECT TOP(1) p.nombreCompleto AS NombreCompleto
        FROM dbo.propietarioTerreno pt
        INNER JOIN dbo.propietario p ON p.propietarioId = pt.propietarioId
        WHERE pt.terrenoId = t.terrenoId
          AND pt.activo = 1
          AND p.activo = 1
        ORDER BY pt.fechaAsignacionUtc DESC, pt.propietarioTerrenoId DESC
    ) propietarioActual
    WHERE d.Activo = 1
      AND (@desde IS NULL OR d.FechaSolicitudUtc >= @desde)
      AND (@hasta IS NULL OR d.FechaSolicitudUtc < @hasta)
      AND (@tecnicoId IS NULL OR d.UsuarioSolicitanteId = @tecnicoId)
      AND (@estado = N'' OR UPPER(ISNULL(d.Estado, N'')) = @estado)
      AND (@departamento = N'' OR ISNULL(dep.NombreDepartamento, N'') = @departamento)
      AND
      (
          @buscar = N''
          OR CONVERT(NVARCHAR(20), d.DiagnosticoIAId) LIKE N'%' + @buscar + N'%'
          OR ISNULL(d.NombreInspeccion, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(d.CodigoTerreno, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(tecnico.nombreCompletoUsuario, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(tecnico.nombreUsuario, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(propietarioActual.NombreCompleto, N'') LIKE N'%' + @buscar + N'%'
      )
)
SELECT
    COUNT(1) OVER() AS TotalRegistros,
    *
FROM Datos
ORDER BY FechaSolicitudUtc DESC, DiagnosticoIAId DESC
OFFSET @offset ROWS
FETCH NEXT @tamano ROWS ONLY;
""";

            ControlFitosanitarioPaginaDto resultado =
                await EjecutarAsync(async conexion =>
                {
                    await using DbCommand comando = CrearComando(conexion, sql);
                    AgregarParametro(comando, "@desde", desde);
                    AgregarParametro(comando, "@hasta", hastaExclusiva);
                    AgregarParametro(comando, "@tecnicoId", tecnicoId);
                    AgregarParametro(comando, "@estado", estadoNormalizado);
                    AgregarParametro(comando, "@departamento", departamentoNormalizado);
                    AgregarParametro(comando, "@buscar", buscarNormalizado);
                    AgregarParametro(comando, "@offset", offset);
                    AgregarParametro(comando, "@tamano", tamanoPagina);

                    var paginaResultado = new ControlFitosanitarioPaginaDto
                    {
                        Pagina = pagina,
                        TamanoPagina = tamanoPagina
                    };

                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (paginaResultado.TotalRegistros == 0)
                            paginaResultado.TotalRegistros = Entero(reader, 0);

                        paginaResultado.Items.Add(new ControlFitosanitarioItemDto
                        {
                            InspeccionId = reader.GetInt32(1),
                            NombreInspeccion = Texto(reader, 2),
                            CodigoTerreno = Texto(reader, 3),
                            Propietario = Texto(reader, 4),
                            Departamento = Texto(reader, 5),
                            UsuarioTecnicoId = reader.GetInt32(6),
                            Tecnico = Texto(reader, 7),
                            FechaRegistroSistemaUtc = Utc(reader.GetDateTime(8)),
                            Estado = Texto(reader, 9),
                            EtapaTecnicaFinalizada = reader.GetBoolean(10),
                            CerradaDefinitiva = reader.GetBoolean(11),
                            TotalFotografias = Entero(reader, 12),
                            Pendientes = Entero(reader, 13),
                            ConError = Entero(reader, 14),
                            Finalizadas = Entero(reader, 15),
                            UsuarioAnalizadorId = NullableInt(reader, 16),
                            Analizador = Texto(reader, 17),
                            UsuarioAprobadorId = NullableInt(reader, 18),
                            Aprobador = Texto(reader, 19),
                            BloqueoAnalizadorUsuarioId = NullableInt(reader, 20),
                            BloqueoAnalizadorUsuario = Texto(reader, 21),
                            BloqueoAnalizadorExpiraUtc = NullableUtc(reader, 22),
                            BloqueoAprobadorUsuarioId = NullableInt(reader, 23),
                            BloqueoAprobadorUsuario = Texto(reader, 24),
                            BloqueoAprobadorExpiraUtc = NullableUtc(reader, 25),
                            UltimaActividadUtc = NullableUtc(reader, 26)
                        });
                    }

                    return paginaResultado;
                }, cancellationToken);

            return Ok(Exito(
                "Inspecciones para control obtenidas correctamente.",
                resultado));
        }

        [HttpGet("usuarios")]
        public async Task<IActionResult> ObtenerUsuariosEtapa(
            [FromQuery] string etapa,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            string solicitado = (etapa ?? string.Empty).Trim().ToUpperInvariant();

            List<ControlFitosanitarioUsuarioDto> usuarios;

            if (solicitado == "TECNICO")
            {
                /*
                 * Para el filtro administrativo se muestran solamente usuarios
                 * que realmente han registrado inspecciones activas. No se
                 * supone ningún nombre de rol.
                 */
                int[] tecnicosIds = await diagnosticoDb.Diagnosticos
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .Select(item => item.UsuarioSolicitanteId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);

                usuarios = await db.Usuarios
                    .AsNoTracking()
                    .Where(item =>
                        item.activo && tecnicosIds.Contains(item.UsuarioId))
                    .OrderBy(item => item.nombreCompletoUsuario)
                    .ThenBy(item => item.nombreUsuario)
                    .Select(item => new ControlFitosanitarioUsuarioDto
                    {
                        UsuarioId = item.UsuarioId,
                        NombreCompleto = item.nombreCompletoUsuario,
                        NombreUsuario = item.nombreUsuario
                    })
                    .ToListAsync(cancellationToken);
            }
            else
            {
                string etapaNormalizada = NormalizarEtapa(solicitado);
                if (string.IsNullOrWhiteSpace(etapaNormalizada))
                    return BadRequest(Error("La etapa indicada no es válida."));

                string interfaz = etapaNormalizada == "ANALIZADOR"
                    ? DiagnosticoIAFlujo.InterfazAnalizador
                    : DiagnosticoIAFlujo.InterfazAprobador;

                usuarios = await (
                    from usuario in db.Usuarios.AsNoTracking()
                    join rolInterfaz in db.RolInterfaz.AsNoTracking()
                        on usuario.rolId equals rolInterfaz.rolId
                    join interfazDb in db.Interfaz.AsNoTracking()
                        on rolInterfaz.interfazId equals interfazDb.interfazId
                    where usuario.activo &&
                          interfazDb.activo &&
                          interfazDb.nombreInterfaz == interfaz &&
                          rolInterfaz.actualizar == true
                    orderby usuario.nombreCompletoUsuario, usuario.nombreUsuario
                    select new ControlFitosanitarioUsuarioDto
                    {
                        UsuarioId = usuario.UsuarioId,
                        NombreCompleto = usuario.nombreCompletoUsuario,
                        NombreUsuario = usuario.nombreUsuario
                    })
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            return Ok(Exito(
                "Usuarios autorizados para la etapa obtenidos correctamente.",
                usuarios));
        }

        [HttpGet("{id:int}/auditoria")]
        public async Task<IActionResult> ObtenerAuditoria(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            if (id <= 0)
                return BadRequest(Error("La inspección indicada no es válida."));

            ControlFitosanitarioAuditoriaDto? auditoria =
                await CargarAuditoriaAsync(id, cancellationToken);

            return auditoria == null
                ? NotFound(Error("No se encontró la inspección indicada."))
                : Ok(Exito(
                    "Trazabilidad fitosanitaria obtenida correctamente.",
                    auditoria));
        }

        [HttpGet("rendimiento-ia")]
        public async Task<IActionResult> ObtenerRendimientoIA(
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            (DateTime? desde, DateTime? hastaExclusiva, IActionResult? error) =
                ValidarFechas(fechaDesde, fechaHasta);
            if (error != null)
                return error;

            const string sql = """
IF OBJECT_ID(N'tempdb..#datosRendimientoIA', N'U') IS NOT NULL
    DROP TABLE #datosRendimientoIA;

SELECT
    i.DiagnosticoIAImagenId,
    ISNULL(NULLIF(LTRIM(RTRIM(i.ModeloIAUtilizado)), N''), N'Sin modelo') AS Modelo,
    UPPER(LTRIM(RTRIM(ISNULL(r.DiagnosticoProbable, N'')))) AS DiagnosticoIA,
    UPPER(LTRIM(RTRIM(ISNULL(ap.DiagnosticoFinal, N'')))) AS DiagnosticoFinal,
    UPPER(ISNULL(ap.Decision, N'')) AS Decision,
    UPPER(ISNULL(i.Estado, N'')) AS Estado,
    i.FechaAnalisisIAUtc
INTO #datosRendimientoIA
FROM dbo.diagnosticoIAImagen i
INNER JOIN dbo.diagnosticoIA d
    ON d.DiagnosticoIAId = i.DiagnosticoIAId
LEFT JOIN dbo.diagnosticoIAImagenResultadoIA r
    ON r.DiagnosticoIAImagenId = i.DiagnosticoIAImagenId
OUTER APPLY
(
    SELECT TOP(1)
        a.DiagnosticoFinal,
        a.Decision
    FROM dbo.diagnosticoIAImagenAprobacionV2 a
    WHERE a.DiagnosticoIAImagenId = i.DiagnosticoIAImagenId
    ORDER BY a.FechaAprobacionUtc DESC,
             a.DiagnosticoIAImagenAprobacionId DESC
) ap
WHERE d.Activo = 1
  AND ISNULL(i.Activo, 1) = 1
  AND i.FechaAnalisisIAUtc IS NOT NULL
  AND (@desde IS NULL OR i.FechaAnalisisIAUtc >= @desde)
  AND (@hasta IS NULL OR i.FechaAnalisisIAUtc < @hasta);

SELECT
    COUNT(1) AS FotografiasAnalizadas,
    SUM(CASE WHEN DiagnosticoFinal <> N'' THEN 1 ELSE 0 END) AS ConResultadoFinal,
    SUM(CASE WHEN DiagnosticoFinal <> N'' AND DiagnosticoIA = DiagnosticoFinal
        THEN 1 ELSE 0 END) AS CoincidenciasExactas,
    SUM(CASE WHEN DiagnosticoFinal <> N'' AND DiagnosticoIA <> DiagnosticoFinal
        THEN 1 ELSE 0 END) AS CorregidasPorHumano,
    SUM(CASE WHEN Estado = N'NO_CONCLUYENTE' THEN 1 ELSE 0 END) AS NoConcluyentes,
    SUM(CASE WHEN Estado = N'RECHAZADA' THEN 1 ELSE 0 END) AS Rechazadas,
    SUM(CASE WHEN Estado = N'ERROR_IA' THEN 1 ELSE 0 END) AS ErroresIA
FROM #datosRendimientoIA;

SELECT
    Modelo,
    COUNT(1) AS FotografiasAnalizadas,
    SUM(CASE WHEN DiagnosticoFinal <> N'' THEN 1 ELSE 0 END) AS ConResultadoFinal,
    SUM(CASE WHEN DiagnosticoFinal <> N'' AND DiagnosticoIA = DiagnosticoFinal
        THEN 1 ELSE 0 END) AS CoincidenciasExactas,
    SUM(CASE WHEN DiagnosticoFinal <> N'' AND DiagnosticoIA <> DiagnosticoFinal
        THEN 1 ELSE 0 END) AS CorregidasPorHumano
FROM #datosRendimientoIA
GROUP BY Modelo
ORDER BY FotografiasAnalizadas DESC, Modelo;

DROP TABLE #datosRendimientoIA;
""";

            ControlFitosanitarioRendimientoIADto data =
                await EjecutarAsync(async conexion =>
                {
                    await using DbCommand comando = CrearComando(conexion, sql);
                    AgregarParametro(comando, "@desde", desde);
                    AgregarParametro(comando, "@hasta", hastaExclusiva);

                    var resultado = new ControlFitosanitarioRendimientoIADto();
                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);

                    if (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.FotografiasAnalizadas = Entero(reader, 0);
                        resultado.FotografiasConResultadoFinal = Entero(reader, 1);
                        resultado.CoincidenciasExactas = Entero(reader, 2);
                        resultado.CorregidasPorHumano = Entero(reader, 3);
                        resultado.NoConcluyentes = Entero(reader, 4);
                        resultado.Rechazadas = Entero(reader, 5);
                        resultado.ErroresIA = Entero(reader, 6);
                        resultado.PorcentajeCoincidencia = Porcentaje(
                            resultado.CoincidenciasExactas,
                            resultado.FotografiasConResultadoFinal);
                    }

                    if (await reader.NextResultAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            int finales = Entero(reader, 2);
                            int coincidencias = Entero(reader, 3);
                            resultado.Modelos.Add(new ControlFitosanitarioModeloIADto
                            {
                                Modelo = Texto(reader, 0),
                                FotografiasAnalizadas = Entero(reader, 1),
                                ConResultadoFinal = finales,
                                CoincidenciasExactas = coincidencias,
                                CorregidasPorHumano = Entero(reader, 4),
                                PorcentajeCoincidencia = Porcentaje(
                                    coincidencias,
                                    finales)
                            });
                        }
                    }

                    return resultado;
                }, cancellationToken);

            return Ok(Exito(
                "Rendimiento de la IA obtenido correctamente.",
                data));
        }

        [HttpPost("{id:int}/reasignar")]
        public async Task<IActionResult> Reasignar(
            int id,
            [FromBody] ControlFitosanitarioReasignarRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            if (request == null || id <= 0)
                return BadRequest(Error("La solicitud de reasignación no es válida."));

            string etapa = NormalizarEtapa(request.Etapa);
            if (string.IsNullOrWhiteSpace(etapa))
                return BadRequest(Error("Seleccione analizador o aprobador."));

            string motivo = (request.Motivo ?? string.Empty).Trim();
            if (motivo.Length is < 8 or > 1000)
            {
                return BadRequest(Error(
                    "El motivo administrativo debe contener entre 8 y 1000 caracteres."));
            }

            string interfazObjetivo = etapa == "ANALIZADOR"
                ? DiagnosticoIAFlujo.InterfazAnalizador
                : DiagnosticoIAFlujo.InterfazAprobador;

            ResultadoPermisoApi permisoDestino = await permisos.ValidarAsync(
                request.UsuarioNuevoId,
                interfazObjetivo,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permisoDestino.Permitido)
            {
                return BadRequest(Error(
                    "El usuario seleccionado no posee permiso de actualización para esa etapa."));
            }

            int usuarioEjecutorId = ObtenerUsuarioId()!.Value;

            ControlFitosanitarioOperacionDto operacion =
                await ReasignarAsync(
                    id,
                    etapa,
                    request.UsuarioNuevoId,
                    motivo,
                    usuarioEjecutorId,
                    cancellationToken);

            if (operacion.InspeccionId == 0)
            {
                return Conflict(Error(
                    "La inspección no existe, está cerrada o la asignación ya corresponde al usuario indicado."));
            }

            return Ok(Exito(
                $"La etapa de {etapa.ToLowerInvariant()} fue reasignada y la acción quedó auditada.",
                operacion));
        }

        [HttpPost("{id:int}/liberar-bloqueo")]
        public async Task<IActionResult> LiberarBloqueo(
            int id,
            [FromBody] ControlFitosanitarioLiberarBloqueoRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            if (request == null || id <= 0)
                return BadRequest(Error("La solicitud de liberación no es válida."));

            string etapa = NormalizarEtapa(request.Etapa);
            if (string.IsNullOrWhiteSpace(etapa))
                return BadRequest(Error("Seleccione analizador o aprobador."));

            string motivo = (request.Motivo ?? string.Empty).Trim();
            if (motivo.Length is < 8 or > 1000)
            {
                return BadRequest(Error(
                    "El motivo administrativo debe contener entre 8 y 1000 caracteres."));
            }

            int usuarioEjecutorId = ObtenerUsuarioId()!.Value;
            ControlFitosanitarioOperacionDto operacion =
                await LiberarBloqueoAsync(
                    id,
                    etapa,
                    motivo,
                    usuarioEjecutorId,
                    cancellationToken);

            if (operacion.InspeccionId == 0)
            {
                return Conflict(Error(
                    "La inspección no tiene un bloqueo activo para la etapa indicada."));
            }

            return Ok(Exito(
                "El bloqueo fue liberado y la acción quedó registrada en la auditoría.",
                operacion));
        }

        private async Task<ControlFitosanitarioAuditoriaDto?>
            CargarAuditoriaAsync(
                int inspeccionId,
                CancellationToken cancellationToken)
        {
            const string sql = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();

SELECT TOP(1)
    d.DiagnosticoIAId,
    ISNULL(NULLIF(LTRIM(RTRIM(d.NombreInspeccion)), N''),
        N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId)) AS NombreInspeccion,
    ISNULL(d.CodigoTerreno, N'') AS CodigoTerreno,
    d.UsuarioSolicitanteId,
    ISNULL(NULLIF(LTRIM(RTRIM(t.nombreCompletoUsuario)), N''),
        ISNULL(t.nombreUsuario, N'')) AS Tecnico,
    d.FechaSolicitudUtc,
    UPPER(ISNULL(d.Estado, N'BORRADOR')) AS Estado,
    CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0)),
    d.FechaFinEtapaTecnicaUtc,
    CONVERT(BIT, ISNULL(d.CerradaDefinitiva, 0)),
    d.FechaCierreDefinitivoUtc,
    a.UsuarioAnalizadorId,
    ISNULL(NULLIF(LTRIM(RTRIM(ua.nombreCompletoUsuario)), N''),
        ISNULL(ua.nombreUsuario, N'')) AS Analizador,
    a.UsuarioAprobadorId,
    ISNULL(NULLIF(LTRIM(RTRIM(up.nombreCompletoUsuario)), N''),
        ISNULL(up.nombreUsuario, N'')) AS Aprobador
FROM dbo.diagnosticoIA d
LEFT JOIN dbo.usuario t ON t.UsuarioId = d.UsuarioSolicitanteId
LEFT JOIN dbo.diagnosticoIAAsignacionFlujo a
    ON a.DiagnosticoIAId = d.DiagnosticoIAId
LEFT JOIN dbo.usuario ua ON ua.UsuarioId = a.UsuarioAnalizadorId
LEFT JOIN dbo.usuario up ON up.UsuarioId = a.UsuarioAprobadorId
WHERE d.DiagnosticoIAId = @id
  AND d.Activo = 1;

SELECT
    bl.Etapa,
    bl.UsuarioId,
    ISNULL(NULLIF(LTRIM(RTRIM(u.nombreCompletoUsuario)), N''),
        ISNULL(u.nombreUsuario, N'')) AS Usuario,
    bl.FechaAdquisicionUtc,
    bl.UltimoHeartbeatUtc,
    bl.ExpiraUtc
FROM dbo.diagnosticoIAEdicionBloqueo bl
LEFT JOIN dbo.usuario u ON u.UsuarioId = bl.UsuarioId
WHERE bl.DiagnosticoIAId = @id
  AND bl.ExpiraUtc > @ahora
ORDER BY bl.Etapa;

SELECT
    i.DiagnosticoIAImagenId,
    i.Orden,
    ISNULL(i.TipoFotografia, N'') AS TipoFotografia,
    ISNULL(i.UrlImagen, N'') AS UrlImagen,
    UPPER(ISNULL(i.Estado, N'')) AS Estado,
    i.FechaIdentificacionCampo,
    i.FechaAnalisisIAUtc,
    i.FechaAnalisisHumanoUtc,
    i.FechaAprobacionUtc,
    ISNULL(i.ModeloIAUtilizado, N'') AS ModeloIA,
    ISNULL(i.IntentosIA, 0) AS IntentosIA,
    ISNULL(r.DiagnosticoProbable, N'') AS DiagnosticoIA,
    ISNULL(h.Diagnostico, N'') AS DiagnosticoHumano,
    ISNULL(ap.DiagnosticoFinal, N'') AS DiagnosticoFinal,
    ISNULL(ap.Decision, N'') AS DecisionFinal
FROM dbo.diagnosticoIAImagen i
LEFT JOIN dbo.diagnosticoIAImagenResultadoIA r
    ON r.DiagnosticoIAImagenId = i.DiagnosticoIAImagenId
OUTER APPLY
(
    SELECT TOP(1) ah.Diagnostico
    FROM dbo.diagnosticoIAImagenAnalisisHumano ah
    WHERE ah.DiagnosticoIAImagenId = i.DiagnosticoIAImagenId
    ORDER BY ah.Version DESC,
             ah.DiagnosticoIAImagenAnalisisHumanoId DESC
) h
OUTER APPLY
(
    SELECT TOP(1) a.DiagnosticoFinal, a.Decision
    FROM dbo.diagnosticoIAImagenAprobacionV2 a
    WHERE a.DiagnosticoIAImagenId = i.DiagnosticoIAImagenId
    ORDER BY a.FechaAprobacionUtc DESC,
             a.DiagnosticoIAImagenAprobacionId DESC
) ap
WHERE i.DiagnosticoIAId = @id
ORDER BY i.Orden, i.DiagnosticoIAImagenId;

SELECT
    eventos.FechaUtc,
    eventos.Tipo,
    eventos.FotografiaId,
    eventos.UsuarioId,
    eventos.Usuario,
    eventos.Accion,
    eventos.EstadoAnterior,
    eventos.EstadoNuevo,
    eventos.Detalle
FROM
(
    SELECT
        h.FechaUtc,
        CAST(N'INSPECCION' AS NVARCHAR(20)) AS Tipo,
        CAST(NULL AS INT) AS FotografiaId,
        h.UsuarioId,
        ISNULL(NULLIF(LTRIM(RTRIM(u.nombreCompletoUsuario)), N''),
            ISNULL(u.nombreUsuario, N'')) AS Usuario,
        h.Accion,
        h.EstadoAnterior,
        h.EstadoNuevo,
        h.Detalle
    FROM dbo.diagnosticoIAHistorial h
    LEFT JOIN dbo.usuario u ON u.UsuarioId = h.UsuarioId
    WHERE h.DiagnosticoIAId = @id

    UNION ALL

    SELECT
        h.FechaUtc,
        CAST(N'FOTOGRAFIA' AS NVARCHAR(20)) AS Tipo,
        h.DiagnosticoIAImagenId AS FotografiaId,
        h.UsuarioId,
        ISNULL(NULLIF(LTRIM(RTRIM(u.nombreCompletoUsuario)), N''),
            ISNULL(u.nombreUsuario, N'')) AS Usuario,
        h.Accion,
        h.EstadoAnterior,
        h.EstadoNuevo,
        h.Detalle
    FROM dbo.diagnosticoIAImagenHistorialV2 h
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
    LEFT JOIN dbo.usuario u ON u.UsuarioId = h.UsuarioId
    WHERE i.DiagnosticoIAId = @id

    UNION ALL

    SELECT
        h.FechaUtc,
        CAST(N'ADMINISTRACION' AS NVARCHAR(20)) AS Tipo,
        CAST(NULL AS INT) AS FotografiaId,
        h.UsuarioEjecutorId AS UsuarioId,
        ISNULL(NULLIF(LTRIM(RTRIM(u.nombreCompletoUsuario)), N''),
            ISNULL(u.nombreUsuario, N'')) AS Usuario,
        h.Accion,
        CAST(N'' AS NVARCHAR(40)) AS EstadoAnterior,
        CAST(N'' AS NVARCHAR(40)) AS EstadoNuevo,
        h.Detalle + CASE WHEN LEN(LTRIM(RTRIM(h.Motivo))) > 0
            THEN N' Motivo: ' + h.Motivo ELSE N'' END AS Detalle
    FROM dbo.diagnosticoIAAdministracionHistorial h
    LEFT JOIN dbo.usuario u ON u.UsuarioId = h.UsuarioEjecutorId
    WHERE h.DiagnosticoIAId = @id
) eventos
ORDER BY eventos.FechaUtc DESC;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                var data = new ControlFitosanitarioAuditoriaDto
                {
                    InspeccionId = reader.GetInt32(0),
                    NombreInspeccion = Texto(reader, 1),
                    CodigoTerreno = Texto(reader, 2),
                    UsuarioTecnicoId = reader.GetInt32(3),
                    Tecnico = Texto(reader, 4),
                    FechaRegistroSistemaUtc = Utc(reader.GetDateTime(5)),
                    Estado = Texto(reader, 6),
                    EtapaTecnicaFinalizada = reader.GetBoolean(7),
                    FechaFinEtapaTecnicaUtc = NullableUtc(reader, 8),
                    CerradaDefinitiva = reader.GetBoolean(9),
                    FechaCierreDefinitivoUtc = NullableUtc(reader, 10),
                    UsuarioAnalizadorId = NullableInt(reader, 11),
                    Analizador = Texto(reader, 12),
                    UsuarioAprobadorId = NullableInt(reader, 13),
                    Aprobador = Texto(reader, 14)
                };

                if (await reader.NextResultAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var bloqueo = new ControlFitosanitarioBloqueoDto
                        {
                            Etapa = Texto(reader, 0),
                            UsuarioId = reader.GetInt32(1),
                            Usuario = Texto(reader, 2),
                            FechaAdquisicionUtc = Utc(reader.GetDateTime(3)),
                            UltimoHeartbeatUtc = Utc(reader.GetDateTime(4)),
                            ExpiraUtc = Utc(reader.GetDateTime(5))
                        };

                        if (bloqueo.Etapa.Equals(
                                "ANALIZADOR",
                                StringComparison.OrdinalIgnoreCase))
                            data.BloqueoAnalizador = bloqueo;
                        else if (bloqueo.Etapa.Equals(
                                     "APROBADOR",
                                     StringComparison.OrdinalIgnoreCase))
                            data.BloqueoAprobador = bloqueo;
                    }
                }

                if (await reader.NextResultAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        string ia = Texto(reader, 11);
                        string final = Texto(reader, 13);
                        data.Fotografias.Add(new ControlFitosanitarioFotoComparacionDto
                        {
                            FotografiaId = reader.GetInt32(0),
                            Orden = reader.GetInt32(1),
                            TipoFotografia = Texto(reader, 2),
                            UrlImagen = Texto(reader, 3),
                            Estado = Texto(reader, 4),
                            FechaIdentificacionCampo = reader.IsDBNull(5)
                                ? null
                                : reader.GetDateTime(5).Date,
                            FechaAnalisisIAUtc = NullableUtc(reader, 6),
                            FechaAnalisisHumanoUtc = NullableUtc(reader, 7),
                            FechaAprobacionUtc = NullableUtc(reader, 8),
                            ModeloIA = Texto(reader, 9),
                            IntentosIA = Entero(reader, 10),
                            DiagnosticoIA = ia,
                            DiagnosticoHumano = Texto(reader, 12),
                            DiagnosticoFinal = final,
                            DecisionFinal = Texto(reader, 14),
                            CoincidenciaIAFinal = !string.IsNullOrWhiteSpace(ia) &&
                                !string.IsNullOrWhiteSpace(final) &&
                                string.Equals(
                                    ia.Trim(),
                                    final.Trim(),
                                    StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }

                if (await reader.NextResultAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        data.Eventos.Add(new ControlFitosanitarioEventoDto
                        {
                            FechaUtc = Utc(reader.GetDateTime(0)),
                            Tipo = Texto(reader, 1),
                            FotografiaId = NullableInt(reader, 2),
                            UsuarioId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            Usuario = Texto(reader, 4),
                            Accion = Texto(reader, 5),
                            EstadoAnterior = Texto(reader, 6),
                            EstadoNuevo = Texto(reader, 7),
                            Detalle = Texto(reader, 8)
                        });
                    }
                }

                return data;
            }, cancellationToken);
        }

        private async Task<ControlFitosanitarioOperacionDto> ReasignarAsync(
            int inspeccionId,
            string etapa,
            int usuarioNuevoId,
            string motivo,
            int usuarioEjecutorId,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = diagnosticoDb.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            await using DbTransaction transaccion =
                await conexion.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                const string validar = """
SELECT TOP(1)
    DiagnosticoIAId,
    UPPER(ISNULL(Estado, N'')) AS Estado,
    CONVERT(BIT, ISNULL(CerradaDefinitiva, 0)) AS CerradaDefinitiva
FROM dbo.diagnosticoIA WITH (UPDLOCK, HOLDLOCK)
WHERE DiagnosticoIAId = @id
  AND Activo = 1;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    validar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken) ||
                        reader.GetBoolean(2))
                    {
                        await transaccion.RollbackAsync(cancellationToken);
                        return new();
                    }
                }

                const string asegurar = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAAsignacionFlujo WITH (UPDLOCK, HOLDLOCK)
    WHERE DiagnosticoIAId = @id
)
BEGIN
    INSERT INTO dbo.diagnosticoIAAsignacionFlujo
    (
        DiagnosticoIAId,
        FechaModificacionUtc
    )
    VALUES
    (
        @id,
        SYSUTCDATETIME()
    );
END;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    asegurar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                int? anteriorId;
                const string leerAsignacion = """
SELECT UsuarioAnalizadorId, UsuarioAprobadorId
FROM dbo.diagnosticoIAAsignacionFlujo WITH (UPDLOCK, HOLDLOCK)
WHERE DiagnosticoIAId = @id;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    leerAsignacion,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    anteriorId = etapa == "ANALIZADOR"
                        ? NullableInt(reader, 0)
                        : NullableInt(reader, 1);
                }

                if (anteriorId == usuarioNuevoId)
                {
                    await transaccion.RollbackAsync(cancellationToken);
                    return new();
                }

                string actualizar = etapa == "ANALIZADOR"
                    ? """
UPDATE dbo.diagnosticoIAAsignacionFlujo
SET UsuarioAnalizadorId = @nuevo,
    FechaAsignacionAnalizadorUtc = SYSUTCDATETIME(),
    FechaModificacionUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAId = @id;
"""
                    : """
UPDATE dbo.diagnosticoIAAsignacionFlujo
SET UsuarioAprobadorId = @nuevo,
    FechaAsignacionAprobadorUtc = SYSUTCDATETIME(),
    FechaModificacionUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAId = @id;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    actualizar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@nuevo", usuarioNuevoId);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                const string quitarBloqueo = """
DELETE FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    quitarBloqueo,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@etapa", etapa);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                string anterior = await ObtenerNombreUsuarioAsync(
                    conexion,
                    transaccion,
                    anteriorId,
                    cancellationToken);
                string nuevo = await ObtenerNombreUsuarioAsync(
                    conexion,
                    transaccion,
                    usuarioNuevoId,
                    cancellationToken);

                string detalle = string.IsNullOrWhiteSpace(anterior)
                    ? $"Se asignó {etapa.ToLowerInvariant()} a {nuevo}."
                    : $"Se reasignó {etapa.ToLowerInvariant()} de {anterior} a {nuevo}.";

                await RegistrarAccionAdministrativaAsync(
                    conexion,
                    transaccion,
                    inspeccionId,
                    usuarioEjecutorId,
                    "REASIGNACION_" + etapa,
                    etapa,
                    anteriorId,
                    usuarioNuevoId,
                    motivo,
                    detalle,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return new ControlFitosanitarioOperacionDto
                {
                    InspeccionId = inspeccionId,
                    Etapa = etapa,
                    UsuarioAnteriorId = anteriorId,
                    UsuarioAnterior = anterior,
                    UsuarioNuevoId = usuarioNuevoId,
                    UsuarioNuevo = nuevo,
                    Motivo = motivo,
                    FechaUtc = DateTime.UtcNow
                };
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<ControlFitosanitarioOperacionDto> LiberarBloqueoAsync(
            int inspeccionId,
            string etapa,
            string motivo,
            int usuarioEjecutorId,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = diagnosticoDb.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            await using DbTransaction transaccion =
                await conexion.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                const string obtener = """
SELECT TOP(1) UsuarioId
FROM dbo.diagnosticoIAEdicionBloqueo WITH (UPDLOCK, HOLDLOCK)
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND ExpiraUtc > SYSUTCDATETIME();
""";

                int? anteriorId;
                await using (DbCommand comando = CrearComando(
                    conexion,
                    obtener,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@etapa", etapa);
                    object? value = await comando.ExecuteScalarAsync(cancellationToken);
                    anteriorId = value == null || value == DBNull.Value
                        ? null
                        : Convert.ToInt32(value);
                }

                if (!anteriorId.HasValue)
                {
                    await transaccion.RollbackAsync(cancellationToken);
                    return new();
                }

                const string eliminar = """
DELETE FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    eliminar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@etapa", etapa);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                string anterior = await ObtenerNombreUsuarioAsync(
                    conexion,
                    transaccion,
                    anteriorId,
                    cancellationToken);
                string detalle =
                    $"Se liberó administrativamente el bloqueo de {etapa.ToLowerInvariant()} que pertenecía a {anterior}.";

                await RegistrarAccionAdministrativaAsync(
                    conexion,
                    transaccion,
                    inspeccionId,
                    usuarioEjecutorId,
                    "LIBERACION_BLOQUEO_" + etapa,
                    etapa,
                    anteriorId,
                    null,
                    motivo,
                    detalle,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return new ControlFitosanitarioOperacionDto
                {
                    InspeccionId = inspeccionId,
                    Etapa = etapa,
                    UsuarioAnteriorId = anteriorId,
                    UsuarioAnterior = anterior,
                    Motivo = motivo,
                    FechaUtc = DateTime.UtcNow
                };
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task RegistrarAccionAdministrativaAsync(
            DbConnection conexion,
            DbTransaction transaccion,
            int inspeccionId,
            int usuarioEjecutorId,
            string accion,
            string etapa,
            int? usuarioAnteriorId,
            int? usuarioNuevoId,
            string motivo,
            string detalle,
            CancellationToken cancellationToken)
        {
            const string sql = """
INSERT INTO dbo.diagnosticoIAAdministracionHistorial
(
    DiagnosticoIAId,
    UsuarioEjecutorId,
    Accion,
    Etapa,
    UsuarioAnteriorId,
    UsuarioNuevoId,
    Motivo,
    Detalle,
    FechaUtc
)
VALUES
(
    @id,
    @ejecutor,
    @accion,
    @etapa,
    @anterior,
    @nuevo,
    @motivo,
    @detalle,
    SYSUTCDATETIME()
);

INSERT INTO dbo.diagnosticoIAHistorial
(
    DiagnosticoIAId,
    UsuarioId,
    EstadoAnterior,
    EstadoNuevo,
    Accion,
    Detalle,
    FechaUtc
)
SELECT
    d.DiagnosticoIAId,
    @ejecutor,
    ISNULL(d.Estado, N''),
    ISNULL(d.Estado, N''),
    @accion,
    @detalle + N' Motivo: ' + @motivo,
    SYSUTCDATETIME()
FROM dbo.diagnosticoIA d
WHERE d.DiagnosticoIAId = @id;
""";

            await using DbCommand comando = CrearComando(
                conexion,
                sql,
                transaccion);
            AgregarParametro(comando, "@id", inspeccionId);
            AgregarParametro(comando, "@ejecutor", usuarioEjecutorId);
            AgregarParametro(comando, "@accion", accion);
            AgregarParametro(comando, "@etapa", etapa);
            AgregarParametro(comando, "@anterior", usuarioAnteriorId);
            AgregarParametro(comando, "@nuevo", usuarioNuevoId);
            AgregarParametro(comando, "@motivo", motivo);
            AgregarParametro(comando, "@detalle", detalle);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<string> ObtenerNombreUsuarioAsync(
            DbConnection conexion,
            DbTransaction transaccion,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (usuarioId is not > 0)
                return string.Empty;

            const string sql = """
SELECT TOP(1)
    ISNULL(NULLIF(LTRIM(RTRIM(nombreCompletoUsuario)), N''),
        ISNULL(nombreUsuario, N''))
FROM dbo.usuario
WHERE UsuarioId = @usuarioId;
""";

            await using DbCommand comando = CrearComando(
                conexion,
                sql,
                transaccion);
            AgregarParametro(comando, "@usuarioId", usuarioId);
            object? valor = await comando.ExecuteScalarAsync(cancellationToken);
            return valor == null || valor == DBNull.Value
                ? $"usuario #{usuarioId.Value}"
                : Convert.ToString(valor)?.Trim() ?? $"usuario #{usuarioId.Value}";
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            await InicializarAsync(cancellationToken);

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                ObtenerUsuarioId(),
                InspeccionFitosanitariaAdministracionDatabaseInitializer
                    .InterfazControl,
                tipo,
                cancellationToken);

            if (permiso.Permitido)
                return null;

            return StatusCode(
                permiso.CodigoEstado,
                Error(permiso.Mensaje));
        }

        private async Task InicializarAsync(CancellationToken cancellationToken)
        {
            await flujo.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);
            await bloqueos.InicializarAsync(cancellationToken);
            await administracion.InicializarAsync(cancellationToken);
        }

        private (DateTime? Desde, DateTime? HastaExclusiva, IActionResult? Error)
            ValidarFechas(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            DateTime? desde = fechaDesde?.Date;
            DateTime? hasta = fechaHasta?.Date;
            DateTime hoy = DateTime.UtcNow.Date;

            if (desde.HasValue && desde.Value > hoy)
                return (null, null, BadRequest(Error(
                    "La fecha inicial no puede estar en el futuro.")));

            if (hasta.HasValue && hasta.Value > hoy)
                return (null, null, BadRequest(Error(
                    "La fecha final no puede estar en el futuro.")));

            if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
                return (null, null, BadRequest(Error(
                    "La fecha inicial debe ser anterior o igual a la fecha final.")));

            return (desde, hasta?.AddDays(1), null);
        }

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                            User.FindFirstValue("usuarioId") ??
                            User.FindFirstValue("uid") ??
                            User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private async Task<T> EjecutarAsync<T>(
            Func<DbConnection, Task<T>> accion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = diagnosticoDb.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                return await accion(conexion);
            }
            finally
            {
                if (cerrar && diagnosticoDb.Database.CurrentTransaction == null)
                    await conexion.CloseAsync();
            }
        }

        private DbCommand CrearComando(
            DbConnection conexion,
            string sql,
            DbTransaction? transaccion = null)
        {
            DbCommand comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 180;
            comando.Transaction = transaccion ??
                diagnosticoDb.Database.CurrentTransaction?.GetDbTransaction();
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

        private static int Entero(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(reader.GetValue(ordinal));

        private static int? NullableInt(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(reader.GetValue(ordinal));

        private static string Texto(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal)
                ? string.Empty
                : Convert.ToString(reader.GetValue(ordinal))?.Trim() ??
                  string.Empty;

        private static DateTime Utc(DateTime value) =>
            DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static DateTime? NullableUtc(
            DbDataReader reader,
            int ordinal) =>
            reader.IsDBNull(ordinal)
                ? null
                : Utc(reader.GetDateTime(ordinal));

        private static string NormalizarEtapa(string? etapa) =>
            (etapa ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "ANALIZADOR" => "ANALIZADOR",
                "APROBADOR" => "APROBADOR",
                _ => string.Empty
            };

        private static string NormalizarCodigo(string? valor) =>
            (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_');

        private static decimal Porcentaje(int parte, int total) =>
            total <= 0
                ? 0m
                : Math.Round(parte * 100m / total, 2);

        private static object Exito<T>(string mensaje, T data) => new
        {
            success = true,
            message = mensaje,
            data
        };

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
