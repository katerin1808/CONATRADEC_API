using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{


    [ApiController]
    [Route("api/rol-permisos")]
    public class RolPermisosController : ControllerBase
    {
        private readonly RolContext _db;
        public RolPermisosController(RolContext db) => _db = db;

        // Helper: resolver IDs por nombre (sin exponer IDs al front)
        private async Task<(Rol rol, Permiso permiso)?> ResolveIdsAsync(string nombreRol, string nombrePermiso)
        {
            var r = await _db.Roles.FirstOrDefaultAsync(x => x.nombreRol == nombreRol.Trim());
            if (r is null) return null;
            var p = await _db.Permisos.FirstOrDefaultAsync(x => x.nombrePermiso == nombrePermiso.Trim());
            if (p is null) return null;
            return (r, p);
        }


        // ===========================================================
        // 3) STREAM AGRUPADO POR ROL (útil si haces grilla por rol)
        // ===========================================================
        // GET /api/rol-permisos/stream
        // GET /api/rol-permisos/stream?nombreRol=Admin
        [HttpGet("/api/rol-permisos/matriz-por-rol", Name = "ListarRolConPermisosStream")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ListarRolConPermisosStream()
        {
            // Solo activos y sin tracking para rendimiento
            var rolesQ = _db.Roles.AsNoTracking().Where(r => r.activo);
            var permisosQ = _db.Permisos.AsNoTracking().Where(p => p.activo);

            // Genera TODAS las combinaciones rol-permiso y hace left join contra RolPermisos
            var rows = await (
                from r in rolesQ
                from p in permisosQ
                join rp0 in _db.RolPermisos.AsNoTracking()
                    on new { r.rolId, p.permisoId } equals new { rp0.rolId, rp0.permisoId } into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol, p.nombrePermiso
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    p.permisoId,
                    p.nombrePermiso,
                    leer = rp != null && rp.leer,
                    agregar = rp != null && rp.agregar,
                    actualizar = rp != null && rp.actualizar,
                    eliminar = rp != null && rp.eliminar
                }
            ).ToListAsync();

            // Agrupa por rol y materializa el DTO final
            var result = rows
                .GroupBy(x => new { x.rolId, x.nombreRol })
                .Select(g => new RolConPermisosDto
                {
                    rol = new RolLiteDto
                    {
                        rolId = g.Key.rolId,
                        nombreRol = g.Key.nombreRol
                    },
                    permisos = g.Select(x => new InterfazPermisoDto
                    {
                        permisoId = x.permisoId,
                        nombrePermiso = x.nombrePermiso,
                        leer = x.leer,
                        agregar = x.agregar,
                        actualizar = x.actualizar,
                        eliminar = x.eliminar
                    }).ToList()
                })
                .ToList();

            return Ok(result);
        }
        
            // 4) STREAM FILTRADO POR NOMBRE DE ROL
           // ===========================================================
          // GET /api/rol-permisos/matriz-por-rol-nombre?nombreRol=Administrador
       [HttpGet("/api/rol-permisos/matriz-por-rol-nombre", Name = "ListarRolConPermisosPorNombre")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ListarRolConPermisosPorNombre([FromQuery] string nombreRol)
        {
            if (string.IsNullOrWhiteSpace(nombreRol))
                return BadRequest("Debe proporcionar un nombre de rol.");

            // Filtramos solo el rol con ese nombre
            var rolesQ = _db.Roles.AsNoTracking()
                .Where(r => r.activo && r.nombreRol == nombreRol.Trim());

            var permisosQ = _db.Permisos.AsNoTracking().Where(p => p.activo);

            // Genera TODAS las combinaciones del rol encontrado con los permisos
            var rows = await (
                from r in rolesQ
                from p in permisosQ
                join rp0 in _db.RolPermisos.AsNoTracking()
                    on new { r.rolId, p.permisoId } equals new { rp0.rolId, rp0.permisoId } into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol, p.nombrePermiso
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    p.permisoId,
                    p.nombrePermiso,
                    leer = rp != null && rp.leer,
                    agregar = rp != null && rp.agregar,
                    actualizar = rp != null && rp.actualizar,
                    eliminar = rp != null && rp.eliminar
                }
            ).ToListAsync();

            // Si no hay coincidencias, devolver 404
            if (rows.Count == 0)
                return NotFound($"No se encontró el rol '{nombreRol}' o no tiene permisos activos.");

            // Agrupa por rol (en este caso será solo uno)
            var result = rows
                .GroupBy(x => new { x.rolId, x.nombreRol })
                .Select(g => new RolConPermisosDto
                {
                    rol = new RolLiteDto
                    {
                        rolId = g.Key.rolId,
                        nombreRol = g.Key.nombreRol
                    },
                    permisos = g.Select(x => new InterfazPermisoDto
                    {
                        permisoId = x.permisoId,
                        nombrePermiso = x.nombrePermiso,
                        leer = x.leer,
                        agregar = x.agregar,
                        actualizar = x.actualizar,
                        eliminar = x.eliminar
                    }).ToList()
                })
                .ToList();

            return Ok(result);
        }


        // ===========================================================
        // 4) PUT UPSERT: inserta (aunque todo sea false) y actualiza si existe
        // PUT /api/rol-permisos/actualizar-permisos
        // body: List<RolConPermisosDto>
        // ===========================================================
        [HttpPut("actualizar-permisos")]
        public async Task<IActionResult> ActualizarPermisos([FromBody] List<RolConPermisosDto> items)
        {
            if (items is null || items.Count == 0)
                return BadRequest("El payload está vacío.");

            using var trx = await _db.Database.BeginTransactionAsync();

            try
            {
                var rolIds = items.Select(i => i.rol.rolId).Distinct().ToList();
                var permisoIds = items.SelectMany(i => i.permisos.Select(p => p.permisoId)).Distinct().ToList();

                // Validar existencia para evitar FK errors
                var rolesSet = (await _db.Roles
                    .Where(r => rolIds.Contains(r.rolId))
                    .Select(r => r.rolId)
                    .ToListAsync()).ToHashSet();

                var permisosSet = (await _db.Permisos
                    .Where(p => permisoIds.Contains(p.permisoId))
                    .Select(p => p.permisoId)
                    .ToListAsync()).ToHashSet();

                // Relaciones existentes de los roles enviados
                var existentes = await _db.RolPermisos
                    .Where(rp => rolIds.Contains(rp.rolId))
                    .ToListAsync();

                var map = existentes.ToDictionary(k => (k.rolId, k.permisoId), v => v);

                foreach (var item in items)
                {
                    if (!rolesSet.Contains(item.rol.rolId)) continue;

                    foreach (var permiso in item.permisos)
                    {
                        if (!permisosSet.Contains(permiso.permisoId)) continue;

                        var key = (item.rol.rolId, permiso.permisoId);

                        if (map.TryGetValue(key, out var rp)) // UPDATE
                        {
                            rp.leer = permiso.leer;
                            rp.agregar = permiso.agregar;
                            rp.actualizar = permiso.actualizar;
                            rp.eliminar = permiso.eliminar;
                            _db.RolPermisos.Update(rp);
                        }
                        else // INSERT (aunque todos sean false)
                        {
                            var nuevo = new RolPermiso
                            {
                                rolId = item.rol.rolId,
                                permisoId = permiso.permisoId,
                                leer = permiso.leer,
                                agregar = permiso.agregar,
                                actualizar = permiso.actualizar,
                                eliminar = permiso.eliminar
                            };
                            _db.RolPermisos.Add(nuevo);
                            map[key] = nuevo;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                await trx.CommitAsync();
                return Ok(); // o NoContent();
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }


        // POST /api/rol-permisos/agregar-permiso-por-nombre
        [HttpPost("agregar-permiso-por-nombre")]
        public async Task<IActionResult> AgregarPermisoPorNombre([FromBody] AgregarPermisoPorNombreRequest req)
        {
            if (req == null ||
                string.IsNullOrWhiteSpace(req.nombreRol) ||
                string.IsNullOrWhiteSpace(req.nombrePermiso))
            {
                return BadRequest("Debe enviar nombreRol y nombrePermiso.");
            }

            var nombreRol = req.nombreRol.Trim();
            var nombrePermiso = req.nombrePermiso.Trim();

            // Buscar rol por nombre (exacto). Si quieres parcial, usa EF.Functions.Like.
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null)
                return NotFound($"No se encontró el rol '{nombreRol}'.");

            // Buscar permiso por nombre (exacto)
            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso);
            if (permiso is null)
                return NotFound($"No se encontró el permiso '{nombrePermiso}'.");

            // Upsert por (rolId, permisoId)
            var existente = await _db.RolPermisos
                .FirstOrDefaultAsync(rp => rp.rolId == rol.rolId && rp.permisoId == permiso.permisoId);

            var accion = "actualizado";
            if (existente is null)
            {
                var nuevo = new RolPermiso
                {
                    rolId = rol.rolId,
                    permisoId = permiso.permisoId,
                    leer = req.leer,
                    agregar = req.agregar,
                    actualizar = req.actualizar,
                    eliminar = req.eliminar
                };
                _db.RolPermisos.Add(nuevo);
                accion = "insertado";
            }
            else
            {
                existente.leer = req.leer;
                existente.agregar = req.agregar;
                existente.actualizar = req.actualizar;
                existente.eliminar = req.eliminar;
                _db.RolPermisos.Update(existente);
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = $"Permiso {accion} correctamente.",
                rol = new { rol.rolId, rol.nombreRol },
                permiso = new { permiso.permisoId, permiso.nombrePermiso },
                valores = new { req.leer, req.agregar, req.actualizar, req.eliminar }
            });

        }
}
}

