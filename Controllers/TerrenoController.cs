using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using static CONATRADEC_API.DTOs.TerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/terreno")]
    public sealed class TerrenoController : ControllerBase
    {
        private readonly DBContext db;

        public TerrenoController(DBContext db)
        {
            this.db = db;
        }

        // ============================================================
        // LISTAR
        // ============================================================

        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<TerrenoListarDto>>> Listar(
            CancellationToken cancellationToken)
        {
            List<TerrenoListarDto> terrenos =
                await ConsultarTerrenosAsync(
                    texto: null,
                    codigoTerreno: null,
                    nombrePropietario: null,
                    identificacionPropietario: null,
                    direccion: null,
                    paisId: null,
                    departamentoId: null,
                    municipioId: null,
                    fechaDesde: null,
                    fechaHasta: null,
                    extensionMinima: null,
                    extensionMaxima: null,
                    page: null,
                    pageSize: null,
                    ordenarPor: "codigo",
                    descendente: false,
                    cancellationToken);

            return Ok(terrenos);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken)
        {
            TerrenoListarDto? terreno =
                (await ConsultarTerrenosPorIdAsync(
                    id,
                    cancellationToken))
                .FirstOrDefault();

            return terreno is null
                ? NotFound(new
                {
                    mensaje = "No se encontró el terreno solicitado."
                })
                : Ok(terreno);
        }

        /// <summary>
        /// Catálogo mínimo utilizado por el formulario de terreno.
        ///
        /// No permite crear, editar ni desactivar propietarios. Solo devuelve
        /// personas activas para establecer la relación propietarioTerreno.
        /// </summary>
        [Authorize]
        [HttpGet("propietarios-disponibles")]
        public async Task<IActionResult> ListarPropietariosDisponibles(
            [FromQuery] string? buscar,
            CancellationToken cancellationToken = default)
        {
            string texto = NormalizarFiltro(buscar);

            const string sql = """
                SELECT
                    p.propietarioId,
                    p.identificacion,
                    p.nombreCompleto,
                    p.telefono,
                    p.correo,
                    p.direccion,
                    p.activo,
                    p.fechaRegistroUtc,
                    COUNT(DISTINCT CASE
                        WHEN pt.activo = 1
                        THEN pt.terrenoId
                    END) AS totalTerrenos
                FROM dbo.propietario p
                LEFT JOIN dbo.propietarioTerreno pt
                    ON pt.propietarioId = p.propietarioId
                WHERE p.activo = 1
                  AND (
                        @buscar = N''
                        OR p.identificacion LIKE
                            N'%' + @buscar + N'%'
                        OR p.nombreCompleto LIKE
                            N'%' + @buscar + N'%'
                        OR ISNULL(p.correo, N'') LIKE
                            N'%' + @buscar + N'%'
                      )
                GROUP BY
                    p.propietarioId,
                    p.identificacion,
                    p.nombreCompleto,
                    p.telefono,
                    p.correo,
                    p.direccion,
                    p.activo,
                    p.fechaRegistroUtc
                ORDER BY
                    p.nombreCompleto,
                    p.identificacion;
                """;

            var propietarios = await ConsultarAsync(
                sql,
                command => AgregarParametro(
                    command,
                    "@buscar",
                    texto),
                reader => new
                {
                    propietarioId =
                        reader.GetInt32(0),
                    identificacion =
                        Texto(reader, 1),
                    nombreCompleto =
                        Texto(reader, 2),
                    telefono =
                        TextoNullable(reader, 3),
                    correo =
                        TextoNullable(reader, 4),
                    direccion =
                        TextoNullable(reader, 5),
                    activo =
                        reader.GetBoolean(6),
                    fechaRegistroUtc =
                        reader.GetDateTime(7),
                    totalTerrenos =
                        reader.GetInt32(8),
                    usuarioPortalId =
                        (int?)null,
                    usuarioPortal =
                        (string?)null
                },
                cancellationToken);

            return Ok(propietarios);
        }

        // ============================================================
        // CREAR
        // ============================================================

        [HttpPost("crear")]
        public async Task<IActionResult> Crear(
            [FromBody] TerrenoCrearDto dto,
            CancellationToken cancellationToken)
        {
            string? error = ValidarDatosTerreno(dto);

            if (error is not null)
                return BadRequest(new { mensaje = error });

            if (!await MunicipioActivoExisteAsync(
                    dto.municipioId,
                    cancellationToken))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El municipio seleccionado no existe o está inactivo."
                });
            }

            PropietarioBase? propietario =
                await ResolverPropietarioAsync(
                    dto.propietarioId,
                    cancellationToken);

            if (propietario is null)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Debe seleccionar un propietario activo registrado."
                });
            }

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var terreno = new Terreno
                {
                    codigoTerreno = $"TMP-{Guid.NewGuid():N}",
                    direccionTerreno = dto.direccionTerreno.Trim(),
                    extensionManzanaTerreno =
                        decimal.Round(dto.extensionManzanaTerreno, 2),
                    fechaIngresoTerreno =
                        DateOnly.FromDateTime(DateTime.Now),
                    cantidadPlantasTerreno =
                        dto.cantidadPlantasTerreno,
                    activo = true,
                    municipioId = dto.municipioId,
                    cantidadQuintalesOro =
                        decimal.Round(dto.cantidadQuintalesOro, 2),
                    latitud = dto.latitud,
                    longitud = dto.longitud
                };

                db.Terreno.Add(terreno);
                await db.SaveChangesAsync(cancellationToken);

                terreno.codigoTerreno = GenerarCodigoTerreno(
                    terreno.municipioId,
                    terreno.terrenoId);

                await db.SaveChangesAsync(cancellationToken);

                await CrearRelacionPropietarioTerrenoAsync(
                    propietario.PropietarioId,
                    terreno.terrenoId,
                    ObtenerUsuarioIdNullable(),
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                TerrenoListarDto? creado =
                    (await ConsultarTerrenosPorIdAsync(
                        terreno.terrenoId,
                        cancellationToken))
                    .FirstOrDefault();

                return Ok(new
                {
                    mensaje = "Terreno creado correctamente.",
                    data = creado ?? new TerrenoListarDto
                    {
                        terrenoId = terreno.terrenoId,
                        codigoTerreno = terreno.codigoTerreno,
                        propietarioId = propietario.PropietarioId
                    }
                });
            }
            catch (DbUpdateException ex)
                when (EsConflictoUnico(ex))
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                return Conflict(new
                {
                    mensaje =
                        "No fue posible generar un código único para " +
                        "el terreno. Intente guardar nuevamente."
                });
            }
            catch (OperationCanceledException)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje =
                            "Ocurrió un error al crear el terreno."
                    });
            }
        }

        // ============================================================
        // EDITAR
        // ============================================================

        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(
            int id,
            [FromBody] TerrenoEditarDto dto,
            CancellationToken cancellationToken)
        {
            string? error = ValidarDatosTerreno(dto);

            if (error is not null)
                return BadRequest(new { mensaje = error });

            Terreno? terreno = await db.Terreno
                .FirstOrDefaultAsync(
                    item =>
                        item.terrenoId == id &&
                        item.activo,
                    cancellationToken);

            if (terreno is null)
            {
                return NotFound(new
                {
                    mensaje = "Terreno no encontrado o inactivo."
                });
            }

            if (!await MunicipioActivoExisteAsync(
                    dto.municipioId,
                    cancellationToken))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El municipio seleccionado no existe o está inactivo."
                });
            }

            PropietarioBase? propietario =
                await ResolverPropietarioAsync(
                    dto.propietarioId,
                    cancellationToken);

            if (propietario is null)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Debe seleccionar un propietario activo registrado."
                });
            }

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                // El código y la fecha de ingreso son inmutables.
                terreno.direccionTerreno =
                    dto.direccionTerreno.Trim();

                terreno.extensionManzanaTerreno =
                    decimal.Round(
                        dto.extensionManzanaTerreno,
                        2);

                terreno.municipioId = dto.municipioId;

                terreno.cantidadQuintalesOro =
                    decimal.Round(
                        dto.cantidadQuintalesOro,
                        2);

                terreno.cantidadPlantasTerreno =
                    dto.cantidadPlantasTerreno;

                terreno.latitud = dto.latitud;
                terreno.longitud = dto.longitud;

                await db.SaveChangesAsync(cancellationToken);

                await CambiarPropietarioTerrenoAsync(
                    propietario.PropietarioId,
                    terreno.terrenoId,
                    ObtenerUsuarioIdNullable(),
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                TerrenoListarDto? actualizado =
                    (await ConsultarTerrenosPorIdAsync(
                        terreno.terrenoId,
                        cancellationToken))
                    .FirstOrDefault();

                return Ok(new
                {
                    mensaje = "Terreno actualizado correctamente.",
                    data = actualizado
                });
            }
            catch (OperationCanceledException)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje =
                            "Ocurrió un error al actualizar el terreno."
                    });
            }
        }

        // ============================================================
        // ELIMINAR LÓGICAMENTE
        // ============================================================

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            Terreno? terreno = await db.Terreno
                .FirstOrDefaultAsync(
                    item =>
                        item.terrenoId == id &&
                        item.activo,
                    cancellationToken);

            if (terreno is null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Terreno no encontrado o ya desactivado."
                });
            }

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                terreno.activo = false;
                await db.SaveChangesAsync(cancellationToken);

                await EjecutarAsync(
                    """
                    UPDATE dbo.propietarioTerreno
                    SET activo = 0,
                        fechaDesasignacionUtc =
                            SYSUTCDATETIME(),
                        desasignadoPorUsuarioId =
                            @usuarioId
                    WHERE terrenoId = @terrenoId
                      AND activo = 1;
                    """,
                    command =>
                    {
                        AgregarParametro(
                            command,
                            "@usuarioId",
                            ObtenerUsuarioIdNullable());

                        AgregarParametro(
                            command,
                            "@terrenoId",
                            terreno.terrenoId);
                    },
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    mensaje = "Terreno eliminado correctamente.",
                    data = new
                    {
                        terreno.terrenoId,
                        terreno.codigoTerreno,
                        terreno.activo
                    }
                });
            }
            catch (OperationCanceledException)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje =
                            "Ocurrió un error al eliminar el terreno."
                    });
            }
        }

        // ============================================================
        // BÚSQUEDA PAGINADA
        // ============================================================

        [HttpGet("buscar")]
        public async Task<IActionResult> Buscar(
            string? texto,
            string? codigoTerreno,
            string? nombrePropietario,
            string? identificacionPropietario,
            string? direccion,
            int? paisId,
            int? departamentoId,
            int? municipioId,
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima,
            string? ordenarPor = "codigo",
            bool descendente = false,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            string? error = ValidarFiltrosBusqueda(
                fechaDesde,
                fechaHasta,
                extensionMinima,
                extensionMaxima);

            if (error is not null)
                return BadRequest(new { mensaje = error });

            int total = await ContarTerrenosAsync(
                texto,
                codigoTerreno,
                nombrePropietario,
                identificacionPropietario,
                direccion,
                paisId,
                departamentoId,
                municipioId,
                fechaDesde,
                fechaHasta,
                extensionMinima,
                extensionMaxima,
                cancellationToken);

            List<TerrenoListarDto> data =
                await ConsultarTerrenosAsync(
                    texto,
                    codigoTerreno,
                    nombrePropietario,
                    identificacionPropietario,
                    direccion,
                    paisId,
                    departamentoId,
                    municipioId,
                    fechaDesde,
                    fechaHasta,
                    extensionMinima,
                    extensionMaxima,
                    page,
                    pageSize,
                    ordenarPor,
                    descendente,
                    cancellationToken);

            return Ok(new
            {
                total,
                page,
                pageSize,
                totalPages = total == 0
                    ? 0
                    : (int)Math.Ceiling(
                        total / (decimal)pageSize),
                data
            });
        }

        // ============================================================
        // CONSULTAS
        // ============================================================

        private Task<List<TerrenoListarDto>>
            ConsultarTerrenosPorIdAsync(
                int terrenoId,
                CancellationToken cancellationToken) =>
            ConsultarTerrenosInternoAsync(
                terrenoId,
                texto: null,
                codigoTerreno: null,
                nombrePropietario: null,
                identificacionPropietario: null,
                direccion: null,
                paisId: null,
                departamentoId: null,
                municipioId: null,
                fechaDesde: null,
                fechaHasta: null,
                extensionMinima: null,
                extensionMaxima: null,
                page: null,
                pageSize: null,
                ordenarPor: "codigo",
                descendente: false,
                cancellationToken);

        private Task<List<TerrenoListarDto>>
            ConsultarTerrenosAsync(
                string? texto,
                string? codigoTerreno,
                string? nombrePropietario,
                string? identificacionPropietario,
                string? direccion,
                int? paisId,
                int? departamentoId,
                int? municipioId,
                DateOnly? fechaDesde,
                DateOnly? fechaHasta,
                decimal? extensionMinima,
                decimal? extensionMaxima,
                int? page,
                int? pageSize,
                string? ordenarPor,
                bool descendente,
                CancellationToken cancellationToken) =>
            ConsultarTerrenosInternoAsync(
                terrenoId: null,
                texto,
                codigoTerreno,
                nombrePropietario,
                identificacionPropietario,
                direccion,
                paisId,
                departamentoId,
                municipioId,
                fechaDesde,
                fechaHasta,
                extensionMinima,
                extensionMaxima,
                page,
                pageSize,
                ordenarPor,
                descendente,
                cancellationToken);

        private async Task<List<TerrenoListarDto>>
            ConsultarTerrenosInternoAsync(
                int? terrenoId,
                string? texto,
                string? codigoTerreno,
                string? nombrePropietario,
                string? identificacionPropietario,
                string? direccion,
                int? paisId,
                int? departamentoId,
                int? municipioId,
                DateOnly? fechaDesde,
                DateOnly? fechaHasta,
                decimal? extensionMinima,
                decimal? extensionMaxima,
                int? page,
                int? pageSize,
                string? ordenarPor,
                bool descendente,
                CancellationToken cancellationToken)
        {
            string orden = CrearOrdenamiento(
                ordenarPor,
                descendente);

            string paginacion =
                page.HasValue && pageSize.HasValue
                    ? """
                      OFFSET @offset ROWS
                      FETCH NEXT @pageSize ROWS ONLY
                      """
                    : string.Empty;

            string sql = $"""
                SELECT
                    t.terrenoId,
                    t.codigoTerreno,
                    t.direccionTerreno,
                    t.extensionManzanaTerreno,
                    t.fechaIngresoTerreno,
                    t.cantidadPlantasTerreno,
                    t.municipioId,
                    t.cantidadQuintalesOro,
                    t.latitud,
                    t.longitud,
                    t.activo,

                    p.propietarioId,
                    p.identificacion,
                    p.nombreCompleto,
                    p.telefono,
                    p.correo,
                    p.direccion,

                    pa.PaisId,
                    pa.NombrePais,
                    d.DepartamentoId,
                    d.NombreDepartamento,
                    m.MunicipioId,
                    m.NombreMunicipio
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
                  AND (@terrenoId IS NULL
                       OR t.terrenoId = @terrenoId)
                  AND (
                        @texto = N''
                        OR t.codigoTerreno LIKE
                            N'%' + @texto + N'%'
                        OR t.direccionTerreno LIKE
                            N'%' + @texto + N'%'
                        OR ISNULL(p.nombreCompleto, N'') LIKE
                            N'%' + @texto + N'%'
                        OR ISNULL(p.identificacion, N'') LIKE
                            N'%' + @texto + N'%'
                      )
                  AND (
                        @codigo = N''
                        OR t.codigoTerreno LIKE
                            N'%' + @codigo + N'%'
                      )
                  AND (
                        @nombrePropietario = N''
                        OR ISNULL(p.nombreCompleto, N'') LIKE
                            N'%' + @nombrePropietario + N'%'
                      )
                  AND (
                        @identificacion = N''
                        OR ISNULL(p.identificacion, N'') LIKE
                            N'%' + @identificacion + N'%'
                      )
                  AND (
                        @direccion = N''
                        OR t.direccionTerreno LIKE
                            N'%' + @direccion + N'%'
                      )
                  AND (@paisId IS NULL
                       OR pa.PaisId = @paisId)
                  AND (@departamentoId IS NULL
                       OR d.DepartamentoId = @departamentoId)
                  AND (@municipioId IS NULL
                       OR m.MunicipioId = @municipioId)
                  AND (@fechaDesde IS NULL
                       OR t.fechaIngresoTerreno >= @fechaDesde)
                  AND (@fechaHasta IS NULL
                       OR t.fechaIngresoTerreno <= @fechaHasta)
                  AND (@extensionMinima IS NULL
                       OR t.extensionManzanaTerreno >=
                            @extensionMinima)
                  AND (@extensionMaxima IS NULL
                       OR t.extensionManzanaTerreno <=
                            @extensionMaxima)
                ORDER BY {orden}
                {paginacion};
                """;

            return await ConsultarAsync(
                sql,
                command =>
                {
                    ConfigurarParametrosBusqueda(
                        command,
                        terrenoId,
                        texto,
                        codigoTerreno,
                        nombrePropietario,
                        identificacionPropietario,
                        direccion,
                        paisId,
                        departamentoId,
                        municipioId,
                        fechaDesde,
                        fechaHasta,
                        extensionMinima,
                        extensionMaxima);

                    if (page.HasValue && pageSize.HasValue)
                    {
                        AgregarParametro(
                            command,
                            "@offset",
                            (page.Value - 1) *
                            pageSize.Value);

                        AgregarParametro(
                            command,
                            "@pageSize",
                            pageSize.Value);
                    }
                },
                MapearTerreno,
                cancellationToken);
        }

        private async Task<int> ContarTerrenosAsync(
            string? texto,
            string? codigoTerreno,
            string? nombrePropietario,
            string? identificacionPropietario,
            string? direccion,
            int? paisId,
            int? departamentoId,
            int? municipioId,
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT COUNT_BIG(1)
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
                        OR t.codigoTerreno LIKE
                            N'%' + @texto + N'%'
                        OR t.direccionTerreno LIKE
                            N'%' + @texto + N'%'
                        OR ISNULL(p.nombreCompleto, N'') LIKE
                            N'%' + @texto + N'%'
                        OR ISNULL(p.identificacion, N'') LIKE
                            N'%' + @texto + N'%'
                      )
                  AND (
                        @codigo = N''
                        OR t.codigoTerreno LIKE
                            N'%' + @codigo + N'%'
                      )
                  AND (
                        @nombrePropietario = N''
                        OR ISNULL(p.nombreCompleto, N'') LIKE
                            N'%' + @nombrePropietario + N'%'
                      )
                  AND (
                        @identificacion = N''
                        OR ISNULL(p.identificacion, N'') LIKE
                            N'%' + @identificacion + N'%'
                      )
                  AND (
                        @direccion = N''
                        OR t.direccionTerreno LIKE
                            N'%' + @direccion + N'%'
                      )
                  AND (@paisId IS NULL
                       OR pa.PaisId = @paisId)
                  AND (@departamentoId IS NULL
                       OR d.DepartamentoId = @departamentoId)
                  AND (@municipioId IS NULL
                       OR m.MunicipioId = @municipioId)
                  AND (@fechaDesde IS NULL
                       OR t.fechaIngresoTerreno >= @fechaDesde)
                  AND (@fechaHasta IS NULL
                       OR t.fechaIngresoTerreno <= @fechaHasta)
                  AND (@extensionMinima IS NULL
                       OR t.extensionManzanaTerreno >=
                            @extensionMinima)
                  AND (@extensionMaxima IS NULL
                       OR t.extensionManzanaTerreno <=
                            @extensionMaxima);
                """;

            long total = await EscalarLongAsync(
                sql,
                command => ConfigurarParametrosBusqueda(
                    command,
                    terrenoId: null,
                    texto,
                    codigoTerreno,
                    nombrePropietario,
                    identificacionPropietario,
                    direccion,
                    paisId,
                    departamentoId,
                    municipioId,
                    fechaDesde,
                    fechaHasta,
                    extensionMinima,
                    extensionMaxima),
                cancellationToken);

            return total > int.MaxValue
                ? int.MaxValue
                : (int)total;
        }

        private static TerrenoListarDto MapearTerreno(
            DbDataReader reader)
        {
            int? propietarioId =
                EnteroNullable(reader, 11);

            TerrenoPropietarioDto? propietario =
                propietarioId.HasValue
                    ? new TerrenoPropietarioDto
                    {
                        propietarioId =
                            propietarioId.Value,
                        identificacion =
                            Texto(reader, 12),
                        nombreCompleto =
                            Texto(reader, 13),
                        telefono =
                            TextoNullable(reader, 14),
                        correo =
                            TextoNullable(reader, 15),
                        direccion =
                            TextoNullable(reader, 16)
                    }
                    : null;

            return new TerrenoListarDto
            {
                terrenoId = reader.GetInt32(0),
                codigoTerreno = Texto(reader, 1),
                direccionTerreno = Texto(reader, 2),
                extensionManzanaTerreno =
                    reader.GetDecimal(3),
                fechaIngresoTerreno =
                    DateOnly.FromDateTime(
                        reader.GetDateTime(4)),
                cantidadPlantasTerreno =
                    reader.GetInt32(5),
                municipioId = reader.GetInt32(6),
                cantidadQuintalesOro =
                    reader.GetDecimal(7),
                latitud = reader.GetDecimal(8),
                longitud = reader.GetDecimal(9),
                activo = reader.GetBoolean(10),
                propietarioId = propietarioId,
                propietario = propietario,
                ubicacion = new TerrenoUbicacionDto
                {
                    paisId = reader.GetInt32(17),
                    nombrePais = Texto(reader, 18),
                    departamentoId =
                        reader.GetInt32(19),
                    nombreDepartamento =
                        Texto(reader, 20),
                    municipioId =
                        reader.GetInt32(21),
                    nombreMunicipio =
                        Texto(reader, 22)
                }
            };
        }

        // ============================================================
        // PROPIETARIO
        // ============================================================

        private async Task<PropietarioBase?>
            ResolverPropietarioAsync(
                int propietarioId,
                CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (1)
                    propietarioId,
                    identificacion,
                    nombreCompleto,
                    telefono,
                    correo
                FROM dbo.propietario
                WHERE propietarioId = @propietarioId
                  AND activo = 1;
                """;

            List<PropietarioBase> resultados =
                await ConsultarAsync(
                    sql,
                    command => AgregarParametro(
                        command,
                        "@propietarioId",
                        propietarioId),
                    reader => new PropietarioBase
                    {
                        PropietarioId =
                            reader.GetInt32(0),
                        Identificacion =
                            Texto(reader, 1),
                        NombreCompleto =
                            Texto(reader, 2),
                        Telefono =
                            TextoNullable(reader, 3),
                        Correo =
                            TextoNullable(reader, 4)
                    },
                    cancellationToken);

            return resultados.FirstOrDefault();
        }

        private async Task CrearRelacionPropietarioTerrenoAsync(
            int propietarioId,
            int terrenoId,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            await EjecutarAsync(
                """
                INSERT INTO dbo.propietarioTerreno
                (
                    propietarioId,
                    terrenoId,
                    activo,
                    fechaAsignacionUtc,
                    asignadoPorUsuarioId
                )
                VALUES
                (
                    @propietarioId,
                    @terrenoId,
                    1,
                    SYSUTCDATETIME(),
                    @usuarioId
                );
                """,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@propietarioId",
                        propietarioId);

                    AgregarParametro(
                        command,
                        "@terrenoId",
                        terrenoId);

                    AgregarParametro(
                        command,
                        "@usuarioId",
                        usuarioId);
                },
                cancellationToken);
        }

        private async Task CambiarPropietarioTerrenoAsync(
            int propietarioId,
            int terrenoId,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            int? actual = await EscalarEnteroAsync(
                """
                SELECT TOP (1) propietarioId
                FROM dbo.propietarioTerreno
                WHERE terrenoId = @terrenoId
                  AND activo = 1;
                """,
                command => AgregarParametro(
                    command,
                    "@terrenoId",
                    terrenoId),
                cancellationToken);

            if (actual == propietarioId)
                return;

            await EjecutarAsync(
                """
                UPDATE dbo.propietarioTerreno
                SET activo = 0,
                    fechaDesasignacionUtc =
                        SYSUTCDATETIME(),
                    desasignadoPorUsuarioId =
                        @usuarioId
                WHERE terrenoId = @terrenoId
                  AND activo = 1;
                """,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@usuarioId",
                        usuarioId);

                    AgregarParametro(
                        command,
                        "@terrenoId",
                        terrenoId);
                },
                cancellationToken);

            await CrearRelacionPropietarioTerrenoAsync(
                propietarioId,
                terrenoId,
                usuarioId,
                cancellationToken);
        }

        // ============================================================
        // VALIDACIONES
        // ============================================================

        private static string? ValidarDatosTerreno(
            TerrenoGuardarBaseDto dto)
        {
            if (string.IsNullOrWhiteSpace(
                    dto.direccionTerreno))
            {
                return "La dirección del terreno es obligatoria.";
            }

            if (dto.extensionManzanaTerreno <= 0)
            {
                return
                    "La extensión del terreno debe ser mayor que cero.";
            }

            if (dto.cantidadQuintalesOro < 0)
            {
                return
                    "La cantidad de quintales no puede ser negativa.";
            }

            if (dto.cantidadPlantasTerreno < 0)
            {
                return
                    "La cantidad de plantas no puede ser negativa.";
            }

            if (dto.municipioId <= 0)
                return "Debe seleccionar un municipio.";

            if (dto.latitud is < -90 or > 90)
                return "La latitud debe estar entre -90 y 90.";

            if (dto.longitud is < -180 or > 180)
                return "La longitud debe estar entre -180 y 180.";

            if (dto.propietarioId <= 0)
            {
                return "Debe seleccionar un propietario registrado.";
            }

            return null;
        }

        private static string? ValidarFiltrosBusqueda(
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima)
        {
            if (fechaDesde.HasValue &&
                fechaHasta.HasValue &&
                fechaDesde > fechaHasta)
            {
                return
                    "La fecha inicial no puede ser mayor que la final.";
            }

            if (extensionMinima is < 0 ||
                extensionMaxima is < 0)
            {
                return "La extensión no puede ser negativa.";
            }

            if (extensionMinima.HasValue &&
                extensionMaxima.HasValue &&
                extensionMinima > extensionMaxima)
            {
                return
                    "La extensión mínima no puede superar la máxima.";
            }

            return null;
        }

        private Task<bool> MunicipioActivoExisteAsync(
            int municipioId,
            CancellationToken cancellationToken) =>
            db.Municipios
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.MunicipioId == municipioId &&
                        item.Activo,
                    cancellationToken);

        // ============================================================
        // SQL
        // ============================================================

        private async Task<List<T>> ConsultarAsync<T>(
            string sql,
            Action<DbCommand> configurar,
            Func<DbDataReader, T> mapear,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = sql;
                AsignarTransaccionActual(command);
                configurar(command);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                var resultados = new List<T>();

                while (await reader.ReadAsync(
                           cancellationToken))
                {
                    resultados.Add(mapear(reader));
                }

                return resultados;
            }
            finally
            {
                if (cerrar &&
                    db.Database.CurrentTransaction is null)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private async Task<int> EjecutarAsync(
            string sql,
            Action<DbCommand> configurar,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = sql;
                AsignarTransaccionActual(command);
                configurar(command);

                return await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            finally
            {
                if (cerrar &&
                    db.Database.CurrentTransaction is null)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private async Task<int?> EscalarEnteroAsync(
            string sql,
            Action<DbCommand> configurar,
            CancellationToken cancellationToken)
        {
            object? valor = await EscalarAsync(
                sql,
                configurar,
                cancellationToken);

            return valor is null or DBNull
                ? null
                : Convert.ToInt32(
                    valor,
                    CultureInfo.InvariantCulture);
        }

        private async Task<long> EscalarLongAsync(
            string sql,
            Action<DbCommand> configurar,
            CancellationToken cancellationToken)
        {
            object? valor = await EscalarAsync(
                sql,
                configurar,
                cancellationToken);

            return valor is null or DBNull
                ? 0
                : Convert.ToInt64(
                    valor,
                    CultureInfo.InvariantCulture);
        }

        private async Task<object?> EscalarAsync(
            string sql,
            Action<DbCommand> configurar,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = sql;
                AsignarTransaccionActual(command);
                configurar(command);

                return await command.ExecuteScalarAsync(
                    cancellationToken);
            }
            finally
            {
                if (cerrar &&
                    db.Database.CurrentTransaction is null)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private void AsignarTransaccionActual(
            DbCommand command)
        {
            IDbContextTransaction? transaccion =
                db.Database.CurrentTransaction;

            if (transaccion is not null)
            {
                command.Transaction =
                    transaccion.GetDbTransaction();
            }
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor)
        {
            DbParameter parametro =
                command.CreateParameter();

            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            command.Parameters.Add(parametro);
        }

        private static void ConfigurarParametrosBusqueda(
            DbCommand command,
            int? terrenoId,
            string? texto,
            string? codigoTerreno,
            string? nombrePropietario,
            string? identificacionPropietario,
            string? direccion,
            int? paisId,
            int? departamentoId,
            int? municipioId,
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima)
        {
            AgregarParametro(
                command,
                "@terrenoId",
                terrenoId);

            AgregarParametro(
                command,
                "@texto",
                NormalizarFiltro(texto));

            AgregarParametro(
                command,
                "@codigo",
                NormalizarFiltro(codigoTerreno));

            AgregarParametro(
                command,
                "@nombrePropietario",
                NormalizarFiltro(nombrePropietario));

            AgregarParametro(
                command,
                "@identificacion",
                NormalizarFiltro(
                    identificacionPropietario));

            AgregarParametro(
                command,
                "@direccion",
                NormalizarFiltro(direccion));

            AgregarParametro(
                command,
                "@paisId",
                PositivoONull(paisId));

            AgregarParametro(
                command,
                "@departamentoId",
                PositivoONull(departamentoId));

            AgregarParametro(
                command,
                "@municipioId",
                PositivoONull(municipioId));

            AgregarParametro(
                command,
                "@fechaDesde",
                fechaDesde?.ToDateTime(
                    TimeOnly.MinValue));

            AgregarParametro(
                command,
                "@fechaHasta",
                fechaHasta?.ToDateTime(
                    TimeOnly.MinValue));

            AgregarParametro(
                command,
                "@extensionMinima",
                extensionMinima);

            AgregarParametro(
                command,
                "@extensionMaxima",
                extensionMaxima);
        }

        // ============================================================
        // AUXILIARES
        // ============================================================

        private static string CrearOrdenamiento(
            string? ordenarPor,
            bool descendente)
        {
            string direccion =
                descendente ? "DESC" : "ASC";

            return ordenarPor?
                .Trim()
                .ToLowerInvariant() switch
            {
                "propietario" =>
                    $"p.nombreCompleto {direccion}, " +
                    $"t.terrenoId {direccion}",

                "fecha" =>
                    $"t.fechaIngresoTerreno {direccion}, " +
                    $"t.terrenoId {direccion}",

                "extension" =>
                    $"t.extensionManzanaTerreno {direccion}, " +
                    $"t.terrenoId {direccion}",

                _ =>
                    $"t.codigoTerreno {direccion}, " +
                    $"t.terrenoId {direccion}"
            };
        }

        private static string GenerarCodigoTerreno(
            int municipioId,
            int terrenoId) =>
            $"TRR-{municipioId:0000}-{terrenoId:000000}";

        private static bool EsConflictoUnico(
            DbUpdateException exception) =>
            exception.InnerException is SqlException sql &&
            sql.Number is 2601 or 2627;

        private static string NormalizarFiltro(
            string? valor) =>
            (valor ?? string.Empty).Trim();

        private static int? PositivoONull(
            int? valor) =>
            valor is > 0 ? valor : null;

        private int? ObtenerUsuarioIdNullable()
        {
            string? valor =
                User.FindFirst("uid")?.Value ??
                User.FindFirst(
                    ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("sub")?.Value;

            return int.TryParse(
                valor,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int usuarioId) &&
                usuarioId > 0
                    ? usuarioId
                    : null;
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

        private sealed class PropietarioBase
        {
            public int PropietarioId { get; set; }
            public string Identificacion { get; set; } =
                string.Empty;
            public string NombreCompleto { get; set; } =
                string.Empty;
            public string? Telefono { get; set; }
            public string? Correo { get; set; }
        }
    }
}
