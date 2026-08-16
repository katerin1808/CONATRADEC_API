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
    /// Listado paginado de propietarios para clientes web, Windows y Android.
    ///
    /// El endpoint api/parametrizacion-acceso/propietarios continúa disponible
    /// con su respuesta original para mantener compatibilidad.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/parametrizacion-acceso/propietarios")]
    public sealed class PropietariosPaginadosController : ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public PropietariosPaginadosController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet("paginado")]
        public async Task<ActionResult<
            ResultadoPaginadoDto<PropietarioPaginadoItemDto>>>
            ListarPaginado(
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 12,
                [FromQuery] string? buscar = null,
                [FromQuery] bool incluirInactivos = false,
                [FromQuery] bool soloInactivos = false,
                CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    ParametrizacionAccesoDatabaseInitializer.Propietarios,
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

            pagina = Math.Max(
                1,
                pagina);

            tamanoPagina = Math.Clamp(
                tamanoPagina,
                6,
                100);

            string texto =
                (buscar ?? string.Empty)
                    .Trim();

            /*
             * soloInactivos se agrega sin alterar el comportamiento anterior:
             * - false + incluirInactivos=false => solo activos.
             * - false + incluirInactivos=true  => activos e inactivos.
             * - true                          => exclusivamente inactivos.
             */
            int totalRegistros =
                await ConsultarTotalAsync(
                    texto,
                    incluirInactivos,
                    soloInactivos,
                    cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 0
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            if (totalPaginas > 0 &&
                pagina > totalPaginas)
            {
                pagina = totalPaginas;
            }

            List<PropietarioPaginadoItemDto> items =
                totalRegistros == 0
                    ? new()
                    : await ConsultarPaginaAsync(
                        texto,
                        incluirInactivos,
                        soloInactivos,
                        pagina,
                        tamanoPagina,
                        cancellationToken);

            return Ok(
                new ResultadoPaginadoDto<PropietarioPaginadoItemDto>
                {
                    Items = items,
                    Pagina = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }

        private async Task<int> ConsultarTotalAsync(
            string texto,
            bool incluirInactivos,
            bool soloInactivos,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT COUNT_BIG(1)
                FROM dbo.propietario p
                WHERE
                    (
                        (@soloInactivos = 1 AND p.activo = 0)
                        OR
                        (
                            @soloInactivos = 0
                            AND
                            (
                                @incluirInactivos = 1
                                OR p.activo = 1
                            )
                        )
                    )
                  AND (
                        @buscar = N''
                        OR p.identificacion LIKE N'%' + @buscar + N'%'
                        OR p.nombreCompleto LIKE N'%' + @buscar + N'%'
                        OR ISNULL(p.correo, N'') LIKE N'%' + @buscar + N'%'
                      );
                """;

            DbConnection conexion =
                db.Database.GetDbConnection();

            bool cerrarConexion =
                conexion.State != ConnectionState.Open;

            if (cerrarConexion)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando =
                    conexion.CreateCommand();

                comando.CommandText = sql;

                AgregarParametro(
                    comando,
                    "@buscar",
                    texto);

                AgregarParametro(
                    comando,
                    "@incluirInactivos",
                    incluirInactivos);

                AgregarParametro(
                    comando,
                    "@soloInactivos",
                    soloInactivos);

                object? valor =
                    await comando.ExecuteScalarAsync(
                        cancellationToken);

                return valor == null ||
                       valor == DBNull.Value
                    ? 0
                    : Convert.ToInt32(valor);
            }
            finally
            {
                if (cerrarConexion)
                    await conexion.CloseAsync();
            }
        }

        private async Task<List<PropietarioPaginadoItemDto>>
            ConsultarPaginaAsync(
                string texto,
                bool incluirInactivos,
                bool soloInactivos,
                int pagina,
                int tamanoPagina,
                CancellationToken cancellationToken)
        {
            const string sql = """
                WITH propietariosPaginados AS
                (
                    SELECT
                        p.propietarioId,
                        p.identificacion,
                        p.nombreCompleto,
                        p.telefono,
                        p.correo,
                        p.direccion,
                        p.activo,
                        p.fechaRegistroUtc,
                        ROW_NUMBER() OVER
                        (
                            ORDER BY
                                p.nombreCompleto,
                                p.identificacion,
                                p.propietarioId
                        ) AS numeroFila
                    FROM dbo.propietario p
                    WHERE
                        (
                            (@soloInactivos = 1 AND p.activo = 0)
                            OR
                            (
                                @soloInactivos = 0
                                AND
                                (
                                    @incluirInactivos = 1
                                    OR p.activo = 1
                                )
                            )
                        )
                      AND (
                            @buscar = N''
                            OR p.identificacion LIKE N'%' + @buscar + N'%'
                            OR p.nombreCompleto LIKE N'%' + @buscar + N'%'
                            OR ISNULL(p.correo, N'') LIKE N'%' + @buscar + N'%'
                          )
                )
                SELECT
                    p.propietarioId,
                    p.identificacion,
                    p.nombreCompleto,
                    p.telefono,
                    p.correo,
                    p.direccion,
                    p.activo,
                    p.fechaRegistroUtc,
                    (
                        SELECT COUNT(DISTINCT pt.terrenoId)
                        FROM dbo.propietarioTerreno pt
                        WHERE pt.propietarioId = p.propietarioId
                          AND pt.activo = 1
                    ) AS totalTerrenos,
                    (
                        SELECT MAX(up.usuarioId)
                        FROM dbo.usuarioPropietario up
                        WHERE up.propietarioId = p.propietarioId
                          AND up.activo = 1
                    ) AS usuarioPortalId,
                    (
                        SELECT MAX(u.nombreUsuario)
                        FROM dbo.usuarioPropietario up
                        INNER JOIN dbo.usuario u
                            ON u.UsuarioId = up.usuarioId
                        WHERE up.propietarioId = p.propietarioId
                          AND up.activo = 1
                    ) AS usuarioPortal
                FROM propietariosPaginados p
                WHERE p.numeroFila > @offset
                  AND p.numeroFila <= (@offset + @tamanoPagina)
                ORDER BY p.numeroFila;
                """;

            int offset =
                (pagina - 1) *
                tamanoPagina;

            DbConnection conexion =
                db.Database.GetDbConnection();

            bool cerrarConexion =
                conexion.State != ConnectionState.Open;

            if (cerrarConexion)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando =
                    conexion.CreateCommand();

                comando.CommandText = sql;

                AgregarParametro(
                    comando,
                    "@buscar",
                    texto);

                AgregarParametro(
                    comando,
                    "@incluirInactivos",
                    incluirInactivos);

                AgregarParametro(
                    comando,
                    "@soloInactivos",
                    soloInactivos);

                AgregarParametro(
                    comando,
                    "@offset",
                    offset);

                AgregarParametro(
                    comando,
                    "@tamanoPagina",
                    tamanoPagina);

                var resultado =
                    new List<PropietarioPaginadoItemDto>(
                        tamanoPagina);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                           cancellationToken))
                {
                    resultado.Add(
                        new PropietarioPaginadoItemDto
                        {
                            PropietarioId =
                                reader.GetInt32(0),

                            Identificacion =
                                Texto(
                                    reader,
                                    1),

                            NombreCompleto =
                                Texto(
                                    reader,
                                    2),

                            Telefono =
                                TextoNullable(
                                    reader,
                                    3),

                            Correo =
                                TextoNullable(
                                    reader,
                                    4),

                            Direccion =
                                TextoNullable(
                                    reader,
                                    5),

                            Activo =
                                reader.GetBoolean(6),

                            FechaRegistroUtc =
                                reader.GetDateTime(7),

                            TotalTerrenos =
                                reader.GetInt32(8),

                            UsuarioPortalId =
                                EnteroNullable(
                                    reader,
                                    9),

                            UsuarioPortal =
                                TextoNullable(
                                    reader,
                                    10)
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

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue(
                    "usuarioId") ??
                User.FindFirstValue(
                    "sub");

            return int.TryParse(
                       valor,
                       out int usuarioId) &&
                   usuarioId > 0
                ? usuarioId
                : null;
        }

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor)
        {
            DbParameter parametro =
                comando.CreateParameter();

            parametro.ParameterName =
                nombre;

            parametro.Value =
                valor ??
                DBNull.Value;

            comando.Parameters.Add(
                parametro);
        }

        private static string Texto(
            DbDataReader reader,
            int ordinal) =>
            reader.IsDBNull(ordinal)
                ? string.Empty
                : reader.GetString(ordinal);

        private static string? TextoNullable(
            DbDataReader reader,
            int ordinal) =>
            reader.IsDBNull(ordinal)
                ? null
                : reader.GetString(ordinal);

        private static int? EnteroNullable(
            DbDataReader reader,
            int ordinal) =>
            reader.IsDBNull(ordinal)
                ? null
                : reader.GetInt32(ordinal);
    }
}
