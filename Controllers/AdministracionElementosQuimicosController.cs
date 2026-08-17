using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API administrativa moderna de Elementos químicos.
    ///
    /// El ElementoQuimicoController histórico permanece disponible para
    /// selectores y versiones anteriores. Esta API concentra únicamente el
    /// CRUD administrativo, paginación, permisos y resolución de inactivos.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/elementos-quimicos")]
    public sealed class AdministracionElementosQuimicosController :
        ControllerBase
    {
        private const string PermisoInterfaz =
            "elementoQuimicoPage";

        private const string CodigoInactivoExistente =
            "ELEMENTO_QUIMICO_INACTIVO_EXISTENTE";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<AdministracionElementosQuimicosController>
            logger;

        public AdministracionElementosQuimicosController(
            DBContext db,
            PermisoApiService permisos,
            ILogger<AdministracionElementosQuimicosController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO ADMINISTRATIVO PAGINADO
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> Listar(
            string? buscar = null,
            int pagina = 1,
            int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            string texto =
                NormalizarBusqueda(buscar);

            IQueryable<ElementoQuimico> query =
                db.elementoQuimico
                    .AsNoTracking()
                    .Where(x => x.activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.nombreElementoQuimico.Contains(texto) ||
                    x.simboloElementoQuimico.Contains(texto));
            }

            /*
             * El orden se aplica antes de Skip/Take para que las páginas sean
             * estables y reproducibles en cliente.
             */
            query = query
                .OrderBy(x => x.nombreElementoQuimico)
                .ThenBy(x => x.simboloElementoQuimico)
                .ThenBy(x => x.elementoQuimicosId);

            int totalRegistros =
                await query.CountAsync(
                    cancellationToken);

            List<ElementoQuimicoAdminDto> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x =>
                        new ElementoQuimicoAdminDto
                        {
                            ElementoQuimicosId =
                                x.elementoQuimicosId,
                            SimboloElementoQuimico =
                                x.simboloElementoQuimico,
                            NombreElementoQuimico =
                                x.nombreElementoQuimico,
                            PesoEquivalenteElementoQuimico =
                                x.pesoEquivalenteElementoQuimico,
                            Activo =
                                x.activo
                        })
                    .ToListAsync(
                        cancellationToken);

            return Ok(
                PaginaRespuesta<ElementoQuimicoAdminDto>
                    .Crear(
                        items,
                        pagina,
                        tamanoPagina,
                        totalRegistros));
        }

        // ==========================================================
        // DETALLE ADMINISTRATIVO ACTUAL
        // ==========================================================
        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObtenerPorId(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del elemento químico no es válido."));
            }

            ElementoQuimicoAdminDto? data =
                await db.elementoQuimico
                    .AsNoTracking()
                    .Where(x =>
                        x.elementoQuimicosId == id &&
                        x.activo)
                    .Select(x =>
                        new ElementoQuimicoAdminDto
                        {
                            ElementoQuimicosId =
                                x.elementoQuimicosId,
                            SimboloElementoQuimico =
                                x.simboloElementoQuimico,
                            NombreElementoQuimico =
                                x.nombreElementoQuimico,
                            PesoEquivalenteElementoQuimico =
                                x.pesoEquivalenteElementoQuimico,
                            Activo =
                                x.activo
                        })
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (data == null)
            {
                return NotFound(
                    Error(
                        "El elemento químico no existe o está inactivo."));
            }

            return Ok(data);
        }

        // ==========================================================
        // CREAR
        // ==========================================================
        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] ElementoQuimicoGuardarDto? dto,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron los datos del elemento químico."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                ValidarDatos(datos);

            if (validacion != null)
                return validacion;

            if (await ExisteActivoDuplicadoAsync(
                    datos.Simbolo,
                    datos.Nombre,
                    null,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "Ya existe un elemento químico activo con el mismo símbolo o nombre."));
            }

            List<ElementoQuimico> inactivos =
                await BuscarInactivosCoincidentesAsync(
                    datos.Simbolo,
                    datos.Nombre,
                    cancellationToken);

            if (inactivos.Count > 1)
            {
                return Conflict(
                    Error(
                        "El símbolo y el nombre coinciden con registros eliminados diferentes. Reactívelos desde la lista de eliminados para resolver el conflicto."));
            }

            if (inactivos.Count == 1 &&
                !crearNuevoSiExisteInactivo)
            {
                return Conflict(
                    ConflictoInactivo(
                        Proyectar(inactivos[0]),
                        "Ya existe un elemento químico eliminado con el mismo símbolo o nombre."));
            }

            var entidad =
                new ElementoQuimico
                {
                    simboloElementoQuimico =
                        datos.Simbolo,
                    nombreElementoQuimico =
                        datos.Nombre,
                    pesoEquivalenteElementoQuimico =
                        datos.Peso,
                    activo =
                        true
                };

            try
            {
                db.elementoQuimico.Add(entidad);

                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        Proyectar(entidad),
                        "Elemento químico creado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el elemento químico {Simbolo}.",
                    datos.Simbolo);

                return Conflict(
                    Error(
                        "No fue posible crear el elemento químico porque ya existe un registro con el mismo símbolo o nombre."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un elemento químico.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al crear el elemento químico."));
            }
        }

        // ==========================================================
        // EDITAR
        // ==========================================================
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] ElementoQuimicoGuardarDto? dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del elemento químico no es válido."));
            }

            if (dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron los datos del elemento químico."));
            }

            if (dto.ElementoQuimicosId is > 0 &&
                dto.ElementoQuimicosId.Value != id)
            {
                return BadRequest(
                    Error(
                        "El identificador de la ruta no coincide con el elemento enviado."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                ValidarDatos(datos);

            if (validacion != null)
                return validacion;

            ElementoQuimico? entidad =
                await db.elementoQuimico
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "El elemento químico indicado no existe."));
            }

            if (!entidad.activo)
            {
                return Conflict(
                    Error(
                        "No se puede actualizar un elemento químico que está inactivo."));
            }

            if (await ExisteActivoDuplicadoAsync(
                    datos.Simbolo,
                    datos.Nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "Otro elemento químico activo utiliza el mismo símbolo o nombre."));
            }

            entidad.simboloElementoQuimico =
                datos.Simbolo;
            entidad.nombreElementoQuimico =
                datos.Nombre;
            entidad.pesoEquivalenteElementoQuimico =
                datos.Peso;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        Proyectar(entidad),
                        "Elemento químico actualizado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el elemento químico {ElementoId}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible actualizar el elemento químico porque ya existe un registro con el mismo símbolo o nombre."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el elemento químico {ElementoId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al actualizar el elemento químico."));
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // ==========================================================
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Desactivar(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del elemento químico no es válido."));
            }

            ElementoQuimico? entidad =
                await db.elementoQuimico
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "El elemento químico no existe o ya está desactivado."));
            }

            List<string> dependencias =
                await ObtenerDependenciasAsync(
                    id,
                    cancellationToken);

            if (dependencias.Count > 0)
            {
                string detalle =
                    string.Join(
                        ", ",
                        dependencias);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el elemento químico porque está siendo utilizado en: " +
                        detalle + ".",
                    usadoEn = dependencias
                });
            }

            entidad.activo = false;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        "Elemento químico desactivado correctamente."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el elemento químico {ElementoId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al eliminar el elemento químico."));
            }
        }

        // ==========================================================
        // REACTIVAR CON LOS DATOS ACTUALES DEL FORMULARIO
        // ==========================================================
        [HttpPut("{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarConDatos(
            int id,
            [FromBody] ElementoQuimicoGuardarDto? dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del elemento químico no es válido."));
            }

            if (dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron los datos del elemento químico."));
            }

            if (dto.ElementoQuimicosId is > 0 &&
                dto.ElementoQuimicosId.Value != id)
            {
                return BadRequest(
                    Error(
                        "El identificador del formulario no coincide con el registro inactivo seleccionado."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                ValidarDatos(datos);

            if (validacion != null)
                return validacion;

            ElementoQuimico? entidad =
                await db.elementoQuimico
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "El elemento químico eliminado ya no existe."));
            }

            if (entidad.activo)
            {
                return Conflict(
                    Error(
                        "El elemento químico ya se encuentra activo."));
            }

            if (await ExisteActivoDuplicadoAsync(
                    datos.Simbolo,
                    datos.Nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "No se puede reactivar porque otro elemento químico activo utiliza el mismo símbolo o nombre."));
            }

            entidad.simboloElementoQuimico =
                datos.Simbolo;
            entidad.nombreElementoQuimico =
                datos.Nombre;
            entidad.pesoEquivalenteElementoQuimico =
                datos.Peso;
            entidad.activo = true;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        Proyectar(entidad),
                        "Elemento químico reactivado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al reactivar el elemento químico {ElementoId}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible reactivar el elemento químico porque existe otro registro con la misma identidad."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al reactivar el elemento químico {ElementoId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al reactivar el elemento químico."));
            }
        }

        private async Task<bool> ExisteActivoDuplicadoAsync(
            string simbolo,
            string nombre,
            int? excluirId,
            CancellationToken cancellationToken) =>
            await db.elementoQuimico
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.activo &&
                        (!excluirId.HasValue ||
                         x.elementoQuimicosId != excluirId.Value) &&
                        (EF.Functions.Collate(
                            x.simboloElementoQuimico,
                            "Modern_Spanish_CI_AI") == simbolo ||
                         EF.Functions.Collate(
                            x.nombreElementoQuimico,
                            "Modern_Spanish_CI_AI") == nombre),
                    cancellationToken);

        private async Task<List<ElementoQuimico>>
            BuscarInactivosCoincidentesAsync(
                string simbolo,
                string nombre,
                CancellationToken cancellationToken) =>
            await db.elementoQuimico
                .AsNoTracking()
                .Where(x =>
                    !x.activo &&
                    (EF.Functions.Collate(
                        x.simboloElementoQuimico,
                        "Modern_Spanish_CI_AI") == simbolo ||
                     EF.Functions.Collate(
                        x.nombreElementoQuimico,
                        "Modern_Spanish_CI_AI") == nombre))
                .OrderBy(x => x.elementoQuimicosId)
                .Take(2)
                .ToListAsync(cancellationToken);

        /// <summary>
        /// Conserva exactamente las protecciones históricas de eliminación.
        /// Las consultas son independientes porque se ejecutan una sola vez por
        /// acción de usuario y DbContext no admite operaciones paralelas.
        /// </summary>
        private async Task<List<string>> ObtenerDependenciasAsync(
            int id,
            CancellationToken cancellationToken)
        {
            var dependencias =
                new List<string>();

            if (await db
                    .fuenteNutrienteElementoQuimico
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "fuentes de nutrientes");
            }

            if (await db
                    .ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "parámetros de extracción por quintal oro");
            }

            if (await db
                    .ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "rangos nutricionales por cultivo");
            }

            if (await db
                    .ParametroFuenteOrganicaAporte
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "parámetros de fuentes orgánicas");
            }

            /*
             * Los registros históricos se verifican sin filtrar por estado,
             * porque el elemento debe continuar disponible para consultarlos.
             */
            if (await db.AnalisisSueloElementos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "análisis de suelo guardados");
            }

            if (await db.AnalisisSueloCalculoElementos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "cálculos de análisis de suelo");
            }

            if (await db.formulaNutricionalDetalle
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "detalles de fórmulas nutricionales");
            }

            if (await db.formulaNutricionalAporte
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "aportes de fórmulas nutricionales");
            }

            if (await db.fertilizacionMixtaDetalle
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "fertilizaciones mixtas");
            }

            return dependencias;
        }

        private IActionResult? ValidarDatos(
            DatosNormalizados datos)
        {
            if (string.IsNullOrWhiteSpace(datos.Simbolo))
            {
                return BadRequest(
                    Error(
                        "El símbolo del elemento químico es obligatorio."));
            }

            if (datos.Simbolo.Length > 10)
            {
                return BadRequest(
                    Error(
                        "El símbolo no puede superar 10 caracteres."));
            }

            if (string.IsNullOrWhiteSpace(datos.Nombre))
            {
                return BadRequest(
                    Error(
                        "El nombre del elemento químico es obligatorio."));
            }

            if (datos.Nombre.Length > 100)
            {
                return BadRequest(
                    Error(
                        "El nombre no puede superar 100 caracteres."));
            }

            if (datos.Peso <= 0)
            {
                return BadRequest(
                    Error(
                        "El peso equivalente debe ser mayor que cero."));
            }

            if (datos.Peso > 99999999.99m)
            {
                return BadRequest(
                    Error(
                        "El peso equivalente supera el valor permitido."));
            }

            return null;
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    PermisoInterfaz,
                    tipo,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                valor,
                out int id) &&
                id > 0
                    ? id
                    : null;
        }

        private static DatosNormalizados NormalizarDatos(
            ElementoQuimicoGuardarDto dto) =>
            new(
                NormalizarSimbolo(
                    dto.SimboloElementoQuimico),
                NormalizarNombre(
                    dto.NombreElementoQuimico),
                RedondearDosDecimales(
                    dto.PesoEquivalenteElementoQuimico ?? 0));

        private static string NormalizarBusqueda(
            string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length > 100
                ? texto[..100]
                : texto;
        }

        private static string NormalizarSimbolo(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarNombre(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static decimal RedondearDosDecimales(
            decimal valor) =>
            decimal.Round(
                valor,
                2,
                MidpointRounding.AwayFromZero);

        private static ElementoQuimicoAdminDto Proyectar(
            ElementoQuimico entidad) =>
            new()
            {
                ElementoQuimicosId =
                    entidad.elementoQuimicosId,
                SimboloElementoQuimico =
                    entidad.simboloElementoQuimico,
                NombreElementoQuimico =
                    entidad.nombreElementoQuimico,
                PesoEquivalenteElementoQuimico =
                    entidad.pesoEquivalenteElementoQuimico,
                Activo =
                    entidad.activo
            };

        private static object Error(
            string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };

        private static object Exito(
            string mensaje) =>
            new
            {
                success = true,
                message = mensaje
            };

        private static object Exito<T>(
            T data,
            string mensaje) =>
            new
            {
                success = true,
                message = mensaje,
                data
            };

        private static object ConflictoInactivo(
            ElementoQuimicoAdminDto data,
            string mensaje) =>
            new
            {
                success = false,
                code = CodigoInactivoExistente,
                message = mensaje,
                data
            };

        private readonly record struct DatosNormalizados(
            string Simbolo,
            string Nombre,
            decimal Peso);

        public sealed class ElementoQuimicoGuardarDto
        {
            public int? ElementoQuimicosId { get; set; }

            public string SimboloElementoQuimico { get; set; } =
                string.Empty;

            public string NombreElementoQuimico { get; set; } =
                string.Empty;

            public decimal? PesoEquivalenteElementoQuimico { get; set; }
        }

        public sealed class ElementoQuimicoAdminDto
        {
            public int ElementoQuimicosId { get; set; }

            public string SimboloElementoQuimico { get; set; } =
                string.Empty;

            public string NombreElementoQuimico { get; set; } =
                string.Empty;

            public decimal PesoEquivalenteElementoQuimico { get; set; }

            public bool Activo { get; set; }
        }

        public sealed class PaginaRespuesta<T>
        {
            public List<T> Items { get; set; } =
                new();

            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }

            public static PaginaRespuesta<T> Crear(
                List<T> items,
                int pagina,
                int tamanoPagina,
                int total) =>
                new()
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = total,
                    TotalPaginas = total == 0
                        ? 1
                        : (int)Math.Ceiling(
                            total /
                            (double)tamanoPagina)
                };
        }
    }
}
