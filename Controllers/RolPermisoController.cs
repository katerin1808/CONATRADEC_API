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
        [HttpGet("stream", Name = "ListarRolConPermisosStream")]
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




    }
}
