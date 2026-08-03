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
    /// Búsqueda reutilizable de terrenos para el módulo de Diagnóstico IA.
    /// Se mantiene separada del CRUD de terrenos para evitar modificar su
    /// comportamiento actual.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia/terrenos")]
    public sealed class DiagnosticoIATerrenoBusquedaController : ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public DiagnosticoIATerrenoBusquedaController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet("buscar")]
        public async Task<IActionResult> Buscar(
            [FromQuery] string? texto,
            [FromQuery] string? codigo,
            [FromQuery] string? propietario,
            [FromQuery] string? identificacionPropietario,
            [FromQuery] string? ubicacion,
            [FromQuery] string? direccion,
            [FromQuery] decimal? extensionMinima,
            [FromQuery] decimal? extensionMaxima,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                "terrenoPage",
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

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 50);

            if (extensionMinima.HasValue && extensionMinima.Value < 0 ||
                extensionMaxima.HasValue && extensionMaxima.Value < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La extensión no puede ser negativa."
                });
            }

            if (extensionMinima.HasValue &&
                extensionMaxima.HasValue &&
                extensionMinima.Value > extensionMaxima.Value)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La extensión mínima no puede ser mayor que la máxima."
                });
            }

            string filtroTexto = Normalizar(texto);
            string filtroCodigo = Normalizar(codigo);
            string filtroPropietario = Normalizar(propietario);
            string filtroIdentificacion = Normalizar(identificacionPropietario);
            string filtroUbicacion = Normalizar(ubicacion);
            string filtroDireccion = Normalizar(direccion);

            await db.Database.OpenConnectionAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    db.Database.GetDbConnection().CreateCommand();

                command.CommandText = """
                    WITH terrenosFiltrados AS
                    (
                        SELECT
                            t.terrenoId,
                            t.codigoTerreno,
                            t.direccionTerreno,
                            t.extensionManzanaTerreno,
                            t.fechaIngresoTerreno,
                            t.cantidadPlantasTerreno,
                            t.cantidadQuintalesOro,
                            t.latitud,
                            t.longitud,
                            ISNULL(p.propietarioId, 0) AS propietarioId,
                            ISNULL(p.identificacion, N'') AS identificacionPropietario,
                            ISNULL(p.nombreCompleto, N'Sin propietario asignado') AS propietario,
                            pa.NombrePais AS pais,
                            d.NombreDepartamento AS departamento,
                            m.NombreMunicipio AS municipio,
                            COUNT_BIG(1) OVER() AS total
                        FROM dbo.terreno t
                        INNER JOIN dbo.municipio m
                            ON m.MunicipioId = t.municipioId
                        INNER JOIN dbo.departamento d
                            ON d.DepartamentoId = m.DepartamentoId
                        INNER JOIN dbo.pais pa
                            ON pa.PaisId = d.PaisId
                        LEFT JOIN dbo.propietarioTerreno pt
                            ON pt.terrenoId = t.terrenoId
                           AND pt.activo = 1
                        LEFT JOIN dbo.propietario p
                            ON p.propietarioId = pt.propietarioId
                        WHERE t.activo = 1
                          AND (
                                @texto = N''
                                OR t.codigoTerreno LIKE N'%' + @texto + N'%'
                                OR t.direccionTerreno LIKE N'%' + @texto + N'%'
                                OR ISNULL(p.nombreCompleto, N'') LIKE N'%' + @texto + N'%'
                                OR ISNULL(p.identificacion, N'') LIKE N'%' + @texto + N'%'
                                OR pa.NombrePais LIKE N'%' + @texto + N'%'
                                OR d.NombreDepartamento LIKE N'%' + @texto + N'%'
                                OR m.NombreMunicipio LIKE N'%' + @texto + N'%'
                              )
                          AND (@codigo = N'' OR t.codigoTerreno LIKE N'%' + @codigo + N'%')
                          AND (@propietario = N'' OR ISNULL(p.nombreCompleto, N'') LIKE N'%' + @propietario + N'%')
                          AND (@identificacion = N'' OR ISNULL(p.identificacion, N'') LIKE N'%' + @identificacion + N'%')
                          AND (
                                @ubicacion = N''
                                OR pa.NombrePais LIKE N'%' + @ubicacion + N'%'
                                OR d.NombreDepartamento LIKE N'%' + @ubicacion + N'%'
                                OR m.NombreMunicipio LIKE N'%' + @ubicacion + N'%'
                              )
                          AND (@direccion = N'' OR t.direccionTerreno LIKE N'%' + @direccion + N'%')
                          AND (@extensionMinima IS NULL OR t.extensionManzanaTerreno >= @extensionMinima)
                          AND (@extensionMaxima IS NULL OR t.extensionManzanaTerreno <= @extensionMaxima)
                    )
                    SELECT
                        terrenoId,
                        codigoTerreno,
                        direccionTerreno,
                        extensionManzanaTerreno,
                        fechaIngresoTerreno,
                        cantidadPlantasTerreno,
                        cantidadQuintalesOro,
                        latitud,
                        longitud,
                        propietarioId,
                        identificacionPropietario,
                        propietario,
                        pais,
                        departamento,
                        municipio,
                        total
                    FROM terrenosFiltrados
                    ORDER BY codigoTerreno, terrenoId
                    OFFSET @offset ROWS
                    FETCH NEXT @tamanoPagina ROWS ONLY;
                    """;

                AgregarParametro(command, "@texto", filtroTexto);
                AgregarParametro(command, "@codigo", filtroCodigo);
                AgregarParametro(command, "@propietario", filtroPropietario);
                AgregarParametro(command, "@identificacion", filtroIdentificacion);
                AgregarParametro(command, "@ubicacion", filtroUbicacion);
                AgregarParametro(command, "@direccion", filtroDireccion);
                AgregarParametro(command, "@extensionMinima", extensionMinima);
                AgregarParametro(command, "@extensionMaxima", extensionMaxima);
                AgregarParametro(command, "@offset", (pagina - 1) * tamanoPagina);
                AgregarParametro(command, "@tamanoPagina", tamanoPagina);

                var datos = new List<object>();
                long total = 0;

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    total = reader.GetInt64(15);

                    datos.Add(new
                    {
                        terrenoId = reader.GetInt32(0),
                        codigoTerreno = reader.GetString(1),
                        direccionTerreno = reader.GetString(2),
                        extensionManzanaTerreno = reader.GetDecimal(3),
                        fechaIngresoTerreno = reader.GetDateTime(4),
                        cantidadPlantasTerreno = reader.GetInt32(5),
                        cantidadQuintalesOro = reader.GetDecimal(6),
                        latitud = reader.GetDecimal(7),
                        longitud = reader.GetDecimal(8),
                        propietarioId = reader.GetInt32(9) > 0
                            ? reader.GetInt32(9)
                            : (int?)null,
                        identificacionPropietario = reader.GetString(10),
                        propietario = reader.GetString(11),
                        pais = reader.GetString(12),
                        departamento = reader.GetString(13),
                        municipio = reader.GetString(14)
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = string.Empty,
                    data = new
                    {
                        pagina,
                        tamanoPagina,
                        total = total > int.MaxValue
                            ? int.MaxValue
                            : (int)total,
                        totalPaginas = total == 0
                            ? 0
                            : (int)Math.Ceiling(total / (decimal)tamanoPagina),
                        items = datos
                    }
                });
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
        }

        private static string Normalizar(string? valor) =>
            string.IsNullOrWhiteSpace(valor)
                ? string.Empty
                : valor.Trim();

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = nombre;
            parameter.Value = valor ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
