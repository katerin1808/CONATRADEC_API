using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API administrativa de Tipos de publicación.
    ///
    /// Mantiene separados los registros activos de los eliminados y deja
    /// intactos los endpoints históricos de CategoriaPublicacionController.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/categorias-publicacion")]
    public sealed class AdministracionCategoriasPublicacionController :
        ControllerBase
    {
        private const string InterfazCategorias =
            "categoriaPublicacionPage";

        private readonly NoticiasDbContext db;
        private readonly PermisoApiService permisoApiService;

        public AdministracionCategoriasPublicacionController(
            NoticiasDbContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet]
        public async Task<ActionResult> Listar(
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<CategoriaPublicacion> query =
                db.CategoriasPublicacion
                    .AsNoTracking()
                    .Where(x => x.activo);

            string texto =
                buscar?.Trim() ??
                string.Empty;

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query =
                    query.Where(
                        x =>
                            x.nombreCategoriaPublicacion
                                .Contains(texto) ||
                            x.descripcionCategoriaPublicacion
                                .Contains(texto));
            }

            List<CategoriaPublicacionAdminResponse> datos =
                await query
                    .OrderBy(x => x.orden)
                    .ThenBy(
                        x => x.nombreCategoriaPublicacion)
                    .ThenBy(
                        x => x.categoriaPublicacionId)
                    .Select(
                        x =>
                            new CategoriaPublicacionAdminResponse
                            {
                                CategoriaPublicacionId =
                                    x.categoriaPublicacionId,
                                NombreCategoriaPublicacion =
                                    x.nombreCategoriaPublicacion,
                                DescripcionCategoriaPublicacion =
                                    x.descripcionCategoriaPublicacion,
                                ColorHex =
                                    x.colorHex,
                                Orden =
                                    x.orden,
                                Activo =
                                    x.activo,
                                CantidadPublicaciones =
                                    x.Publicaciones.Count()
                            })
                    .ToListAsync(cancellationToken);

            return Ok(datos);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult> Obtener(
            int id,
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacionAdminResponse? dato =
                await db.CategoriasPublicacion
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.categoriaPublicacionId == id &&
                            x.activo)
                    .Select(
                        x =>
                            new CategoriaPublicacionAdminResponse
                            {
                                CategoriaPublicacionId =
                                    x.categoriaPublicacionId,
                                NombreCategoriaPublicacion =
                                    x.nombreCategoriaPublicacion,
                                DescripcionCategoriaPublicacion =
                                    x.descripcionCategoriaPublicacion,
                                ColorHex =
                                    x.colorHex,
                                Orden =
                                    x.orden,
                                Activo =
                                    x.activo,
                                CantidadPublicaciones =
                                    x.Publicaciones.Count()
                            })
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (dato == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de publicación no existe o ya no se encuentra activo."
                });
            }

            return Ok(dato);
        }

        [HttpPost]
        public async Task<ActionResult> Crear(
            [FromBody]
                CategoriaPublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            string nombre =
                NormalizarTextoUnaLinea(
                    dto.nombreCategoriaPublicacion);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de publicación es obligatorio."
                });
            }

            string nombreNormalizado =
                nombre.ToUpperInvariant();

            bool activoDuplicado =
                await db.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.activo &&
                            x.nombreCategoriaPublicacion
                                .ToUpper() ==
                            nombreNormalizado,
                        cancellationToken);

            if (activoDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un tipo de publicación activo con ese nombre."
                });
            }

            CategoriaPublicacion? inactivo =
                await db.CategoriasPublicacion
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            !x.activo &&
                            x.nombreCategoriaPublicacion
                                .ToUpper() ==
                            nombreNormalizado,
                        cancellationToken);

            if (inactivo != null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un tipo de publicación eliminado con ese nombre.",
                    registroId =
                        inactivo.categoriaPublicacionId,
                    registroNombre =
                        inactivo.nombreCategoriaPublicacion
                });
            }

            var entidad =
                new CategoriaPublicacion
                {
                    nombreCategoriaPublicacion =
                        nombre,
                    descripcionCategoriaPublicacion =
                        NormalizarDescripcion(
                            dto.descripcionCategoriaPublicacion),
                    colorHex =
                        NormalizarColor(
                            dto.colorHex),
                    orden =
                        dto.orden,
                    activo =
                        true
                };

            db.CategoriasPublicacion.Add(entidad);

            await db.SaveChangesAsync(
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    success = true,
                    message =
                        "Tipo de publicación creado correctamente.",
                    data =
                        Mapear(entidad, 0)
                });
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody]
                CategoriaPublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacion? entidad =
                await db.CategoriasPublicacion
                    .FirstOrDefaultAsync(
                        x =>
                            x.categoriaPublicacionId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de publicación no existe o ya no se encuentra activo."
                });
            }

            string nombre =
                NormalizarTextoUnaLinea(
                    dto.nombreCategoriaPublicacion);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de publicación es obligatorio."
                });
            }

            string nombreNormalizado =
                nombre.ToUpperInvariant();

            /*
             * Se evita crear una identidad duplicada incluso si el otro
             * registro está eliminado. De esta forma una reactivación futura
             * no queda bloqueada por un cambio de nombre hecho en edición.
             */
            bool nombreDuplicado =
                await db.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.categoriaPublicacionId != id &&
                            x.nombreCategoriaPublicacion
                                .ToUpper() ==
                            nombreNormalizado,
                        cancellationToken);

            if (nombreDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otro tipo de publicación, activo o eliminado, con ese nombre."
                });
            }

            entidad.nombreCategoriaPublicacion =
                nombre;

            entidad.descripcionCategoriaPublicacion =
                NormalizarDescripcion(
                    dto.descripcionCategoriaPublicacion);

            entidad.colorHex =
                NormalizarColor(
                    dto.colorHex);

            entidad.orden =
                dto.orden;

            await db.SaveChangesAsync(
                cancellationToken);

            int cantidad =
                await db.Publicaciones
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.categoriaPublicacionId ==
                            id,
                        cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Tipo de publicación actualizado correctamente.",
                data =
                    Mapear(entidad, cantidad)
            });
        }

        [HttpPut("{id:int}/reactivar-con-datos")]
        public async Task<ActionResult> ReactivarConDatos(
            int id,
            [FromBody]
                CategoriaPublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacion? entidad =
                await db.CategoriasPublicacion
                    .FirstOrDefaultAsync(
                        x =>
                            x.categoriaPublicacionId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de publicación eliminado no fue encontrado."
                });
            }

            if (entidad.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El tipo de publicación ya se encuentra activo."
                });
            }

            string nombre =
                NormalizarTextoUnaLinea(
                    dto.nombreCategoriaPublicacion);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de publicación es obligatorio."
                });
            }

            string nombreNormalizado =
                nombre.ToUpperInvariant();

            bool activoDuplicado =
                await db.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.categoriaPublicacionId != id &&
                            x.activo &&
                            x.nombreCategoriaPublicacion
                                .ToUpper() ==
                            nombreNormalizado,
                        cancellationToken);

            if (activoDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un tipo de publicación activo con ese nombre."
                });
            }

            entidad.nombreCategoriaPublicacion =
                nombre;

            entidad.descripcionCategoriaPublicacion =
                NormalizarDescripcion(
                    dto.descripcionCategoriaPublicacion);

            entidad.colorHex =
                NormalizarColor(
                    dto.colorHex);

            entidad.orden =
                dto.orden;

            entidad.activo =
                true;

            await db.SaveChangesAsync(
                cancellationToken);

            int cantidad =
                await db.Publicaciones
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.categoriaPublicacionId ==
                            id,
                        cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Tipo de publicación reactivado correctamente.",
                data =
                    Mapear(entidad, cantidad)
            });
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<ActionResult> Eliminar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacion? entidad =
                await db.CategoriasPublicacion
                    .FirstOrDefaultAsync(
                        x =>
                            x.categoriaPublicacionId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de publicación no existe o ya se encuentra inactivo."
                });
            }

            int cantidadPublicaciones =
                await db.Publicaciones
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.categoriaPublicacionId == id,
                        cancellationToken);

            if (cantidadPublicaciones > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede desactivar el tipo de publicación porque tiene publicaciones relacionadas.",
                    cantidadPublicaciones
                });
            }

            entidad.activo = false;

            await db.SaveChangesAsync(
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Tipo de publicación desactivado correctamente."
            });
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    InterfazCategorias,
                    permiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        resultado.Mensaje,
                    mensaje =
                        resultado.Mensaje
                });
        }

        private static CategoriaPublicacionAdminResponse
            Mapear(
                CategoriaPublicacion entidad,
                int cantidadPublicaciones) =>
            new()
            {
                CategoriaPublicacionId =
                    entidad.categoriaPublicacionId,
                NombreCategoriaPublicacion =
                    entidad.nombreCategoriaPublicacion,
                DescripcionCategoriaPublicacion =
                    entidad.descripcionCategoriaPublicacion,
                ColorHex =
                    entidad.colorHex,
                Orden =
                    entidad.orden,
                Activo =
                    entidad.activo,
                CantidadPublicaciones =
                    cantidadPublicaciones
            };

        private static string NormalizarTextoUnaLinea(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

        private static string NormalizarDescripcion(
            string? valor) =>
            (valor ?? string.Empty)
                .Trim();

        private static string NormalizarColor(
            string? valor) =>
            (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        public sealed class CategoriaPublicacionAdminResponse
        {
            public int CategoriaPublicacionId { get; set; }

            public string NombreCategoriaPublicacion
            {
                get;
                set;
            } = string.Empty;

            public string DescripcionCategoriaPublicacion
            {
                get;
                set;
            } = string.Empty;

            public string ColorHex
            {
                get;
                set;
            } = "#3B655B";

            public int Orden { get; set; }

            public bool Activo { get; set; }

            public int CantidadPublicaciones
            {
                get;
                set;
            }
        }
    }
}
