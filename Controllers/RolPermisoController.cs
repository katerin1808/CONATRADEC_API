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


        [HttpPut("actualizar-permisos", Name = "ActualizarRolConPermisos")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ActualizarRolConPermisos(
    [FromBody] List<RolConPermisosDto> rolesConPermisos)
        {
            if (rolesConPermisos == null || rolesConPermisos.Count == 0)
                return BadRequest("La lista de roles con permisos está vacía o es inválida.");

            using var trx = await _db.Database.BeginTransactionAsync();
            try
            {
                var rolIds = rolesConPermisos.Select(r => r.rol.rolId).Distinct().ToList();
                var permisoIdsUs = rolesConPermisos.SelectMany(r => r.permisos.Select(p => p.permisoId)).Distinct().ToList();

                var rolesValidos = await _db.Roles.Where(r => rolIds.Contains(r.rolId)).Select(r => r.rolId).ToListAsync();
                var permisosValidos = await _db.Permisos.Where(p => permisoIdsUs.Contains(p.permisoId)).Select(p => p.permisoId).ToListAsync();

                // Cargamos relaciones existentes para acceso O(1)
                var existentes = await _db.RolPermisos.Where(x => rolIds.Contains(x.rolId)).ToListAsync();
                var map = existentes.ToDictionary(k => (k.rolId, k.permisoId), v => v);

                foreach (var rolDto in rolesConPermisos)
                {
                    if (!rolesValidos.Contains(rolDto.rol.rolId)) continue;

                    foreach (var permisoDto in rolDto.permisos)
                    {
                        if (!permisosValidos.Contains(permisoDto.permisoId)) continue;

                        var key = (rolDto.rol.rolId, permisoDto.permisoId);
                        var alguno = permisoDto.leer || permisoDto.agregar || permisoDto.actualizar || permisoDto.eliminar;

                        switch (rolDto.modo)
                        {
                            case ModoOperacionRol.Agregar:
                                // Solo crear si NO existe y hay al menos un flag en true
                                if (alguno && !map.ContainsKey(key))
                                {
                                    var nuevo = new RolPermiso
                                    {
                                        rolId = rolDto.rol.rolId,
                                        permisoId = permisoDto.permisoId,
                                        leer = permisoDto.leer,
                                        agregar = permisoDto.agregar,
                                        actualizar = permisoDto.actualizar,
                                        eliminar = permisoDto.eliminar
                                    };
                                    _db.RolPermisos.Add(nuevo);
                                    map[key] = nuevo;
                                }
                                // Si existe, se ignora (no se actualiza ni elimina)
                                break;

                            case ModoOperacionRol.Actualizar:
                                // Solo actualizar si YA existe
                                if (map.TryGetValue(key, out var rpAct))
                                {
                                    rpAct.leer = permisoDto.leer;
                                    rpAct.agregar = permisoDto.agregar;
                                    rpAct.actualizar = permisoDto.actualizar;
                                    rpAct.eliminar = permisoDto.eliminar;
                                    _db.RolPermisos.Update(rpAct);
                                }
                                // Si no existe, se ignora (no se crea)
                                break;

                            case ModoOperacionRol.Reemplazar:
                            default:
                                // Upsert: crea o actualiza; y si todos false y existe, elimina
                                if (alguno)
                                {
                                    if (!map.TryGetValue(key, out var rpRep))
                                    {
                                        rpRep = new RolPermiso
                                        {
                                            rolId = rolDto.rol.rolId,
                                            permisoId = permisoDto.permisoId,
                                            leer = permisoDto.leer,
                                            agregar = permisoDto.agregar,
                                            actualizar = permisoDto.actualizar,
                                            eliminar = permisoDto.eliminar
                                        };
                                        _db.RolPermisos.Add(rpRep);
                                        map[key] = rpRep;
                                    }
                                    else
                                    {
                                        rpRep.leer = permisoDto.leer;
                                        rpRep.agregar = permisoDto.agregar;
                                        rpRep.actualizar = permisoDto.actualizar;
                                        rpRep.eliminar = permisoDto.eliminar;
                                        _db.RolPermisos.Update(rpRep);
                                    }
                                }
                                else if (map.TryGetValue(key, out var rpDel))
                                {
                                    _db.RolPermisos.Remove(rpDel);
                                    map.Remove(key);
                                }
                                break;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                await trx.CommitAsync();

                // Devuelve SIEMPRE la misma lista completa
                var result = await ListarRolConPermisosStream();
                return Ok(result);
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }

        }
}
}
