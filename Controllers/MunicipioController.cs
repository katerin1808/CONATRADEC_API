using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.MunicipioDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/municipio")]
    public sealed class MunicipioController : ControllerBase
    {
        private readonly DBContext context;
        private readonly ILogger<MunicipioController> logger;

        public MunicipioController(
            DBContext context,
            ILogger<MunicipioController> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPLETO PARA SELECTORES Y FORMULARIOS
        // Se conserva también la ruta anterior sin prefijo.
        // ==========================================================
        [HttpGet("listarTodos-por-departamento-por-pais")]
        [HttpGet("~/listarTodos-por-departamento-por-pais")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>>
            ListarTodosConDepartamentoYPais(
                CancellationToken cancellationToken)
        {
            List<MunicipioResponse> municipios =
                await context.Municipios
                    .AsNoTracking()
                    .Where(municipio =>
                        municipio.Activo &&
                        municipio.Departamento != null &&
                        municipio.Departamento.Activo &&
                        municipio.Departamento.Pais != null &&
                        municipio.Departamento.Pais.Activo)
                    .OrderBy(municipio =>
                        municipio.Departamento!.Pais!.NombrePais)
                    .ThenBy(municipio =>
                        municipio.Departamento!.NombreDepartamento)
                    .ThenBy(municipio =>
                        municipio.NombreMunicipio)
                    .Select(municipio =>
                        new MunicipioResponse
                        {
                            MunicipioId = municipio.MunicipioId,
                            NombreMunicipio = municipio.NombreMunicipio,
                            DepartamentoId = municipio.DepartamentoId,
                            NombreDepartamento =
                                municipio.Departamento!.NombreDepartamento,
                            PaisId = municipio.Departamento.PaisId,
                            NombrePais =
                                municipio.Departamento.Pais!.NombrePais,
                            Activo = municipio.Activo
                        })
                    .ToListAsync(cancellationToken);

            return Ok(municipios);
        }

        // ==========================================================
        // LISTADO COMPLETO POR DEPARTAMENTO PARA COMPATIBILIDAD
        // ==========================================================
        [HttpGet("por-departamento/{departamentoId:int}")]
        [HttpGet("~/por-departamento/{departamentoId:int}")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>>
            ListarPorDepartamento(
                int departamentoId,
                CancellationToken cancellationToken)
        {
            DepartamentoUbicacionResponse? departamento =
                await ObtenerDepartamentoActivoAsync(
                    departamentoId,
                    cancellationToken);

            if (departamento == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El departamento seleccionado no existe o está inactivo."
                });
            }

            List<MunicipioResponse> municipios =
                await context.Municipios
                    .AsNoTracking()
                    .Where(municipio =>
                        municipio.DepartamentoId == departamentoId &&
                        municipio.Activo)
                    .OrderBy(municipio => municipio.NombreMunicipio)
                    .Select(municipio =>
                        new MunicipioResponse
                        {
                            MunicipioId = municipio.MunicipioId,
                            NombreMunicipio = municipio.NombreMunicipio,
                            DepartamentoId = municipio.DepartamentoId,
                            NombreDepartamento =
                                departamento.NombreDepartamento,
                            PaisId = departamento.PaisId,
                            NombrePais = departamento.NombrePais,
                            Activo = municipio.Activo
                        })
                    .ToListAsync(cancellationToken);

            return Ok(municipios);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        [HttpGet("~/buscar-municipios")]
        public async Task<ActionResult<MunicipioPaginaResponse>> Buscar(
            [FromQuery] int departamentoId,
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            [FromQuery] string orden = "nombre",
            [FromQuery] string direccion = "asc",
            CancellationToken cancellationToken = default)
        {
            if (departamentoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe indicar un departamento válido para buscar municipios."
                });
            }

            DepartamentoUbicacionResponse? departamento =
                await ObtenerDepartamentoActivoAsync(
                    departamentoId,
                    cancellationToken);

            if (departamento == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El departamento seleccionado no existe o está inactivo."
                });
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<Municipio> query =
                context.Municipios
                    .AsNoTracking()
                    .Where(municipio =>
                        municipio.DepartamentoId == departamentoId &&
                        municipio.Activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                if (texto.Length > 80)
                    texto = texto[..80];

                query = query.Where(municipio =>
                    municipio.NombreMunicipio.Contains(texto));
            }

            bool descendente = string.Equals(
                direccion,
                "desc",
                StringComparison.OrdinalIgnoreCase);

            query = orden.Trim().ToLowerInvariant() switch
            {
                "terrenos" when descendente =>
                    query
                        .OrderByDescending(municipio =>
                            context.Terreno.Count(terreno =>
                                terreno.municipioId == municipio.MunicipioId &&
                                terreno.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                "terrenos" =>
                    query
                        .OrderBy(municipio =>
                            context.Terreno.Count(terreno =>
                                terreno.municipioId == municipio.MunicipioId &&
                                terreno.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                "usuarios" when descendente =>
                    query
                        .OrderByDescending(municipio =>
                            context.Usuarios.Count(usuario =>
                                usuario.municipioId == municipio.MunicipioId &&
                                usuario.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                "usuarios" =>
                    query
                        .OrderBy(municipio =>
                            context.Usuarios.Count(usuario =>
                                usuario.municipioId == municipio.MunicipioId &&
                                usuario.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                "uso" when descendente =>
                    query
                        .OrderByDescending(municipio =>
                            context.Terreno.Count(terreno =>
                                terreno.municipioId == municipio.MunicipioId &&
                                terreno.activo) +
                            context.Usuarios.Count(usuario =>
                                usuario.municipioId == municipio.MunicipioId &&
                                usuario.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                "uso" =>
                    query
                        .OrderBy(municipio =>
                            context.Terreno.Count(terreno =>
                                terreno.municipioId == municipio.MunicipioId &&
                                terreno.activo) +
                            context.Usuarios.Count(usuario =>
                                usuario.municipioId == municipio.MunicipioId &&
                                usuario.activo))
                        .ThenBy(municipio => municipio.NombreMunicipio),

                _ when descendente =>
                    query.OrderByDescending(municipio =>
                        municipio.NombreMunicipio),

                _ =>
                    query.OrderBy(municipio => municipio.NombreMunicipio)
            };

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            List<MunicipioResponse> items =
                await query
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(municipio =>
                        new MunicipioResponse
                        {
                            MunicipioId = municipio.MunicipioId,
                            NombreMunicipio = municipio.NombreMunicipio,
                            DepartamentoId = municipio.DepartamentoId,
                            NombreDepartamento =
                                departamento.NombreDepartamento,
                            PaisId = departamento.PaisId,
                            NombrePais = departamento.NombrePais,
                            Activo = municipio.Activo,
                            CantidadTerrenos =
                                context.Terreno.Count(terreno =>
                                    terreno.municipioId == municipio.MunicipioId &&
                                    terreno.activo),
                            CantidadUsuarios =
                                context.Usuarios.Count(usuario =>
                                    usuario.municipioId == municipio.MunicipioId &&
                                    usuario.activo)
                        })
                    .ToListAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros / (double)tamanoPagina);

            return Ok(new MunicipioPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = totalPaginas,
                DepartamentoId = departamento.DepartamentoId,
                NombreDepartamento = departamento.NombreDepartamento,
                PaisId = departamento.PaisId,
                NombrePais = departamento.NombrePais
            });
        }

        // ==========================================================
        // CREAR
        // Se conserva también POST /crear.
        // ==========================================================
        [HttpPost("crear")]
        [HttpPost("~/crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create(
            [FromBody] MunicipioCreateRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del municipio."
                });
            }

            string nombre = NormalizarNombre(request.NombreMunicipio);
            ActionResult? validacion =
                ValidarDatos(nombre, request.DepartamentoId);

            if (validacion != null)
                return validacion;

            DepartamentoUbicacionResponse? departamento =
                await ObtenerDepartamentoActivoAsync(
                    request.DepartamentoId,
                    cancellationToken);

            if (departamento == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se puede crear el municipio porque el departamento o su país están inactivos."
                });
            }

            bool duplicado =
                await context.Municipios.AnyAsync(
                    municipio =>
                        municipio.DepartamentoId == request.DepartamentoId &&
                        municipio.Activo &&
                        EF.Functions.Collate(
                            municipio.NombreMunicipio,
                            "Modern_Spanish_CI_AI") == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un municipio activo con el nombre {nombre} en {departamento.NombreDepartamento}."
                });
            }

            var entity = new Municipio
            {
                NombreMunicipio = nombre,
                DepartamentoId = departamento.DepartamentoId,
                Activo = true
            };

            try
            {
                context.Municipios.Add(entity);
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Municipio creado correctamente.",
                    data = new MunicipioResponse
                    {
                        MunicipioId = entity.MunicipioId,
                        NombreMunicipio = entity.NombreMunicipio,
                        DepartamentoId = entity.DepartamentoId,
                        NombreDepartamento =
                            departamento.NombreDepartamento,
                        PaisId = departamento.PaisId,
                        NombrePais = departamento.NombrePais,
                        Activo = entity.Activo,
                        CantidadTerrenos = 0,
                        CantidadUsuarios = 0
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el municipio {Nombre} en el departamento {DepartamentoId}.",
                    nombre,
                    request.DepartamentoId);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el municipio porque ya existe un registro con el mismo nombre en este departamento."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un municipio.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el municipio."
                });
            }
        }

        // ==========================================================
        // ACTUALIZAR
        // El municipio permanece en su departamento actual.
        // ==========================================================
        [HttpPut("actualizar/{id:int}")]
        [HttpPut("~/actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(
            int id,
            [FromBody] MunicipioUpdateRequest? request,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador del municipio no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del municipio."
                });
            }

            string nombre = NormalizarNombre(request.NombreMunicipio);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del municipio es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del municipio no puede superar 80 caracteres."
                });
            }

            Municipio? entity =
                await context.Municipios.FirstOrDefaultAsync(
                    municipio => municipio.MunicipioId == id,
                    cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El municipio indicado no existe."
                });
            }

            if (!entity.Activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un municipio que está inactivo."
                });
            }

            DepartamentoUbicacionResponse? departamento =
                await ObtenerDepartamentoActivoAsync(
                    entity.DepartamentoId,
                    cancellationToken);

            if (departamento == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar el municipio porque su departamento o país están inactivos."
                });
            }

            bool duplicado =
                await context.Municipios.AnyAsync(
                    municipio =>
                        municipio.MunicipioId != id &&
                        municipio.DepartamentoId == entity.DepartamentoId &&
                        municipio.Activo &&
                        EF.Functions.Collate(
                            municipio.NombreMunicipio,
                            "Modern_Spanish_CI_AI") == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otro municipio activo con ese nombre en el mismo departamento."
                });
            }

            entity.NombreMunicipio = nombre;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                int cantidadTerrenos =
                    await context.Terreno
                        .AsNoTracking()
                        .CountAsync(
                            terreno =>
                                terreno.municipioId == id && terreno.activo,
                            cancellationToken);

                int cantidadUsuarios =
                    await context.Usuarios
                        .AsNoTracking()
                        .CountAsync(
                            usuario =>
                                usuario.municipioId == id && usuario.activo,
                            cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Municipio actualizado correctamente.",
                    data = new MunicipioResponse
                    {
                        MunicipioId = entity.MunicipioId,
                        NombreMunicipio = entity.NombreMunicipio,
                        DepartamentoId = entity.DepartamentoId,
                        NombreDepartamento =
                            departamento.NombreDepartamento,
                        PaisId = departamento.PaisId,
                        NombrePais = departamento.NombrePais,
                        Activo = entity.Activo,
                        CantidadTerrenos = cantidadTerrenos,
                        CantidadUsuarios = cantidadUsuarios
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el municipio {MunicipioId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el municipio porque ya existe un registro con el mismo nombre en este departamento."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el municipio {MunicipioId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el municipio."
                });
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // ==========================================================
        [HttpDelete("eliminar/{id:int}")]
        [HttpDelete("~/eliminar/{id:int}")]
        public async Task<ActionResult> Delete(
            int id,
            CancellationToken cancellationToken)
        {
            Municipio? entity =
                await context.Municipios.FirstOrDefaultAsync(
                    municipio =>
                        municipio.MunicipioId == id && municipio.Activo,
                    cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El municipio no existe o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            bool usadoEnUsuarios =
                await context.Usuarios.AnyAsync(
                    usuario => usuario.municipioId == id,
                    cancellationToken);

            if (usadoEnUsuarios)
                dependencias.Add("usuarios");

            bool usadoEnTerrenos =
                await context.Terreno.AnyAsync(
                    terreno => terreno.municipioId == id,
                    cancellationToken);

            if (usadoEnTerrenos)
                dependencias.Add("terrenos");

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el municipio porque tiene usuarios o terrenos relacionados.",
                    usadoEn = dependencias
                });
            }

            entity.Activo = false;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Municipio desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el municipio {MunicipioId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el municipio."
                });
            }
        }

        private async Task<DepartamentoUbicacionResponse?>
            ObtenerDepartamentoActivoAsync(
                int departamentoId,
                CancellationToken cancellationToken)
        {
            if (departamentoId <= 0)
                return null;

            return await context.Departamento
                .AsNoTracking()
                .Where(departamento =>
                    departamento.DepartamentoId == departamentoId &&
                    departamento.Activo &&
                    departamento.Pais != null &&
                    departamento.Pais.Activo)
                .Select(departamento =>
                    new DepartamentoUbicacionResponse
                    {
                        DepartamentoId = departamento.DepartamentoId,
                        NombreDepartamento =
                            departamento.NombreDepartamento,
                        PaisId = departamento.PaisId,
                        NombrePais = departamento.Pais!.NombrePais
                    })
                .SingleOrDefaultAsync(cancellationToken);
        }

        private ActionResult? ValidarDatos(
            string nombre,
            int departamentoId)
        {
            if (departamentoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Debe seleccionar un departamento válido."
                });
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del municipio es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del municipio no puede superar 80 caracteres."
                });
            }

            return null;
        }

        private static string NormalizarNombre(string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();
    }
}
