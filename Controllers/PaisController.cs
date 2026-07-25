using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.PaisDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/pais")]
    public sealed class PaisController : ControllerBase
    {
        private readonly DBContext context;
        private readonly ILogger<PaisController> logger;

        public PaisController(
            DBContext context,
            ILogger<PaisController> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPATIBLE PARA FORMULARIOS Y SELECTORES
        // ==========================================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaisResponse>>> GetAll(
            CancellationToken cancellationToken)
        {
            List<PaisResponse> data = await context.Pais
                .AsNoTracking()
                .Where(pais => pais.Activo)
                .OrderBy(pais => pais.NombrePais)
                .Select(pais => new PaisResponse
                {
                    PaisId = pais.PaisId,
                    NombrePais = pais.NombrePais,
                    CodigoISOPais = pais.CodigoISOPais,
                    Activo = pais.Activo,
                    CantidadDepartamentos = pais.Departamentos.Count(
                        departamento => departamento.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<PaisPaginaResponse>> Buscar(
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            [FromQuery] string orden = "nombre",
            [FromQuery] string direccion = "asc",
            CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<Pais> query = context.Pais
                .AsNoTracking()
                .Where(pais => pais.Activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                if (texto.Length > 80)
                    texto = texto[..80];

                query = query.Where(pais =>
                    pais.NombrePais.Contains(texto) ||
                    pais.CodigoISOPais.Contains(texto));
            }

            bool descendente = string.Equals(
                direccion,
                "desc",
                StringComparison.OrdinalIgnoreCase);

            query = orden.Trim().ToLowerInvariant() switch
            {
                "codigo" when descendente =>
                    query.OrderByDescending(pais => pais.CodigoISOPais)
                         .ThenBy(pais => pais.NombrePais),

                "codigo" =>
                    query.OrderBy(pais => pais.CodigoISOPais)
                         .ThenBy(pais => pais.NombrePais),

                "departamentos" when descendente =>
                    query.OrderByDescending(
                            pais => pais.Departamentos.Count(
                                departamento => departamento.Activo))
                         .ThenBy(pais => pais.NombrePais),

                "departamentos" =>
                    query.OrderBy(
                            pais => pais.Departamentos.Count(
                                departamento => departamento.Activo))
                         .ThenBy(pais => pais.NombrePais),

                _ when descendente =>
                    query.OrderByDescending(pais => pais.NombrePais)
                         .ThenBy(pais => pais.CodigoISOPais),

                _ =>
                    query.OrderBy(pais => pais.NombrePais)
                         .ThenBy(pais => pais.CodigoISOPais)
            };

            int totalRegistros = await query.CountAsync(cancellationToken);

            List<PaisResponse> items = await query
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(pais => new PaisResponse
                {
                    PaisId = pais.PaisId,
                    NombrePais = pais.NombrePais,
                    CodigoISOPais = pais.CodigoISOPais,
                    Activo = pais.Activo,
                    CantidadDepartamentos = pais.Departamentos.Count(
                        departamento => departamento.Activo)
                })
                .ToListAsync(cancellationToken);

            int totalPaginas = totalRegistros == 0
                ? 1
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            return Ok(new PaisPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = totalPaginas
            });
        }

        // ==========================================================
        // CREAR
        // ==========================================================
        [HttpPost("crearPais")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create(
            [FromBody] PaisRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del país."
                });
            }

            string nombre = NormalizarNombre(request.NombrePais);
            string codigoIso = NormalizarCodigoIso(request.CodigoISOPais);

            ActionResult? validacion = ValidarDatos(nombre, codigoIso);
            if (validacion != null)
                return validacion;

            bool codigoDuplicado = await context.Pais.AnyAsync(
                pais =>
                    pais.Activo &&
                    pais.CodigoISOPais == codigoIso,
                cancellationToken);

            if (codigoDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un país activo con el código ISO {codigoIso}."
                });
            }

            bool nombreDuplicado = await context.Pais.AnyAsync(
                pais =>
                    pais.Activo &&
                    EF.Functions.Collate(
                        pais.NombrePais,
                        "Modern_Spanish_CI_AI") == nombre,
                cancellationToken);

            if (nombreDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un país activo con el nombre {nombre}."
                });
            }

            var entity = new Pais
            {
                NombrePais = nombre,
                CodigoISOPais = codigoIso,
                Activo = true
            };

            try
            {
                context.Pais.Add(entity);
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "País creado correctamente.",
                    data = new PaisResponse
                    {
                        PaisId = entity.PaisId,
                        NombrePais = entity.NombrePais,
                        CodigoISOPais = entity.CodigoISOPais,
                        Activo = entity.Activo,
                        CantidadDepartamentos = 0
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el país con código ISO {CodigoIso}.",
                    codigoIso);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el país porque el nombre o el código ISO ya está registrado."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error inesperado al crear un país.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el país."
                });
            }
        }

        // ==========================================================
        // ACTUALIZAR
        // ==========================================================
        [HttpPut("actualizarPais/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(
            int id,
            [FromBody] PaisRequest? request,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador del país no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del país."
                });
            }

            string nombre = NormalizarNombre(request.NombrePais);
            string codigoIso = NormalizarCodigoIso(request.CodigoISOPais);

            ActionResult? validacion = ValidarDatos(nombre, codigoIso);
            if (validacion != null)
                return validacion;

            Pais? entity = await context.Pais.FirstOrDefaultAsync(
                pais => pais.PaisId == id,
                cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El país indicado no existe."
                });
            }

            if (!entity.Activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un país que está inactivo."
                });
            }

            bool codigoDuplicado = await context.Pais.AnyAsync(
                pais =>
                    pais.PaisId != id &&
                    pais.Activo &&
                    pais.CodigoISOPais == codigoIso,
                cancellationToken);

            if (codigoDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe otro país activo con el código ISO {codigoIso}."
                });
            }

            bool nombreDuplicado = await context.Pais.AnyAsync(
                pais =>
                    pais.PaisId != id &&
                    pais.Activo &&
                    EF.Functions.Collate(
                        pais.NombrePais,
                        "Modern_Spanish_CI_AI") == nombre,
                cancellationToken);

            if (nombreDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe otro país activo con el nombre {nombre}."
                });
            }

            entity.NombrePais = nombre;
            entity.CodigoISOPais = codigoIso;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "País actualizado correctamente.",
                    data = new PaisResponse
                    {
                        PaisId = entity.PaisId,
                        NombrePais = entity.NombrePais,
                        CodigoISOPais = entity.CodigoISOPais,
                        Activo = entity.Activo,
                        CantidadDepartamentos = await context.Departamento
                            .AsNoTracking()
                            .CountAsync(
                                departamento =>
                                    departamento.PaisId == entity.PaisId &&
                                    departamento.Activo,
                                cancellationToken)
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el país {PaisId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el país porque el nombre o el código ISO ya está registrado."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el país {PaisId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el país."
                });
            }
        }

        // ==========================================================
        // ELIMINAR LÓGICAMENTE
        // ==========================================================
        [HttpDelete("eliminarPais/{id:int}")]
        public async Task<ActionResult> Delete(
            int id,
            CancellationToken cancellationToken)
        {
            Pais? entity = await context.Pais.FirstOrDefaultAsync(
                pais =>
                    pais.PaisId == id &&
                    pais.Activo,
                cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El país no existe o ya está desactivado."
                });
            }

            bool tieneDepartamentos = await context.Departamento.AnyAsync(
                departamento => departamento.PaisId == id,
                cancellationToken);

            if (tieneDepartamentos)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el país porque tiene departamentos relacionados."
                });
            }

            entity.Activo = false;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "País desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el país {PaisId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el país."
                });
            }
        }

        private ActionResult? ValidarDatos(
            string nombre,
            string codigoIso)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del país es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del país no puede superar 80 caracteres."
                });
            }

            if (codigoIso.Length != 3 ||
                !codigoIso.All(char.IsLetter))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El código ISO debe contener exactamente 3 letras."
                });
            }

            return null;
        }

        private static string NormalizarNombre(string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarCodigoIso(string? valor) =>
            new string(
                (valor ?? string.Empty)
                    .Where(char.IsLetter)
                    .Take(3)
                    .ToArray())
                .ToUpperInvariant();
    }
}
