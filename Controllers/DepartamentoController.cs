using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.DepartamentoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/departamento")]
    public sealed class DepartamentoController : ControllerBase
    {
        private readonly DBContext context;
        private readonly ILogger<DepartamentoController> logger;

        public DepartamentoController(
            DBContext context,
            ILogger<DepartamentoController> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPATIBLE PARA FORMULARIOS Y SELECTORES
        // ==========================================================
        [HttpGet("por-pais/{paisId:int}")]
        public async Task<ActionResult<IEnumerable<DepartamentoResponse>>>
            BuscarPorPais(
                int paisId,
                CancellationToken cancellationToken)
        {
            var pais = await context.Pais
                .AsNoTracking()
                .Where(item =>
                    item.PaisId == paisId &&
                    item.Activo)
                .Select(item => new
                {
                    item.PaisId,
                    item.NombrePais
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (pais == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El país indicado no existe o está inactivo."
                });
            }

            List<DepartamentoResponse> departamentos =
                await context.Departamento
                    .AsNoTracking()
                    .Where(item =>
                        item.PaisId == paisId &&
                        item.Activo)
                    .OrderBy(item =>
                        item.NombreDepartamento)
                    .Select(item =>
                        new DepartamentoResponse
                        {
                            DepartamentoId =
                                item.DepartamentoId,

                            NombreDepartamento =
                                item.NombreDepartamento,

                            PaisId =
                                item.PaisId,

                            NombrePais =
                                pais.NombrePais,

                            Activo =
                                item.Activo,

                            CantidadMunicipios =
                                item.Municipios.Count(
                                    municipio =>
                                        municipio.Activo)
                        })
                    .ToListAsync(cancellationToken);

            return Ok(departamentos);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<DepartamentoPaginaResponse>>
            Buscar(
                [FromQuery] int paisId,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                [FromQuery] string orden = "nombre",
                [FromQuery] string direccion = "asc",
                CancellationToken cancellationToken = default)
        {
            if (paisId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe indicar un país válido para buscar departamentos."
                });
            }

            var pais = await context.Pais
                .AsNoTracking()
                .Where(item =>
                    item.PaisId == paisId &&
                    item.Activo)
                .Select(item => new
                {
                    item.PaisId,
                    item.NombrePais
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (pais == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El país indicado no existe o está inactivo."
                });
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            IQueryable<Departamento> query =
                context.Departamento
                    .AsNoTracking()
                    .Where(item =>
                        item.PaisId == paisId &&
                        item.Activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                if (texto.Length > 80)
                    texto = texto[..80];

                query = query.Where(item =>
                    item.NombreDepartamento.Contains(texto));
            }

            bool descendente = string.Equals(
                direccion,
                "desc",
                StringComparison.OrdinalIgnoreCase);

            query = orden.Trim().ToLowerInvariant() switch
            {
                "municipios" when descendente =>
                    query
                        .OrderByDescending(item =>
                            item.Municipios.Count(
                                municipio =>
                                    municipio.Activo))
                        .ThenBy(item =>
                            item.NombreDepartamento),

                "municipios" =>
                    query
                        .OrderBy(item =>
                            item.Municipios.Count(
                                municipio =>
                                    municipio.Activo))
                        .ThenBy(item =>
                            item.NombreDepartamento),

                _ when descendente =>
                    query
                        .OrderByDescending(item =>
                            item.NombreDepartamento),

                _ =>
                    query
                        .OrderBy(item =>
                            item.NombreDepartamento)
            };

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            List<DepartamentoResponse> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item =>
                        new DepartamentoResponse
                        {
                            DepartamentoId =
                                item.DepartamentoId,

                            NombreDepartamento =
                                item.NombreDepartamento,

                            PaisId =
                                item.PaisId,

                            NombrePais =
                                pais.NombrePais,

                            Activo =
                                item.Activo,

                            CantidadMunicipios =
                                item.Municipios.Count(
                                    municipio =>
                                        municipio.Activo)
                        })
                    .ToListAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new DepartamentoPaginaResponse
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas,
                    PaisId = pais.PaisId,
                    NombrePais = pais.NombrePais
                });
        }

        // ==========================================================
        // CREAR
        // ==========================================================
        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create(
            [FromBody] DepartamentoCreateRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del departamento."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.NombreDepartamento);

            ActionResult? validacion =
                ValidarDatos(
                    nombre,
                    request.PaisId);

            if (validacion != null)
                return validacion;

            var pais = await context.Pais
                .AsNoTracking()
                .Where(item =>
                    item.PaisId == request.PaisId &&
                    item.Activo)
                .Select(item => new
                {
                    item.PaisId,
                    item.NombrePais
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (pais == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se puede crear el departamento porque el país no existe o está inactivo."
                });
            }

            bool duplicado =
                await context.Departamento.AnyAsync(
                    item =>
                        item.PaisId == request.PaisId &&
                        item.Activo &&
                        EF.Functions.Collate(
                            item.NombreDepartamento,
                            "Modern_Spanish_CI_AI") ==
                        nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un departamento activo con el nombre {nombre} en {pais.NombrePais}."
                });
            }

            var entity = new Departamento
            {
                NombreDepartamento = nombre,
                PaisId = pais.PaisId,
                Activo = true
            };

            try
            {
                context.Departamento.Add(entity);
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Departamento creado correctamente.",

                    data =
                        new DepartamentoResponse
                        {
                            DepartamentoId =
                                entity.DepartamentoId,

                            NombreDepartamento =
                                entity.NombreDepartamento,

                            PaisId =
                                entity.PaisId,

                            NombrePais =
                                pais.NombrePais,

                            Activo =
                                entity.Activo,

                            CantidadMunicipios = 0
                        }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el departamento {Nombre} en el país {PaisId}.",
                    nombre,
                    request.PaisId);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el departamento porque ya existe un registro con el mismo nombre en este país."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un departamento.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el departamento."
                });
            }
        }

        // ==========================================================
        // ACTUALIZAR
        // ==========================================================
        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(
            int id,
            [FromBody] DepartamentoUpdateRequest? request,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del departamento no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del departamento."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.NombreDepartamento);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del departamento es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del departamento no puede superar 80 caracteres."
                });
            }

            Departamento? entity =
                await context.Departamento
                    .FirstOrDefaultAsync(
                        item =>
                            item.DepartamentoId == id,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El departamento indicado no existe."
                });
            }

            if (!entity.Activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un departamento que está inactivo."
                });
            }

            bool duplicado =
                await context.Departamento.AnyAsync(
                    item =>
                        item.DepartamentoId != id &&
                        item.PaisId == entity.PaisId &&
                        item.Activo &&
                        EF.Functions.Collate(
                            item.NombreDepartamento,
                            "Modern_Spanish_CI_AI") ==
                        nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otro departamento activo con ese nombre en el mismo país."
                });
            }

            entity.NombreDepartamento = nombre;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                string nombrePais =
                    await context.Pais
                        .AsNoTracking()
                        .Where(item =>
                            item.PaisId == entity.PaisId)
                        .Select(item =>
                            item.NombrePais)
                        .SingleOrDefaultAsync(cancellationToken)
                    ?? string.Empty;

                int cantidadMunicipios =
                    await context.Municipios
                        .AsNoTracking()
                        .CountAsync(
                            item =>
                                item.DepartamentoId == id &&
                                item.Activo,
                            cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Departamento actualizado correctamente.",

                    data =
                        new DepartamentoResponse
                        {
                            DepartamentoId =
                                entity.DepartamentoId,

                            NombreDepartamento =
                                entity.NombreDepartamento,

                            PaisId =
                                entity.PaisId,

                            NombrePais =
                                nombrePais,

                            Activo =
                                entity.Activo,

                            CantidadMunicipios =
                                cantidadMunicipios
                        }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el departamento {DepartamentoId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el departamento porque ya existe un registro con el mismo nombre en este país."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el departamento {DepartamentoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el departamento."
                });
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // ==========================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(
            int id,
            CancellationToken cancellationToken)
        {
            Departamento? entity =
                await context.Departamento
                    .FirstOrDefaultAsync(
                        item =>
                            item.DepartamentoId == id &&
                            item.Activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El departamento no existe o ya está desactivado."
                });
            }

            bool tieneMunicipios =
                await context.Municipios.AnyAsync(
                    item =>
                        item.DepartamentoId == id,
                    cancellationToken);

            if (tieneMunicipios)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el departamento porque tiene municipios relacionados."
                });
            }

            entity.Activo = false;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Departamento desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el departamento {DepartamentoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el departamento."
                });
            }
        }

        // ==========================================================
        // ENDPOINT ANTERIOR CONSERVADO POR COMPATIBILIDAD
        // ==========================================================
        [HttpPost("conteo-paginado")]
        public async Task<ActionResult> ConteoPaginado(
            [FromBody] ConteoPaginadoRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe enviar los datos del conteo."
                });
            }

            int pageSize =
                Math.Clamp(
                    request.PageSize,
                    1,
                    100);

            IQueryable<Departamento> query =
                context.Departamento
                    .AsNoTracking()
                    .Where(item =>
                        item.Activo);

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)pageSize);

            if (!request.ContarIntervalo ||
                request.Inicio <= 0 ||
                request.Fin <= 0)
            {
                return Ok(new
                {
                    totalRegistros,
                    totalPaginas
                });
            }

            int inicio =
                Math.Clamp(
                    request.Inicio,
                    1,
                    totalPaginas);

            int fin =
                Math.Clamp(
                    request.Fin,
                    inicio,
                    totalPaginas);

            int skip =
                (inicio - 1) *
                pageSize;

            int take =
                (fin - inicio + 1) *
                pageSize;

            int cantidad =
                await query
                    .Skip(skip)
                    .Take(take)
                    .CountAsync(cancellationToken);

            return Ok(new
            {
                inicio,
                fin,
                pageSize,
                cantidad
            });
        }

        private ActionResult? ValidarDatos(
            string nombre,
            int paisId)
        {
            if (paisId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe seleccionar un país válido."
                });
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del departamento es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del departamento no puede superar 80 caracteres."
                });
            }

            return null;
        }

        private static string NormalizarNombre(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();
    }
}
