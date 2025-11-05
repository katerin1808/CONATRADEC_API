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
        private readonly DBContext _db;
        public RolPermisosController(DBContext db) => _db = db;

        // Helper: resolver IDs por nombre (sin exponer IDs al front)
        private async Task<(Rol rol, Interfaz interfaz)?> ResolveIdsAsync(string nombreRol, string nombreInterfaz)
        {
            var r = await _db.Roles.FirstOrDefaultAsync(x => x.nombreRol == nombreRol.Trim());
            if (r is null) return null;
            var p = await _db.Interfaz.FirstOrDefaultAsync(x => x.nombreInterfaz == nombreInterfaz.Trim());
            if (p is null) return null;
            return (r, p);
        }


        // ===========================================================
        // 3) STREAM AGRUPADO POR ROL (útil si haces grilla por rol)
        // ===========================================================
        // GET /api/rol-permisos/stream
        // GET /api/rol-permisos/stream?nombreRol=Admin
        [HttpGet("/api/rol-interfaz/matriz-por-rol", Name = "ListarRolConInterfazStream")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ListarRolConInterfazStream()
        {
            // Solo activos y sin tracking para rendimiento
            var rolesQ = _db.Roles.AsNoTracking().Where(r => r.activo);
            var interfazQ = _db.Interfaz.AsNoTracking().Where(p => p.activo);

            // Genera TODAS las combinaciones rol-permiso y hace left join contra RolPermisos
            var rows = await (
                from r in rolesQ
                from p in interfazQ
                join rp0 in _db.RolInteraz.AsNoTracking()
                    on new { r.rolId, p.interfazId } equals new { rp0.rolId, rp0.interfazId } into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol, p.nombreInterfaz
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    p.interfazId,
                    p.nombreInterfaz,
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
                    interfaz = g.Select(x => new InterfazPermisoDto
                    {
                        interfazId = x.interfazId,
                        nombreIntefaz = x.nombreInterfaz,
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
        [HttpGet("/api/rol-interfaz/matriz-por-rol-nombre", Name = "ListarRolConPermisosPorNombre")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>> ListarRolConPermisosPorNombre([FromQuery] string nombreRol)
        {
            if (string.IsNullOrWhiteSpace(nombreRol))
                return BadRequest("Debe proporcionar un nombre de rol.");

            // Filtramos solo el rol con ese nombre
            var rolesQ = _db.Roles.AsNoTracking()
                .Where(r => r.activo && r.nombreRol == nombreRol.Trim());

            var interfazQ = _db.Interfaz.AsNoTracking().Where(p => p.activo);

            // Genera TODAS las combinaciones del rol encontrado con los permisos
            var rows = await (
                from r in rolesQ
                from p in interfazQ
                join rp0 in _db.RolInteraz.AsNoTracking()
                    on new { r.rolId, p.interfazId } equals new { rp0.rolId, rp0.interfazId } into grp
                from rp in grp.DefaultIfEmpty()
                orderby r.nombreRol, p.nombreInterfaz
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    p.interfazId,
                    p.nombreInterfaz,
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
                    interfaz = g.Select(x => new InterfazPermisoDto
                    {
                        interfazId = x.interfazId,
                        nombreIntefaz = x.nombreInterfaz,
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
        [HttpPut("actualizar-interfaz")]
        public async Task<IActionResult> ActualizarPermisos([FromBody] List<RolConPermisosDto> items)
        {
            if (items is null || items.Count == 0)
                return BadRequest("El objeto está vacío.");

            using var trx = await _db.Database.BeginTransactionAsync();

            try
            {
                var rolIds = items.Select(i => i.rol.rolId).Distinct().ToList();
                var permisoIds = items.SelectMany(i => i.interfaz.Select(p => p.interfazId)).Distinct().ToList();

                // Validar existencia para evitar FK errors
                var rolesSet = (await _db.Roles
                    .Where(r => rolIds.Contains(r.rolId))
                    .Select(r => r.rolId)
                    .ToListAsync()).ToHashSet();

                var permisosSet = (await _db.Interfaz
                    .Where(p => permisoIds.Contains(p.interfazId))
                    .Select(p => p.interfazId)
                    .ToListAsync()).ToHashSet();

                // Relaciones existentes de los roles enviados
                var existentes = await _db.RolInteraz
                    .Where(rp => rolIds.Contains(rp.rolId))
                    .ToListAsync();

                var map = existentes.ToDictionary(k => (k.rolId, k.interfazId), v => v);

                foreach (var item in items)
                {
                    if (!rolesSet.Contains(item.rol.rolId)) continue;

                    foreach (var permiso in item.interfaz)
                    {
                        if (!permisosSet.Contains(permiso.interfazId)) continue;

                        var key = (item.rol.rolId, permiso.interfazId);

                        if (map.TryGetValue(key, out var rp)) // UPDATE
                        {
                            rp.leer = permiso.leer;
                            rp.agregar = permiso.agregar;
                            rp.actualizar = permiso.actualizar;
                            rp.eliminar = permiso.eliminar;
                            _db.RolInteraz.Update(rp);
                        }
                        else // INSERT (aunque todos sean false)
                        {
                            var nuevo = new RolInteraz
                            {
                                rolId = item.rol.rolId,
                                interfazId = permiso.interfazId,
                                leer = permiso.leer,
                                agregar = permiso.agregar,
                                actualizar = permiso.actualizar,
                                eliminar = permiso.eliminar,
                            };
                            _db.RolInteraz.Add(nuevo);
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
        [HttpPost("agregar-interfaz-por-nombre")]
        public async Task<IActionResult> AgregarPermisoPorNombre([FromBody] AgregarPermisoPorNombreRequest req)
        {
            if (req == null ||
                string.IsNullOrWhiteSpace(req.nombreRol) ||
                string.IsNullOrWhiteSpace(req.nombreInterfaz))
            {
                return BadRequest("Debe enviar nombreRol y nombrePermiso.");
            }

            var nombreRol = req.nombreRol.Trim();
            var nombrePermiso = req.nombreInterfaz.Trim();

            // Buscar rol por nombre (exacto). Si quieres parcial, usa EF.Functions.Like.
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null)
                return NotFound($"No se encontró el rol '{nombreRol}'.");

            // Buscar permiso por nombre (exacto)
            var permiso = await _db.Interfaz.FirstOrDefaultAsync(p => p.nombreInterfaz == nombrePermiso);
            if (permiso is null)
                return NotFound($"No se encontró el permiso '{nombrePermiso}'.");

            // Upsert por (rolId, permisoId)
            var existente = await _db.RolInteraz
                .FirstOrDefaultAsync(rp => rp.rolId == rol.rolId && rp.interfazId == permiso.interfazId);

            var accion = "actualizado";
            if (existente is null)
            {
                var nuevo = new RolInteraz
                {
                    rolId = rol.rolId,
                    interfazId = permiso.interfazId,
                    leer = req.leer,
                    agregar = req.agregar,
                    actualizar = req.actualizar,
                    eliminar = req.eliminar
                };
                _db.RolInteraz.Add(nuevo);
                accion = "insertado";
            }
            else
            {
                existente.leer = req.leer;
                existente.agregar = req.agregar;
                existente.actualizar = req.actualizar;
                existente.eliminar = req.eliminar;
                _db.RolInteraz.Update(existente);
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = $"Permiso {accion} correctamente.",
                rol = new { rol.rolId, rol.nombreRol },
                permiso = new { permiso.interfazId, permiso.nombreInterfaz },
                valores = new { req.leer, req.agregar, req.actualizar, req.eliminar }
            });
        }



    }



}
