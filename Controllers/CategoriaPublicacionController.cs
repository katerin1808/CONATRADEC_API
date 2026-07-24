using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/configuracion/categorias-publicacion")]
    public sealed class CategoriaPublicacionController : ControllerBase
    {
        private const string InterfazCategorias = "categoriaPublicacionPage";

        private readonly NoticiasDbContext db;
        private readonly PermisoApiService permisoApiService;

        public CategoriaPublicacionController(
            NoticiasDbContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet]
        public async Task<ActionResult> Listar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] bool incluirInactivas = true,
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<CategoriaPublicacion> query = db
                .CategoriasPublicacion
                .AsNoTracking();

            if (!incluirInactivas)
                query = query.Where(x => x.activo);

            string texto = buscar?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.nombreCategoriaPublicacion.Contains(texto) ||
                    x.descripcionCategoriaPublicacion.Contains(texto));
            }

            var datos = await query
                .OrderBy(x => x.orden)
                .ThenBy(x => x.nombreCategoriaPublicacion)
                .Select(x => new
                {
                    x.categoriaPublicacionId,
                    x.nombreCategoriaPublicacion,
                    x.descripcionCategoriaPublicacion,
                    x.colorHex,
                    x.orden,
                    x.activo,
                    cantidadPublicaciones = x.Publicaciones.Count()
                })
                .ToListAsync(cancellationToken);

            return Ok(datos);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult> Obtener(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            var dato = await db.CategoriasPublicacion
                .AsNoTracking()
                .Where(x => x.categoriaPublicacionId == id)
                .Select(x => new
                {
                    x.categoriaPublicacionId,
                    x.nombreCategoriaPublicacion,
                    x.descripcionCategoriaPublicacion,
                    x.colorHex,
                    x.orden,
                    x.activo,
                    cantidadPublicaciones = x.Publicaciones.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (dato == null)
            {
                return NotFound(new
                {
                    mensaje = "El tipo de publicación no fue encontrado."
                });
            }

            return Ok(dato);
        }

        [HttpPost]
        public async Task<ActionResult> Crear(
            [FromBody] CategoriaPublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string nombre = dto.nombreCategoriaPublicacion.Trim();
            string nombreNormalizado = nombre.ToUpperInvariant();
            string descripcion = dto.descripcionCategoriaPublicacion?.Trim()
                ?? string.Empty;
            string color = NormalizarColor(dto.colorHex);

            CategoriaPublicacion? existente = await db
                .CategoriasPublicacion
                .FirstOrDefaultAsync(
                    x => x.nombreCategoriaPublicacion.ToUpper() ==
                         nombreNormalizado,
                    cancellationToken);

            if (existente != null && existente.activo)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe un tipo de publicación activo con ese nombre."
                });
            }

            if (existente != null)
            {
                existente.descripcionCategoriaPublicacion = descripcion;
                existente.colorHex = color;
                existente.orden = dto.orden;
                existente.activo = true;

                await db.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    mensaje = "El tipo de publicación fue reactivado correctamente.",
                    data = Mapear(existente, 0)
                });
            }

            var entidad = new CategoriaPublicacion
            {
                nombreCategoriaPublicacion = nombre,
                descripcionCategoriaPublicacion = descripcion,
                colorHex = color,
                orden = dto.orden,
                activo = true
            };

            db.CategoriasPublicacion.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    mensaje = "Tipo de publicación creado correctamente.",
                    data = Mapear(entidad, 0)
                });
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] CategoriaPublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacion? entidad = await db
                .CategoriasPublicacion
                .FirstOrDefaultAsync(
                    x => x.categoriaPublicacionId == id,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "El tipo de publicación no fue encontrado."
                });
            }

            string nombre = dto.nombreCategoriaPublicacion.Trim();
            string nombreNormalizado = nombre.ToUpperInvariant();

            bool nombreDuplicado = await db.CategoriasPublicacion
                .AsNoTracking()
                .AnyAsync(
                    x => x.categoriaPublicacionId != id &&
                         x.nombreCategoriaPublicacion.ToUpper() ==
                         nombreNormalizado,
                    cancellationToken);

            if (nombreDuplicado)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe otro tipo de publicación con ese nombre."
                });
            }

            entidad.nombreCategoriaPublicacion = nombre;
            entidad.descripcionCategoriaPublicacion =
                dto.descripcionCategoriaPublicacion?.Trim() ?? string.Empty;
            entidad.colorHex = NormalizarColor(dto.colorHex);
            entidad.orden = dto.orden;

            await db.SaveChangesAsync(cancellationToken);

            int cantidad = await db.Publicaciones
                .AsNoTracking()
                .CountAsync(
                    x => x.categoriaPublicacionId == id,
                    cancellationToken);

            return Ok(new
            {
                mensaje = "Tipo de publicación actualizado correctamente.",
                data = Mapear(entidad, cantidad)
            });
        }

        [HttpPatch("{id:int}/estado")]
        public async Task<ActionResult> CambiarEstado(
            int id,
            [FromBody] CategoriaPublicacionEstadoDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            TipoPermisoApi permiso = dto.activo
                ? TipoPermisoApi.Actualizar
                : TipoPermisoApi.Eliminar;

            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                permiso,
                cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaPublicacion? entidad = await db
                .CategoriasPublicacion
                .FirstOrDefaultAsync(
                    x => x.categoriaPublicacionId == id,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "El tipo de publicación no fue encontrado."
                });
            }

            if (entidad.activo == dto.activo)
            {
                return Ok(new
                {
                    mensaje = dto.activo
                        ? "El tipo de publicación ya se encuentra activo."
                        : "El tipo de publicación ya se encuentra inactivo."
                });
            }

            int cantidadPublicaciones = await db.Publicaciones
                .AsNoTracking()
                .CountAsync(
                    x => x.categoriaPublicacionId == id,
                    cancellationToken);

            if (!dto.activo && cantidadPublicaciones > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede desactivar el tipo de publicación porque tiene publicaciones relacionadas.",
                    cantidadPublicaciones
                });
            }

            entidad.activo = dto.activo;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje = dto.activo
                    ? "Tipo de publicación reactivado correctamente."
                    : "Tipo de publicación desactivado correctamente.",
                data = Mapear(entidad, cantidadPublicaciones)
            });
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisoApiService
                .ValidarAsync(
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
                    message = resultado.Mensaje,
                    mensaje = resultado.Mensaje
                });
        }

        private static string NormalizarColor(string? color)
        {
            string valor = string.IsNullOrWhiteSpace(color)
                ? "#3B655B"
                : color.Trim();

            return valor.ToUpperInvariant();
        }

        private static object Mapear(
            CategoriaPublicacion entidad,
            int cantidadPublicaciones) =>
            new
            {
                entidad.categoriaPublicacionId,
                entidad.nombreCategoriaPublicacion,
                entidad.descripcionCategoriaPublicacion,
                entidad.colorHex,
                entidad.orden,
                entidad.activo,
                cantidadPublicaciones
            };
    }
}
