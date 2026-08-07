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
    /// Bandeja operativa del analizador y del aprobador. Se mantiene separada
    /// de la bandeja histórica/general porque el flujo moderno avanza por
    /// fotografía y necesita exponer cuántas evidencias ya llegaron al
    /// aprobador sin esperar el cierre total del expediente.
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
            [FromQuery] int? tecnicoId = null,
            [FromQuery] DateTime? ultimaFechaUtc = null,
            [FromQuery] int? ultimoId = null,
            [FromQuery] int tamanoPagina = 20,
            [FromQuery] int desfaseHorarioMinutos = 0,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string modoNormalizado = (modo ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (modoNormalizado is not ("analizador" or "aprobador"))
            {
                return BadRequest(Error(
                    "La bandeja operativa solo admite modo analizador o aprobador."));
            }

            string interfaz = modoNormalizado == "aprobador"
                ? DiagnosticoIAFlujo.InterfazAprobador
                : DiagnosticoIAFlujo.InterfazAnalizador;

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            tamanoPagina = Math.Clamp(tamanoPagina, 10, 50);
            _ = desfaseHorarioMinutos; // Compatibilidad con el cliente actual.

            if (ultimaFechaUtc.HasValue != ultimoId.HasValue)
            {
                return BadRequest(Error(
                    "El cursor de paginación está incompleto."));
            }

            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);

            List<InspeccionFitosanitariaBandejaItemDto> items =
                await ConsultarAsync(
                    usuarioId.Value,
                    modoNormalizado,
                    tecnicoId is > 0 ? tecnicoId : null,
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
                message = modoNormalizado == "aprobador"
                    ? "Fotografías pendientes de aprobación obtenidas correctamente."
                    : "Fotografías disponibles para el analizador obtenidas correctamente.",
                data = pagina
            });
        }

        private async Task<List<InspeccionFitosanitariaBandejaItemDto>>
            ConsultarAsync(
                int usuarioId,
                string modo,
                int? tecnicoId,
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
               N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId))
            AS NombreInspeccion,
        CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0))
            AS EtapaTecnicaFinalizada,
        CONVERT(BIT, ISNULL(d.CerradaDefinitiva, 0))
            AS CerradaDefinitiva,
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
            WHEN ISNULL(resumen.PendienteAprobacion, 0) > 0
                THEN N'PENDIENTE_APROBACION'
            ELSE N'PENDIENTE_REVISION'
        END AS EstadoCalculado,
        CONVERT(INT, ISNULL(resumen.TotalFotografias, 0)) AS TotalFotografias,
        CONVERT(INT, ISNULL(resumen.Pendientes, 0)) AS Pendientes,
        CONVERT(INT, ISNULL(resumen.ConError, 0)) AS ConError,
        CONVERT(INT, ISNULL(resumen.Finalizadas, 0)) AS Finalizadas,
        CONVERT(INT, ISNULL(resumen.PendienteDecisionTecnico, 0))
            AS RequierenDecisionTecnico,
        CONVERT(INT, ISNULL(resumen.EnviadasRevision, 0))
            AS EnviadasRevision,
        CONVERT(INT, ISNULL(resumen.PendienteAprobacion, 0))
            AS PendientesAprobacion,
        CONVERT(INT, ISNULL(resumen.EnviadasAprobador, 0))
            AS EnviadasAprobador,
        CONVERT(INT, ISNULL(resumen.Procesando, 0)) AS Procesando,
        CONVERT(INT, ISNULL(resumen.Descartadas, 0)) AS Descartadas,
        ISNULL(portada.UrlImagen, N'') AS UrlMiniatura,
        asignacion.UsuarioAnalizadorId AS UsuarioAnalizadorAsignadoId,
        ISNULL(NULLIF(LTRIM(RTRIM(usuarioAnalizador.nombreCompletoUsuario)), N''),
               ISNULL(usuarioAnalizador.nombreUsuario, N'')) AS AnalizadorAsignado,
        asignacion.UsuarioAprobadorId AS UsuarioAprobadorAsignadoId,
        ISNULL(NULLIF(LTRIM(RTRIM(usuarioAprobador.nombreCompletoUsuario)), N''),
               ISNULL(usuarioAprobador.nombreUsuario, N'')) AS AprobadorAsignado,
        ISNULL(CONVERT(VARCHAR(40), asignacion.RowVersion, 1), N'')
            AS VersionAsignacion
    FROM dbo.diagnosticoIA d
    LEFT JOIN dbo.terreno t
        ON t.terrenoId = d.TerrenoId
    LEFT JOIN dbo.municipio m
        ON m.MunicipioId = t.municipioId
    LEFT JOIN dbo.departamento dep
        ON dep.DepartamentoId = m.DepartamentoId
    LEFT JOIN dbo.usuario tecnico
        ON tecnico.UsuarioId = d.UsuarioSolicitanteId
    LEFT JOIN dbo.diagnosticoIAAsignacionFlujo asignacion
        ON asignacion.DiagnosticoIAId = d.DiagnosticoIAId
    LEFT JOIN dbo.usuario usuarioAnalizador
        ON usuarioAnalizador.UsuarioId = asignacion.UsuarioAnalizadorId
    LEFT JOIN dbo.usuario usuarioAprobador
        ON usuarioAprobador.UsuarioId = asignacion.UsuarioAprobadorId
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
        ORDER BY pt.fechaAsignacionUtc DESC,
                 pt.propietarioTerrenoId DESC
    ) propietarioActual
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(1) AS TotalFotografias,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'PENDIENTE_DECISION_TECNICO', N'DEVUELTA_AL_TECNICO')
                    THEN 1 ELSE 0 END) AS PendienteDecisionTecnico,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) =
                    N'PENDIENTE_APROBACION'
                    THEN 1 ELSE 0 END) AS PendienteAprobacion,
            SUM(CASE
                WHEN UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                    (N'PENDIENTE_APROBACION', N'DEVUELTA_AL_ANALIZADOR',
                     N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
                     N'NO_CONCLUYENTE', N'PUBLICADA_ALBUM')
                    THEN 1 ELSE 0 END) AS EnviadasAprobador,
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
        SELECT TOP(1) i.UrlImagen
        FROM dbo.diagnosticoIAImagen i
        WHERE i.DiagnosticoIAId = d.DiagnosticoIAId
          AND ISNULL(i.Activo, 1) = 1
        ORDER BY i.Orden, i.DiagnosticoIAImagenId
    ) portada
    WHERE d.Activo = 1
      AND ISNULL(d.CerradaDefinitiva, 0) = 0
      AND (@tecnicoId IS NULL OR d.UsuarioSolicitanteId = @tecnicoId)
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
          (
              @modo = N'analizador'
              AND
              (
                  asignacion.UsuarioAnalizadorId IS NULL
                  OR asignacion.UsuarioAnalizadorId = @usuarioId
              )
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.diagnosticoIAImagen ia
                  WHERE ia.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(ia.Activo, 1) = 1
                    AND ISNULL(ia.Descartada, 0) = 0
                    AND UPPER(ISNULL(ia.Estado, N'BORRADOR')) IN
                    (
                        N'PENDIENTE_ANALIZADOR',
                        N'EN_ANALISIS_HUMANO',
                        N'DEVUELTA_AL_ANALIZADOR',
                        N'DEVUELTO_PARA_CORRECCION',
                        N'DEVUELTA_AL_TECNICO'
                    )
              )
          )
          OR
          (
              @modo = N'aprobador'
              AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
              AND
              (
                  asignacion.UsuarioAprobadorId IS NULL
                  OR asignacion.UsuarioAprobadorId = @usuarioId
              )
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.diagnosticoIAImagen ap
                  WHERE ap.DiagnosticoIAId = d.DiagnosticoIAId
                    AND ISNULL(ap.Activo, 1) = 1
                    AND ISNULL(ap.Descartada, 0) = 0
                    AND UPPER(ISNULL(ap.Estado, N'BORRADOR')) =
                        N'PENDIENTE_APROBACION'
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
                AgregarParametro(comando, "@tecnicoId", tecnicoId, DbType.Int32);
                AgregarParametro(
                    comando,
                    "@ultimaFechaUtc",
                    ultimaFechaUtc,
                    DbType.DateTime2);
                AgregarParametro(comando, "@ultimoId", ultimoId, DbType.Int32);
                AgregarParametro(comando, "@limite", limite, DbType.Int32);

                var resultado = new List<InspeccionFitosanitariaBandejaItemDto>(
                    limite);

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
                            ? null
                            : reader.GetInt32(24),
                        AnalizadorAsignado = Texto(reader, 25),
                        UsuarioAprobadorAsignadoId = reader.IsDBNull(26)
                            ? null
                            : reader.GetInt32(26),
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
