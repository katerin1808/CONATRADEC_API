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
    /// Bandeja unificada del flujo fitosanitario. Mis inspecciones, decisiones,
    /// analizador, aprobador e historial comparten el mismo cálculo de estado
    /// por fotografía para evitar resultados distintos entre vistas.
    ///
    /// Las bandejas de analizador y aprobador muestran tanto expedientes sin
    /// asignar como expedientes asignados a otros responsables. La asignación
    /// controla la edición, no la consulta; así todos los usuarios con permiso
    /// pueden revisar el contexto sin apropiarse del expediente.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/revision-fitosanitaria")]
    public sealed class InspeccionFitosanitariaBandejaRevisionController :
        ControllerBase
    {
        private readonly DBContext db;
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;

        public InspeccionFitosanitariaBandejaRevisionController(
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
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(
                diagnosticoDb);
        }

        [HttpGet("bandeja-paginada")]
        public async Task<IActionResult> ObtenerPaginada(
            [FromQuery] string modo = "analizador",
            [FromQuery] string? buscar = null,
            [FromQuery] string? propietario = null,
            [FromQuery] int? tecnicoId = null,
            [FromQuery] string? departamento = null,
            [FromQuery] string? tipoFotografia = null,
            [FromQuery] string? estado = null,
            [FromQuery] DateTime? fechaDesde = null,
            [FromQuery] DateTime? fechaHasta = null,
            [FromQuery] DateTime? ultimaFechaUtc = null,
            [FromQuery] int? ultimoId = null,
            [FromQuery] int tamanoPagina = 20,
            [FromQuery] int desfaseHorarioMinutos = 0,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string modoNormalizado = NormalizarModo(modo);
            if (!EsModoValido(modoNormalizado))
            {
                return BadRequest(Error(
                    "La bandeja admite mis, decisiones, analizador, analizador-revisadas, aprobador, aprobador-revisadas o historial."));
            }

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                ObtenerInterfaz(modoNormalizado),
                TipoPermisoApi.Leer,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            tamanoPagina = Math.Clamp(tamanoPagina, 10, 50);
            desfaseHorarioMinutos = Math.Clamp(
                desfaseHorarioMinutos,
                -840,
                840);

            if (ultimaFechaUtc.HasValue != ultimoId.HasValue)
            {
                return BadRequest(Error(
                    "El cursor de paginación está incompleto."));
            }

            DateTime hoyLocal = DateTime.UtcNow
                .AddMinutes(desfaseHorarioMinutos)
                .Date;
            DateTime? desdeLocal = fechaDesde?.Date;
            DateTime? hastaLocal = fechaHasta?.Date;

            if (desdeLocal.HasValue && desdeLocal.Value > hoyLocal)
                return BadRequest(Error("La fecha inicial no puede estar en el futuro."));

            if (hastaLocal.HasValue && hastaLocal.Value > hoyLocal)
                return BadRequest(Error("La fecha final no puede estar en el futuro."));

            if (desdeLocal.HasValue && hastaLocal.HasValue &&
                desdeLocal.Value > hastaLocal.Value)
            {
                return BadRequest(Error(
                    "La fecha inicial debe ser anterior o igual a la fecha final."));
            }

            DateTime? desdeUtc = desdeLocal?.AddMinutes(-desfaseHorarioMinutos);
            DateTime? hastaUtcExclusiva = hastaLocal?
                .AddDays(1)
                .AddMinutes(-desfaseHorarioMinutos);

            string estadoNormalizado = NormalizarCodigo(estado);
            HashSet<string> estadosValidos =
            [
                "BORRADOR",
                "EN_PROCESO",
                "EN_PROCESO_CON_ERRORES",
                "PENDIENTE_REVISION",
                "PENDIENTE_APROBACION",
                "FINALIZADA",
                "FINALIZADA_PARCIALMENTE"
            ];

            if (!string.IsNullOrWhiteSpace(estadoNormalizado) &&
                !estadosValidos.Contains(estadoNormalizado))
            {
                return BadRequest(Error("El estado indicado no es válido."));
            }

            await InicializarAsync(cancellationToken);

            List<InspeccionFitosanitariaBandejaItemDto> items =
                await ConsultarAsync(
                    usuarioId.Value,
                    modoNormalizado,
                    Normalizar(buscar),
                    Normalizar(propietario),
                    tecnicoId is > 0 ? tecnicoId : null,
                    Normalizar(departamento),
                    NormalizarCodigo(tipoFotografia),
                    estadoNormalizado,
                    desdeUtc,
                    hastaUtcExclusiva,
                    ultimaFechaUtc?.ToUniversalTime(),
                    ultimoId,
                    tamanoPagina + 1,
                    cancellationToken);

            bool hayMas = items.Count > tamanoPagina;
            if (hayMas)
                items.RemoveAt(items.Count - 1);

            InspeccionFitosanitariaBandejaItemDto? ultimo = items.LastOrDefault();

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
                message = ObtenerMensajeModo(modoNormalizado),
                data = pagina
            });
        }

        /// <summary>
        /// Devuelve solamente los usuarios que realmente poseen inspecciones en
        /// la vista solicitada. Esto evita filtros vacíos al alternar Pendientes
        /// y Revisadas.
        /// </summary>
        [HttpGet("tecnicos")]
        public async Task<IActionResult> ObtenerTecnicos(
            [FromQuery] string modo = "analizador",
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string modoNormalizado = NormalizarModo(modo);
            if (!EsModoTecnicosValido(modoNormalizado))
                return BadRequest(Error("La vista indicada no admite filtro por técnico."));

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                ObtenerInterfaz(modoNormalizado),
                TipoPermisoApi.Leer,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            await InicializarAsync(cancellationToken);

            List<InspeccionFitosanitariaTecnicoFiltroDto> tecnicos =
                await ConsultarTecnicosAsync(
                    usuarioId.Value,
                    modoNormalizado,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Técnicos responsables obtenidos correctamente.",
                data = new InspeccionFitosanitariaTecnicoFiltroRespuestaDto
                {
                    Tecnicos = tecnicos,
                    Asignaciones = []
                }
            });
        }

        private async Task InicializarAsync(CancellationToken cancellationToken)
        {
            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);
        }

        private async Task<List<InspeccionFitosanitariaBandejaItemDto>>
            ConsultarAsync(
                int usuarioId,
                string modo,
                string buscar,
                string propietario,
                int? tecnicoId,
                string departamento,
                string tipoFotografia,
                string estado,
                DateTime? fechaDesdeUtc,
                DateTime? fechaHastaUtc,
                DateTime? ultimaFechaUtc,
                int? ultimoId,
                int limite,
                CancellationToken cancellationToken)
        {
            const string sql = """
WITH bandeja AS
(
    SELECT
        d.DiagnosticoIAId AS InspeccionId,
        ISNULL(NULLIF(LTRIM(RTRIM(d.NombreInspeccion)), N''),
               N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId)) AS NombreInspeccion,
        CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0)) AS EtapaTecnicaFinalizada,
        CONVERT(BIT, ISNULL(d.CerradaDefinitiva, 0)) AS CerradaDefinitiva,
        ISNULL(d.CodigoTerreno, N'') AS CodigoTerreno,
        ISNULL(propietarioActual.NombreCompleto, N'') AS Propietario,
        ISNULL(m.NombreMunicipio, N'') AS Municipio,
        ISNULL(dep.NombreDepartamento, N'') AS Departamento,
        d.UsuarioSolicitanteId AS UsuarioTecnicoId,
        ISNULL(NULLIF(LTRIM(RTRIM(tecnico.nombreCompletoUsuario)), N''),
               ISNULL(tecnico.nombreUsuario, N'')) AS TecnicoNombreCompleto,
        ISNULL(tecnico.nombreUsuario, N'') AS TecnicoUsuario,
        d.FechaSolicitudUtc AS FechaRegistroSistemaUtc,
        CASE
            WHEN ISNULL(d.CerradaDefinitiva, 0) = 1
                 OR
                 (
                    ISNULL(resumen.TotalFotografias, 0) > 0
                    AND ISNULL(resumen.Pendientes, 0) = 0
                    AND ISNULL(resumen.Procesando, 0) = 0
                    AND ISNULL(resumen.ConError, 0) = 0
                 )
                THEN CASE
                    WHEN ISNULL(resumen.TotalFotografias, 0) > 0
                         AND ISNULL(resumen.FinalizadasExitosas, 0) =
                             ISNULL(resumen.TotalFotografias, 0)
                        THEN N'FINALIZADA'
                    ELSE N'FINALIZADA_PARCIALMENTE'
                END
            WHEN ISNULL(resumen.PendienteAprobacion, 0) > 0
                THEN N'PENDIENTE_APROBACION'
            WHEN ISNULL(resumen.ConError, 0) > 0
                THEN N'EN_PROCESO_CON_ERRORES'
            WHEN ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
                THEN N'PENDIENTE_REVISION'
            WHEN ISNULL(resumen.Procesando, 0) > 0
                THEN N'EN_PROCESO'
            ELSE N'BORRADOR'
        END AS EstadoCalculado,
        CONVERT(INT, ISNULL(resumen.TotalFotografias, 0)) AS TotalFotografias,
        CONVERT(INT, ISNULL(resumen.Pendientes, 0)) AS Pendientes,
        CONVERT(INT, ISNULL(resumen.ConError, 0)) AS ConError,
        CONVERT(INT, ISNULL(resumen.Finalizadas, 0)) AS Finalizadas,
        CONVERT(INT, ISNULL(resumen.PendienteDecisionTecnico, 0)) AS RequierenDecisionTecnico,
        CONVERT(INT, ISNULL(resumen.EnviadasRevision, 0)) AS EnviadasRevision,
        CONVERT(INT, ISNULL(resumen.PendienteAprobacion, 0)) AS PendientesAprobacion,
        CONVERT(INT, ISNULL(resumen.EnviadasAprobador, 0)) AS EnviadasAprobador,
        CONVERT(INT, ISNULL(resumen.Procesando, 0)) AS Procesando,
        CONVERT(INT, ISNULL(resumen.Descartadas, 0)) AS Descartadas,
        ISNULL(portada.UrlImagen, N'') AS UrlMiniatura,
        asignacion.UsuarioAnalizadorId AS UsuarioAnalizadorAsignadoId,
        ISNULL(NULLIF(LTRIM(RTRIM(usuarioAnalizador.nombreCompletoUsuario)), N''),
               ISNULL(usuarioAnalizador.nombreUsuario, N'')) AS AnalizadorAsignado,
        asignacion.UsuarioAprobadorId AS UsuarioAprobadorAsignadoId,
        ISNULL(NULLIF(LTRIM(RTRIM(usuarioAprobador.nombreCompletoUsuario)), N''),
               ISNULL(usuarioAprobador.nombreUsuario, N'')) AS AprobadorAsignado,
        ISNULL(CONVERT(VARCHAR(40), asignacion.RowVersion, 1), N'') AS VersionAsignacion
    FROM dbo.diagnosticoIA d
    LEFT JOIN dbo.terreno t ON t.terrenoId = d.TerrenoId
    LEFT JOIN dbo.municipio m ON m.MunicipioId = t.municipioId
    LEFT JOIN dbo.departamento dep ON dep.DepartamentoId = m.DepartamentoId
    LEFT JOIN dbo.usuario tecnico ON tecnico.UsuarioId = d.UsuarioSolicitanteId
    LEFT JOIN dbo.diagnosticoIAAsignacionFlujo asignacion
        ON asignacion.DiagnosticoIAId = d.DiagnosticoIAId
    LEFT JOIN dbo.usuario usuarioAnalizador
        ON usuarioAnalizador.UsuarioId = asignacion.UsuarioAnalizadorId
    LEFT JOIN dbo.usuario usuarioAprobador
        ON usuarioAprobador.UsuarioId = asignacion.UsuarioAprobadorId
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
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(1) AS TotalFotografias,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'PENDIENTE_DECISION_TECNICO', N'DEVUELTA_AL_TECNICO')
                THEN 1 ELSE 0 END) AS PendienteDecisionTecnico,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'PENDIENTE_APROBACION'
                THEN 1 ELSE 0 END) AS PendienteAprobacion,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'PENDIENTE_APROBACION', N'DEVUELTA_AL_ANALIZADOR',
                 N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                 N'NO_CONCLUYENTE', N'PUBLICADA_ALBUM')
                THEN 1 ELSE 0 END) AS EnviadasAprobador,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'PENDIENTE_IA', N'ANALIZANDO_IA')
                THEN 1 ELSE 0 END) AS Procesando,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'ERROR_IA'
                THEN 1 ELSE 0 END) AS ConError,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
                (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                 N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM', N'ERROR_IA')
                THEN 1 ELSE 0 END) AS Pendientes,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                 N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM')
                THEN 1 ELSE 0 END) AS Finalizadas,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'APROBADA', N'APROBADA_CON_CORRECCION', N'DESCARTADA', N'PUBLICADA_ALBUM')
                THEN 1 ELSE 0 END) AS FinalizadasExitosas,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                 N'DEVUELTO_PARA_CORRECCION', N'DEVUELTA_AL_ANALIZADOR',
                 N'PENDIENTE_APROBACION', N'APROBADA', N'APROBADA_CON_CORRECCION',
                 N'RECHAZADA', N'NO_CONCLUYENTE', N'PUBLICADA_ALBUM')
                THEN 1 ELSE 0 END) AS EnviadasRevision,
            SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'DESCARTADA'
                THEN 1 ELSE 0 END) AS Descartadas
        FROM dbo.diagnosticoIAImagen i
        WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
          AND ISNULL(i.Activo, 1) = 1
    ) resumen
    OUTER APPLY
    (
        SELECT TOP(1) i.UrlImagen
        FROM dbo.diagnosticoIAImagen i
        WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
          AND ISNULL(i.Activo, 1) = 1
        ORDER BY i.Orden, i.DiagnosticoIAImagenId
    ) portada
    WHERE d.Activo = 1
      AND (@tecnicoId IS NULL OR d.UsuarioSolicitanteId = @tecnicoId)
      AND (@fechaDesdeUtc IS NULL OR d.FechaSolicitudUtc >= @fechaDesdeUtc)
      AND (@fechaHastaUtc IS NULL OR d.FechaSolicitudUtc < @fechaHastaUtc)
      AND
      (
          @ultimaFechaUtc IS NULL
          OR d.FechaSolicitudUtc < @ultimaFechaUtc
          OR (d.FechaSolicitudUtc = @ultimaFechaUtc AND d.DiagnosticoIAId < @ultimoId)
      )
      AND
      (
          @buscar = N''
          OR ISNULL(d.NombreInspeccion, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(d.CodigoTerreno, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(t.direccionTerreno, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(m.NombreMunicipio, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(dep.NombreDepartamento, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(tecnico.nombreCompletoUsuario, N'') LIKE N'%' + @buscar + N'%'
          OR ISNULL(tecnico.nombreUsuario, N'') LIKE N'%' + @buscar + N'%'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.propietarioTerreno ptBuscar
              INNER JOIN dbo.propietario pBuscar ON pBuscar.propietarioId = ptBuscar.propietarioId
              WHERE ptBuscar.terrenoId = t.terrenoId
                AND ptBuscar.activo = 1
                AND pBuscar.activo = 1
                AND (pBuscar.nombreCompleto LIKE N'%' + @buscar + N'%'
                     OR pBuscar.identificacion LIKE N'%' + @buscar + N'%')
          )
          OR EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen iBuscar
              WHERE iBuscar.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(iBuscar.Activo, 1) = 1
                AND ISNULL(iBuscar.NombreArchivoOriginal, N'') LIKE N'%' + @buscar + N'%'
          )
      )
      AND
      (
          @propietario = N''
          OR EXISTS
          (
              SELECT 1
              FROM dbo.propietarioTerreno ptFiltro
              INNER JOIN dbo.propietario pFiltro ON pFiltro.propietarioId = ptFiltro.propietarioId
              WHERE ptFiltro.terrenoId = t.terrenoId
                AND ptFiltro.activo = 1
                AND pFiltro.activo = 1
                AND (pFiltro.nombreCompleto LIKE N'%' + @propietario + N'%'
                     OR pFiltro.identificacion LIKE N'%' + @propietario + N'%')
          )
      )
      AND (@departamento = N'' OR ISNULL(dep.NombreDepartamento, N'') LIKE N'%' + @departamento + N'%')
      AND
      (
          @tipoFotografia = N''
          OR EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen iTipo
              WHERE iTipo.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(iTipo.Activo, 1) = 1
                AND UPPER(ISNULL(iTipo.TipoFotografia, N'')) = @tipoFotografia
          )
      )
      AND
      (
          (
              @modo = N'mis'
              AND d.UsuarioSolicitanteId = @usuarioId
          )
          OR
          (
              @modo = N'decisiones'
              AND d.UsuarioSolicitanteId = @usuarioId
              AND ISNULL(d.CerradaDefinitiva, 0) = 0
              AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 0
              AND ISNULL(resumen.PendienteDecisionTecnico, 0) > 0
          )
          OR
          (
              @modo = N'analizador'
              AND ISNULL(d.CerradaDefinitiva, 0) = 0
              AND EXISTS
              (
                  SELECT 1 FROM dbo.diagnosticoIAImagen ia
                  WHERE ia.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(ia.Activo, 1) = 1
                    AND ISNULL(ia.Descartada, 0) = 0
                    AND UPPER(ISNULL(ia.Estado, N'BORRADOR')) IN
                        (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                         N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION',
                         N'DEVUELTA_AL_TECNICO')
              )
          )
          OR
          (
              @modo = N'analizador-revisadas'
              AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.diagnosticoIAImagenAnalisisHumano ah
                  INNER JOIN dbo.diagnosticoIAImagen iah
                      ON iah.DiagnosticoIAImagenId = ah.DiagnosticoIAImagenId
                  WHERE iah.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ah.UsuarioAnalizadorId = @usuarioId
                    AND (ah.FechaEnvioUtc IS NOT NULL OR UPPER(ISNULL(ah.EstadoRegistro, N'')) = N'ENVIADO')
              )
              AND NOT EXISTS
              (
                  SELECT 1 FROM dbo.diagnosticoIAImagen iar
                  WHERE iar.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(iar.Activo, 1) = 1
                    AND ISNULL(iar.Descartada, 0) = 0
                    AND UPPER(ISNULL(iar.Estado, N'BORRADOR')) IN
                        (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                         N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION',
                         N'DEVUELTA_AL_TECNICO')
              )
          )
          OR
          (
              @modo = N'aprobador'
              AND ISNULL(d.CerradaDefinitiva, 0) = 0
              AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
              AND EXISTS
              (
                  SELECT 1 FROM dbo.diagnosticoIAImagen ap
                  WHERE ap.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(ap.Activo, 1) = 1
                    AND ISNULL(ap.Descartada, 0) = 0
                    AND UPPER(ISNULL(ap.Estado, N'BORRADOR')) = N'PENDIENTE_APROBACION'
              )
          )
          OR
          (
              @modo = N'aprobador-revisadas'
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.diagnosticoIAImagenAprobacionV2 apr
                  INNER JOIN dbo.diagnosticoIAImagen imgApr
                      ON imgApr.DiagnosticoIAImagenId = apr.DiagnosticoIAImagenId
                  WHERE imgApr.DiagnosticoIAId = d.DiagnosticoIAId
                    AND apr.UsuarioAprobadorId = @usuarioId
              )
              AND NOT EXISTS
              (
                  SELECT 1 FROM dbo.diagnosticoIAImagen pendienteApr
                  WHERE pendienteApr.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(pendienteApr.Activo, 1) = 1
                    AND ISNULL(pendienteApr.Descartada, 0) = 0
                    AND UPPER(ISNULL(pendienteApr.Estado, N'BORRADOR')) = N'PENDIENTE_APROBACION'
              )
          )
          OR
          (
              @modo = N'historial'
              AND
              (
                  ISNULL(d.CerradaDefinitiva, 0) = 1
                  OR
                  (
                      ISNULL(resumen.TotalFotografias, 0) > 0
                      AND ISNULL(resumen.Pendientes, 0) = 0
                      AND ISNULL(resumen.Procesando, 0) = 0
                      AND ISNULL(resumen.ConError, 0) = 0
                  )
              )
          )
      )
)
SELECT TOP(@limite)
    InspeccionId,
    NombreInspeccion,
    EtapaTecnicaFinalizada,
    CerradaDefinitiva,
    CodigoTerreno,
    Propietario,
    Municipio,
    Departamento,
    UsuarioTecnicoId,
    TecnicoNombreCompleto,
    TecnicoUsuario,
    FechaRegistroSistemaUtc,
    EstadoCalculado,
    TotalFotografias,
    Pendientes,
    ConError,
    Finalizadas,
    RequierenDecisionTecnico,
    EnviadasRevision,
    PendientesAprobacion,
    EnviadasAprobador,
    Procesando,
    Descartadas,
    UrlMiniatura,
    UsuarioAnalizadorAsignadoId,
    AnalizadorAsignado,
    UsuarioAprobadorAsignadoId,
    AprobadorAsignado,
    VersionAsignacion
FROM bandeja
WHERE (@estado = N'' OR EstadoCalculado = @estado)
ORDER BY FechaRegistroSistemaUtc DESC, InspeccionId DESC;
""";

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                comando.CommandType = CommandType.Text;
                comando.CommandTimeout = 180;

                AgregarParametro(comando, "@usuarioId", usuarioId, DbType.Int32);
                AgregarParametro(comando, "@modo", modo, DbType.String);
                AgregarParametro(comando, "@buscar", buscar, DbType.String);
                AgregarParametro(comando, "@propietario", propietario, DbType.String);
                AgregarParametro(comando, "@tecnicoId", tecnicoId, DbType.Int32);
                AgregarParametro(comando, "@departamento", departamento, DbType.String);
                AgregarParametro(comando, "@tipoFotografia", tipoFotografia, DbType.String);
                AgregarParametro(comando, "@estado", estado, DbType.String);
                AgregarParametro(comando, "@fechaDesdeUtc", fechaDesdeUtc, DbType.DateTime2);
                AgregarParametro(comando, "@fechaHastaUtc", fechaHastaUtc, DbType.DateTime2);
                AgregarParametro(comando, "@ultimaFechaUtc", ultimaFechaUtc, DbType.DateTime2);
                AgregarParametro(comando, "@ultimoId", ultimoId, DbType.Int32);
                AgregarParametro(comando, "@limite", limite, DbType.Int32);

                var resultado = new List<InspeccionFitosanitariaBandejaItemDto>(limite);
                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Add(new InspeccionFitosanitariaBandejaItemDto
                    {
                        InspeccionId = reader.GetInt32(0),
                        NombreInspeccion = Texto(reader, 1),
                        EtapaTecnicaFinalizada = reader.GetBoolean(2),
                        CerradaDefinitiva = reader.GetBoolean(3),
                        CodigoTerreno = Texto(reader, 4),
                        Propietario = Texto(reader, 5),
                        Municipio = Texto(reader, 6),
                        Departamento = Texto(reader, 7),
                        UsuarioTecnicoId = reader.GetInt32(8),
                        TecnicoNombreCompleto = Texto(reader, 9),
                        TecnicoUsuario = Texto(reader, 10),
                        FechaRegistroSistemaUtc = DateTime.SpecifyKind(
                            reader.GetDateTime(11),
                            DateTimeKind.Utc),
                        Estado = Texto(reader, 12),
                        TotalFotografias = reader.GetInt32(13),
                        Pendientes = reader.GetInt32(14),
                        ConError = reader.GetInt32(15),
                        Finalizadas = reader.GetInt32(16),
                        RequierenDecisionTecnico = reader.GetInt32(17),
                        EnviadasRevision = reader.GetInt32(18),
                        PendientesAprobacion = reader.GetInt32(19),
                        EnviadasAprobador = reader.GetInt32(20),
                        Procesando = reader.GetInt32(21),
                        Descartadas = reader.GetInt32(22),
                        UrlMiniatura = Texto(reader, 23),
                        UsuarioAnalizadorAsignadoId = reader.IsDBNull(24)
                            ? null : reader.GetInt32(24),
                        AnalizadorAsignado = Texto(reader, 25),
                        UsuarioAprobadorAsignadoId = reader.IsDBNull(26)
                            ? null : reader.GetInt32(26),
                        AprobadorAsignado = Texto(reader, 27),
                        VersionAsignacion = Texto(reader, 28)
                    });
                }

                return resultado;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<List<InspeccionFitosanitariaTecnicoFiltroDto>>
            ConsultarTecnicosAsync(
                int usuarioId,
                string modo,
                CancellationToken cancellationToken)
        {
            const string sql = """
SELECT DISTINCT
    d.UsuarioSolicitanteId,
    ISNULL(NULLIF(LTRIM(RTRIM(u.nombreCompletoUsuario)), N''),
           ISNULL(u.nombreUsuario, N'')) AS NombreCompleto,
    ISNULL(u.nombreUsuario, N'') AS NombreUsuario
FROM dbo.diagnosticoIA d
INNER JOIN dbo.usuario u ON u.UsuarioId = d.UsuarioSolicitanteId
LEFT JOIN dbo.diagnosticoIAAsignacionFlujo asignacion
    ON asignacion.DiagnosticoIAId = d.DiagnosticoIAId
OUTER APPLY
(
    SELECT
        COUNT_BIG(1) AS TotalFotografias,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
            (N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
             N'NO_CONCLUYENTE', N'DESCARTADA', N'PUBLICADA_ALBUM', N'ERROR_IA')
            THEN 1 ELSE 0 END) AS Pendientes,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
            (N'PENDIENTE_IA', N'ANALIZANDO_IA') THEN 1 ELSE 0 END) AS Procesando,
        SUM(CASE WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'ERROR_IA'
            THEN 1 ELSE 0 END) AS ConError
    FROM dbo.diagnosticoIAImagen i
    WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
      AND ISNULL(i.Activo, 1) = 1
) resumen
WHERE d.Activo = 1
  AND
  (
      (
          @modo = N'analizador'
          AND ISNULL(d.CerradaDefinitiva, 0) = 0
          AND EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen i
              WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(i.Activo, 1) = 1
                AND ISNULL(i.Descartada, 0) = 0
                AND UPPER(ISNULL(i.Estado, N'')) IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION', N'DEVUELTA_AL_TECNICO')
          )
      )
      OR
      (
          @modo = N'analizador-revisadas'
          AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
          AND EXISTS
          (
              SELECT 1
              FROM dbo.diagnosticoIAImagenAnalisisHumano ah
              INNER JOIN dbo.diagnosticoIAImagen img
                  ON img.DiagnosticoIAImagenId = ah.DiagnosticoIAImagenId
              WHERE img.DiagnosticoIAId = d.DiagnosticoIAId
                AND ah.UsuarioAnalizadorId = @usuarioId
                AND (ah.FechaEnvioUtc IS NOT NULL OR UPPER(ISNULL(ah.EstadoRegistro, N'')) = N'ENVIADO')
          )
          AND NOT EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen i
              WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(i.Activo, 1) = 1
                AND ISNULL(i.Descartada, 0) = 0
                AND UPPER(ISNULL(i.Estado, N'')) IN
                    (N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
                     N'DEVUELTA_AL_ANALIZADOR', N'DEVUELTO_PARA_CORRECCION', N'DEVUELTA_AL_TECNICO')
          )
      )
      OR
      (
          @modo = N'aprobador'
          AND ISNULL(d.CerradaDefinitiva, 0) = 0
          AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
          AND EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen i
              WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(i.Activo, 1) = 1
                AND UPPER(ISNULL(i.Estado, N'')) = N'PENDIENTE_APROBACION'
          )
      )
      OR
      (
          @modo = N'aprobador-revisadas'
          AND EXISTS
          (
              SELECT 1
              FROM dbo.diagnosticoIAImagenAprobacionV2 apr
              INNER JOIN dbo.diagnosticoIAImagen img
                  ON img.DiagnosticoIAImagenId = apr.DiagnosticoIAImagenId
              WHERE img.DiagnosticoIAId = d.DiagnosticoIAId
                AND apr.UsuarioAprobadorId = @usuarioId
          )
          AND NOT EXISTS
          (
              SELECT 1 FROM dbo.diagnosticoIAImagen i
              WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
                AND ISNULL(i.Activo, 1) = 1
                AND UPPER(ISNULL(i.Estado, N'')) = N'PENDIENTE_APROBACION'
          )
      )
      OR
      (
          @modo = N'historial'
          AND
          (
              ISNULL(d.CerradaDefinitiva, 0) = 1
              OR
              (
                  ISNULL(resumen.TotalFotografias, 0) > 0
                  AND ISNULL(resumen.Pendientes, 0) = 0
                  AND ISNULL(resumen.Procesando, 0) = 0
                  AND ISNULL(resumen.ConError, 0) = 0
              )
          )
      )
  )
ORDER BY NombreCompleto, NombreUsuario;
""";

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                comando.CommandTimeout = 120;
                AgregarParametro(comando, "@usuarioId", usuarioId, DbType.Int32);
                AgregarParametro(comando, "@modo", modo, DbType.String);

                var resultado = new List<InspeccionFitosanitariaTecnicoFiltroDto>();
                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Add(new InspeccionFitosanitariaTecnicoFiltroDto
                    {
                        UsuarioTecnicoId = reader.GetInt32(0),
                        NombreCompleto = Texto(reader, 1),
                        NombreUsuario = Texto(reader, 2)
                    });
                }

                return resultado;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private static bool EsModoValido(string modo) => modo is
            "mis" or
            "decisiones" or
            "analizador" or
            "analizador-revisadas" or
            "aprobador" or
            "aprobador-revisadas" or
            "historial";

        private static bool EsModoTecnicosValido(string modo) => modo is
            "analizador" or
            "analizador-revisadas" or
            "aprobador" or
            "aprobador-revisadas" or
            "historial";

        private static string ObtenerInterfaz(string modo) => modo switch
        {
            "analizador" or "analizador-revisadas" =>
                DiagnosticoIAFlujo.InterfazAnalizador,
            "aprobador" or "aprobador-revisadas" =>
                DiagnosticoIAFlujo.InterfazAprobador,
            _ => DiagnosticoIAFlujo.InterfazSolicitud
        };

        private static string ObtenerMensajeModo(string modo) => modo switch
        {
            "mis" => "Inspecciones del usuario obtenidas correctamente.",
            "decisiones" => "Inspecciones con decisiones técnicas pendientes obtenidas correctamente.",
            "analizador" => "Fotografías disponibles para el analizador obtenidas correctamente.",
            "analizador-revisadas" => "Inspecciones revisadas por el analizador obtenidas correctamente.",
            "aprobador" => "Fotografías pendientes de aprobación obtenidas correctamente.",
            "aprobador-revisadas" => "Inspecciones revisadas por el aprobador obtenidas correctamente.",
            "historial" => "Historial fitosanitario obtenido correctamente.",
            _ => "Inspecciones obtenidas correctamente."
        };

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private static string NormalizarModo(string? modo) =>
            string.IsNullOrWhiteSpace(modo)
                ? "analizador"
                : modo.Trim().ToLowerInvariant();

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

        private static string Texto(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal)
                ? string.Empty
                : reader.GetString(ordinal);

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
