using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Catálogo paginado de propietarios activos utilizado por el formulario
    /// de terreno y por la pantalla de reasignación de propietarios.
    ///
    /// El endpoint sin paginación continúa disponible para mantener
    /// compatibilidad con versiones anteriores de la aplicación.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/terreno/propietarios-disponibles")]
    public sealed class PropietariosDisponiblesPaginadosController
        : ControllerBase
    {
        private readonly DBContext db;

        public PropietariosDisponiblesPaginadosController(
            DBContext db)
        {
            this.db = db;
        }

        [HttpGet("paginado")]
        public async Task<ActionResult<
            ResultadoPaginadoDto<PropietarioPaginadoItemDto>>>
            ListarPaginado(
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 12,
                [FromQuery] string? buscar = null,
                [FromQuery] int? excluirPropietarioId = null,
                CancellationToken cancellationToken = default)
        {
            pagina =
                Math.Max(
                    1,
                    pagina);

            tamanoPagina =
                Math.Clamp(
                    tamanoPagina,
                    6,
                    100);

            string texto =
                (buscar ??
                 string.Empty)
                    .Trim();

            int? excluirId =
                excluirPropietarioId is > 0
                    ? excluirPropietarioId
                    : null;

            int totalRegistros =
                await ConsultarTotalAsync(
                    texto,
                    excluirId,
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
                    ? []
                    : await ConsultarPaginaAsync(
                        texto,
                        excluirId,
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

        [HttpGet("{propietarioId:int}")]
        public async Task<ActionResult<PropietarioPaginadoItemDto>>
            ObtenerDisponible(
                int propietarioId,
                CancellationToken cancellationToken = default)
        {
            if (propietarioId <= 0)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message =
                            "El identificador del propietario no es válido."
                    });
            }

            PropietarioPaginadoItemDto? propietario =
                await db.Propietarios
                    .AsNoTracking()
                    .Where(item =>
                        item.propietarioId ==
                            propietarioId &&
                        item.activo)
                    .Select(item =>
                        new PropietarioPaginadoItemDto
                        {
                            PropietarioId =
                                item.propietarioId,

                            Identificacion =
                                item.identificacion,

                            NombreCompleto =
                                item.nombreCompleto,

                            Telefono =
                                item.telefono,

                            Correo =
                                item.correo,

                            Direccion =
                                item.direccion,

                            Activo =
                                item.activo,

                            FechaRegistroUtc =
                                item.fechaRegistroUtc,

                            TotalTerrenos =
                                db.PropietarioTerrenos
                                    .Count(relacion =>
                                        relacion.propietarioId ==
                                            item.propietarioId &&
                                        relacion.activo),

                            UsuarioPortalId =
                                null,

                            UsuarioPortal =
                                null
                        })
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (propietario == null)
            {
                return NotFound(
                    new
                    {
                        success = false,
                        message =
                            "No se encontró el propietario activo solicitado."
                    });
            }

            return Ok(
                propietario);
        }

        private async Task<int> ConsultarTotalAsync(
            string texto,
            int? excluirPropietarioId,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT COUNT_BIG(1)
                FROM dbo.propietario p
                WHERE p.activo = 1
                  AND (
                        @excluirPropietarioId IS NULL
                        OR p.propietarioId <> @excluirPropietarioId
                      )
                  AND (
                        @buscar = N''
                        OR p.identificacion LIKE N'%' + @buscar + N'%'
                        OR p.nombreCompleto LIKE N'%' + @buscar + N'%'
                        OR ISNULL(p.correo, N'') LIKE N'%' + @buscar + N'%'
                      );
                """;

            return await EjecutarEscalarEnteroAsync(
                sql,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@buscar",
                        texto);

                    AgregarParametro(
                        command,
                        "@excluirPropietarioId",
                        excluirPropietarioId);
                },
                cancellationToken);
        }

        private async Task<List<PropietarioPaginadoItemDto>>
            ConsultarPaginaAsync(
                string texto,
                int? excluirPropietarioId,
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
                    WHERE p.activo = 1
                      AND (
                            @excluirPropietarioId IS NULL
                            OR p.propietarioId <> @excluirPropietarioId
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
                    ) AS totalTerrenos
                FROM propietariosPaginados p
                WHERE p.numeroFila > @offset
                  AND p.numeroFila <= (@offset + @tamanoPagina)
                ORDER BY p.numeroFila;
                """;

            int offset =
                (pagina - 1) *
                tamanoPagina;

            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State !=
                ConnectionState.Open;

            if (cerrar)
            {
                await connection.OpenAsync(
                    cancellationToken);
            }

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    sql;

                AgregarParametro(
                    command,
                    "@buscar",
                    texto);

                AgregarParametro(
                    command,
                    "@excluirPropietarioId",
                    excluirPropietarioId);

                AgregarParametro(
                    command,
                    "@offset",
                    offset);

                AgregarParametro(
                    command,
                    "@tamanoPagina",
                    tamanoPagina);

                var items =
                    new List<PropietarioPaginadoItemDto>(
                        tamanoPagina);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                           cancellationToken))
                {
                    items.Add(
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
                                null,

                            UsuarioPortal =
                                null
                        });
                }

                return items;
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private async Task<int> EjecutarEscalarEnteroAsync(
            string sql,
            Action<DbCommand> configurar,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State !=
                ConnectionState.Open;

            if (cerrar)
            {
                await connection.OpenAsync(
                    cancellationToken);
            }

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    sql;

                configurar(
                    command);

                object? value =
                    await command.ExecuteScalarAsync(
                        cancellationToken);

                return value is null or DBNull
                    ? 0
                    : Convert.ToInt32(
                        value);
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor)
        {
            DbParameter parameter =
                command.CreateParameter();

            parameter.ParameterName =
                nombre;

            parameter.Value =
                valor ??
                DBNull.Value;

            command.Parameters.Add(
                parameter);
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
    }
}
