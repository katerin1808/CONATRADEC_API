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
        // 1) MATRIZ EXACTA: CROSS JOIN Roles × Permisos + LEFT JOIN RolPermiso
        //    (equivalente a tu SELECT con ISNULL -> false)
        // ===========================================================
        // GET /api/rol-permisos/matriz-exacta
        //     ?incluirInactivos=true|false
        //     &soloInactivos=true|false
        //     &soloFaltantes=true|false
        //     &nombreRol=...&nombrePermiso=...
        [HttpGet("matriz-exacta", Name = "ObtenerMatrizRolPermiso")]
        public async Task<ActionResult<IEnumerable<MatrizRowDto>>> ObtenerMatrizRolPermiso(
            [FromQuery] bool incluirInactivos = false,
            [FromQuery] bool soloInactivos = false,
            [FromQuery] bool soloFaltantes = false,
            [FromQuery] string? nombreRol = null,
            [FromQuery] string? nombrePermiso = null)
        {
            var rolesQ = incluirInactivos ? _db.Roles.AsQueryable() : _db.Roles.Where(r => r.activo);
            var permisosQ = incluirInactivos ? _db.Permisos.AsQueryable() : _db.Permisos.Where(p => p.activo);

            if (!string.IsNullOrWhiteSpace(nombreRol))
                rolesQ = rolesQ.Where(r => r.nombreRol == nombreRol.Trim());
            if (!string.IsNullOrWhiteSpace(nombrePermiso))
                permisosQ = permisosQ.Where(p => p.nombrePermiso == nombrePermiso.Trim());

            var query =
                from r in rolesQ
                from p in permisosQ
                join rp0 in _db.RolPermisos
                    on new { r.rolId, p.permisoId } equals new { rp0.rolId, rp0.permisoId } into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol, p.nombrePermiso
                select new MatrizRowDto
                {
                    rolId = r.rolId,
                    nombreRol = r.nombreRol,
                    rolActivo = r.activo,

                    permisoId = p.permisoId,
                    nombrePermiso = p.nombrePermiso,
                    permisoActivo = p.activo,

                    leer = rp != null && rp.leer,
                    agregar = rp != null && rp.agregar,
                    actualizar = rp != null && rp.actualizar,
                    eliminar = rp != null && rp.eliminar
                };

            // Filtros posteriores
            if (soloInactivos)
                query = query.Where(x => !x.rolActivo || !x.permisoActivo);

            if (soloFaltantes)
            {
                // faltantes = no tiene ningún flag en true y no existe la fila
                // (como no proyectamos rpExiste, aproximamos: faltantes = todos false)
                query = query.Where(x => !x.leer && !x.agregar && !x.actualizar && !x.eliminar);
            }

            var data = await query.ToListAsync();
            return Ok(data);
        }

        // ===========================================================
        // 2) VISTA POR INTERFAZ (como tu imagen: filas=roles, columnas=flags)
        // ===========================================================
        // GET /api/rol-permisos/matriz-por-interfaz?nombrePermiso=Usuarios
        //      &incluirInactivos=true|false
        //      &soloFaltantes=true|false
        [HttpGet("matriz-por-interfaz", Name = "ListarRolesPorInterfaz")]
        public async Task<ActionResult<IEnumerable<RolFlagsPorInterfazDto>>> ListarRolesPorInterfaz(
            [FromQuery] string nombrePermiso,
            [FromQuery] bool incluirInactivos = false,
            [FromQuery] bool soloFaltantes = false)
        {
            if (string.IsNullOrWhiteSpace(nombrePermiso))
                return BadRequest("nombrePermiso es requerido.");

            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso.Trim());
            if (permiso is null) return NotFound($"No existe la interfaz/permiso '{nombrePermiso}'.");

            var rolesQ = incluirInactivos ? _db.Roles.AsQueryable()
                                          : _db.Roles.Where(r => r.activo);

            var baseQ =
                from r in rolesQ
                join rp0 in _db.RolPermisos.Where(x => x.permisoId == permiso.permisoId)
                    on r.rolId equals rp0.rolId into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol
                select new RolFlagsPorInterfazDto
                {
                    rolId = r.rolId,
                    nombreRol = r.nombreRol,
                    rolActivo = r.activo,
                    leer = rp != null && rp.leer,
                    agregar = rp != null && rp.agregar,
                    actualizar = rp != null && rp.actualizar,
                    eliminar = rp != null && rp.eliminar
                };

            if (soloFaltantes)
                baseQ = baseQ.Where(x => !x.leer && !x.agregar && !x.actualizar && !x.eliminar);

            return Ok(await baseQ.ToListAsync());
        }

        // ===========================================================
        // 3) STREAM AGRUPADO POR ROL (útil si haces grilla por rol)
        // ===========================================================
        // GET /api/rol-permisos/stream
        // GET /api/rol-permisos/stream?nombreRol=Admin
        [HttpGet("stream", Name = "ListarRolConPermisosStream")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ListarRolConPermisosStream(
            [FromQuery] string? nombreRol = null)
        {
            var rolesQ = _db.Roles.Where(r => r.activo);
            if (!string.IsNullOrWhiteSpace(nombreRol))
                rolesQ = rolesQ.Where(r => r.nombreRol == nombreRol.Trim());

            var permisosQ = _db.Permisos.Where(p => p.activo);

            var rows = await (
                from r in rolesQ
                from p in permisosQ
                join rp0 in _db.RolPermisos
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

            var result = rows
                .GroupBy(x => new { x.rolId, x.nombreRol })
                .Select(g => new RolConPermisosDto
                {
                    rol = new RolLiteDto { rolId = g.Key.rolId, nombreRol = g.Key.nombreRol },
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
        // 4) CREAR relación por NOMBRES (sin pedir IDs)
        // ===========================================================
        // POST /api/rol-permisos/by-name
        [HttpPost("by-name", Name = "AgregarRolPermisoPorNombre")]
        public async Task<ActionResult> AgregarRolPermisoPorNombre([FromBody] RolPermisoCreateByNameDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var resolved = await ResolveIdsAsync(dto.nombreRol, dto.nombrePermiso);
            if (resolved is null)
                return NotFound($"No existe el rol '{dto.nombreRol}' o el permiso '{dto.nombrePermiso}'.");

            var (rol, permiso) = resolved.Value;

            var exists = await _db.RolPermisos.AnyAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);
            if (exists) return Conflict("Ya existe la relación rol-permiso.");

            _db.RolPermisos.Add(new RolPermiso
            {
                rolId = rol.rolId,
                permisoId = permiso.permisoId,
                leer = dto.leer,
                agregar = dto.agregar,
                actualizar = dto.actualizar,
                eliminar = dto.eliminar
            });

            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(ObtenerMatrizRolPermiso), null);
        }

        // ===========================================================
        // 5) ACTUALIZAR por NOMBRES
        // ===========================================================
        // PUT /api/rol-permisos/by-name
        [HttpPut("by-name", Name = "ActualizarRolPermisoPorNombre")]
        public async Task<IActionResult> ActualizarRolPermisoPorNombre([FromBody] RolPermisoUpdateByNameDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var resolved = await ResolveIdsAsync(dto.nombreRol, dto.nombrePermiso);
            if (resolved is null)
                return NotFound($"No existe el rol '{dto.nombreRol}' o el permiso '{dto.nombrePermiso}'.");

            var (rol, permiso) = resolved.Value;

            var rp = await _db.RolPermisos.FirstOrDefaultAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);
            if (rp is null) return NotFound("La relación rol-permiso no existe.");

            rp.leer = dto.leer;
            rp.agregar = dto.agregar;
            rp.actualizar = dto.actualizar;
            rp.eliminar = dto.eliminar;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ===========================================================
        // 6) ELIMINAR por NOMBRES
        // ===========================================================
        // DELETE /api/rol-permisos/by-name?nombreRol=...&nombrePermiso=...
        [HttpDelete("by-name", Name = "EliminarRolPermisoPorNombre")]
        public async Task<IActionResult> EliminarRolPermisoPorNombre(
            [FromQuery] string nombreRol,
            [FromQuery] string nombrePermiso)
        {
            var resolved = await ResolveIdsAsync(nombreRol, nombrePermiso);
            if (resolved is null)
                return NotFound($"No existe el rol '{nombreRol}' o el permiso '{nombrePermiso}'.");

            var (rol, permiso) = resolved.Value;

            var rp = await _db.RolPermisos.FirstOrDefaultAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);
            if (rp is null) return NotFound("La relación rol-permiso no existe.");

            _db.RolPermisos.Remove(rp);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ===========================================================
        // 7) BULK UPSERT por ROL (guardar toda la grilla de un rol)
        // ===========================================================
        // POST /api/rol-permisos/bulk-upsert-by-name
        // Body: { nombreRol, items:[{ nombrePermiso, leer,agregar,actualizar,eliminar }, ...] }
        [HttpPost("bulk-upsert-by-name", Name = "BulkUpsertRolPermisoPorNombre")]
        public async Task<ActionResult> BulkUpsertRolPermisoPorNombre([FromBody] BulkUpsertByNameDto body)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == body.nombreRol.Trim());
            if (rol is null) return NotFound($"No existe el rol '{body.nombreRol}'.");
            if (body.items is null || body.items.Count == 0) return BadRequest("items no puede estar vacío.");

            var nombresPermiso = body.items.Select(i => i.nombrePermiso.Trim()).Distinct().ToList();
            var permisos = await _db.Permisos.Where(p => nombresPermiso.Contains(p.nombrePermiso)).ToListAsync();
            var faltantes = nombresPermiso.Except(permisos.Select(p => p.nombrePermiso)).ToList();
            if (faltantes.Any()) return NotFound($"Permisos no encontrados: {string.Join(", ", faltantes)}");

            var mapPerm = permisos.ToDictionary(p => p.nombrePermiso, p => p.permisoId);

            var permisoIds = permisos.Select(p => p.permisoId).ToList();
            var actuales = await _db.RolPermisos
                .Where(rp => rp.rolId == rol.rolId && permisoIds.Contains(rp.permisoId))
                .ToListAsync();
            var mapAct = actuales.ToDictionary(a => a.permisoId, a => a);

            int creados = 0, actualizados = 0;
            foreach (var item in body.items)
            {
                var pid = mapPerm[item.nombrePermiso.Trim()];
                if (mapAct.TryGetValue(pid, out var rp))
                {
                    if (rp.leer != item.leer || rp.agregar != item.agregar || rp.actualizar != item.actualizar || rp.eliminar != item.eliminar)
                    {
                        rp.leer = item.leer;
                        rp.agregar = item.agregar;
                        rp.actualizar = item.actualizar;
                        rp.eliminar = item.eliminar;
                        actualizados++;
                    }
                }
                else
                {
                    _db.RolPermisos.Add(new RolPermiso
                    {
                        rolId = rol.rolId,
                        permisoId = pid,
                        leer = item.leer,
                        agregar = item.agregar,
                        actualizar = item.actualizar,
                        eliminar = item.eliminar
                    });
                    creados++;
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { mensaje = "Matriz aplicada", rol = rol.nombreRol, creados, actualizados, total = body.items.Count });
        }

        // ===========================================================
        // 8) BULK UPSERT por INTERFAZ (guardar grilla de una interfaz)
        // ===========================================================
        // POST /api/rol-permisos/bulk-upsert-por-interfaz
        // Body: { nombrePermiso, roles:[{ nombreRol, leer,agregar,actualizar,eliminar }, ...] }
        [HttpPost("bulk-upsert-por-interfaz", Name = "BulkUpsertPorInterfaz")]
        public async Task<ActionResult> BulkUpsertPorInterfaz([FromBody] BulkUpsertPorInterfazDto body)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == body.nombrePermiso.Trim());
            if (permiso is null) return NotFound($"No existe la interfaz/permiso '{body.nombrePermiso}'.");
            if (body.roles is null || body.roles.Count == 0) return BadRequest("roles no puede estar vacío.");

            var nombresRol = body.roles.Select(r => r.nombreRol.Trim()).Distinct().ToList();
            var roles = await _db.Roles.Where(r => nombresRol.Contains(r.nombreRol)).ToListAsync();
            var faltanRoles = nombresRol.Except(roles.Select(r => r.nombreRol)).ToList();
            if (faltanRoles.Any()) return NotFound($"Roles no encontrados: {string.Join(", ", faltanRoles)}");

            var mapRol = roles.ToDictionary(r => r.nombreRol, r => r.rolId);
            var rolIds = roles.Select(r => r.rolId).ToList();

            var actuales = await _db.RolPermisos
                .Where(rp => rp.permisoId == permiso.permisoId && rolIds.Contains(rp.rolId))
                .ToListAsync();
            var mapAct = actuales.ToDictionary(a => a.rolId, a => a);

            int creados = 0, actualizados = 0;
            foreach (var item in body.roles)
            {
                var rid = mapRol[item.nombreRol.Trim()];
                if (mapAct.TryGetValue(rid, out var rp))
                {
                    if (rp.leer != item.leer || rp.agregar != item.agregar || rp.actualizar != item.actualizar || rp.eliminar != item.eliminar)
                    {
                        rp.leer = item.leer;
                        rp.agregar = item.agregar;
                        rp.actualizar = item.actualizar;
                        rp.eliminar = item.eliminar;
                        actualizados++;
                    }
                }
                else
                {
                    _db.RolPermisos.Add(new RolPermiso
                    {
                        rolId = rid,
                        permisoId = permiso.permisoId,
                        leer = item.leer,
                        agregar = item.agregar,
                        actualizar = item.actualizar,
                        eliminar = item.eliminar
                    });
                    creados++;
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { mensaje = "Cambios aplicados", permiso = permiso.nombrePermiso, creados, actualizados, total = body.roles.Count });
        }
    }
}
