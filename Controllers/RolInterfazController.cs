using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{


    [ApiController]
    [Route("api/rol-interfaz")]
    public class RolInterfazController : ControllerBase
    {
        private readonly DBContext _db;
        public RolInterfazController(DBContext db) => _db = db;

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
        public async Task<ActionResult<IEnumerable<RolConInterfazDto>>> ListarRolConInterfazStream()
        {
            // Solo activos y sin tracking para rendimiento
            var rolesQ = _db.Roles.AsNoTracking().Where(r => r.activo);
            var interfazQ = _db.Interfaz.AsNoTracking().Where(p => p.activo);

            // Genera TODAS las combinaciones rol-permiso y hace left join contra RolPermisos
            var rows = await (
                from r in rolesQ
                from p in interfazQ
                join rp0 in _db.RolInterfaz.AsNoTracking()
                    on new { r.rolId, p.interfazId } equals new { rp0.rolId, rp0.interfazId} into grp
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
                .Select(g => new RolConInterfazDto
                {
                    rol = new RolLiteDto
                    {
                        rolId = g.Key.rolId,
                        nombreRol = g.Key.nombreRol
                    },
                       interfaz= g.Select(x => new InterfazPermisoDto
                    {
                        interfazId = x.interfazId,
                        nombreInterfaz = x.nombreInterfaz,
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
       [HttpGet("/api/rol-permisos/matriz-por-rol-nombre", Name = "ListarRolConInterfazPorNombre")]
        public async Task<ActionResult<IEnumerable<RolConInterfazDto>>> ListarRolConInterfazPorNombre([FromQuery] string nombreRol)
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
                join rp0 in _db.RolInterfaz.AsNoTracking()
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
                .Select(g => new RolConInterfazDto
                {
                    rol = new RolLiteDto
                    {
                        rolId = g.Key.rolId,
                        nombreRol = g.Key.nombreRol
                    },
                       interfaz = g.Select(x => new InterfazPermisoDto
                    {
                        interfazId = x.interfazId,
                        nombreInterfaz = x.nombreInterfaz,
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
        public async Task<IActionResult> ActualizarInterfaz([FromBody] List<RolConInterfazDto> items)
        {
            if (items is null || items.Count == 0)
                return BadRequest("El payload está vacío.");

            using var trx = await _db.Database.BeginTransactionAsync();

            try
            {
                var rolIds = items.Select(i => i.rol.rolId).Distinct().ToList();
                var interfazIds = items.SelectMany(i => i.interfaz.Select(p => p.interfazId)).Distinct().ToList();

                // Validar existencia para evitar FK errors
                var rolesSet = (await _db.Roles
                    .Where(r => rolIds.Contains(r.rolId))
                    .Select(r => r.rolId)
                    .ToListAsync()).ToHashSet();

                var interfazSet = (await _db.Interfaz
                    .Where(p => interfazIds.Contains(p.interfazId))
                    .Select(p => p.interfazId)
                    .ToListAsync()).ToHashSet();

                // Relaciones existentes de los roles enviados
                var existentes = await _db.RolInterfaz
                    .Where(rp => rolIds.Contains(rp.rolId))
                    .ToListAsync();

                var map = existentes.ToDictionary(k => (k.rolId, k.interfazId), v => v);

                foreach (var item in items)
                {
                    if (!rolesSet.Contains(item.rol.rolId)) continue;

                    foreach (var interfaz in item.interfaz)
                    {
                        if (!interfazSet.Contains(interfaz.interfazId)) continue;

                        var key = (item.rol.rolId, interfaz.interfazId);

                        if (map.TryGetValue(key, out var rp)) // UPDATE
                        {
                            rp.leer = interfaz.leer;
                            rp.agregar = interfaz.agregar;
                            rp.actualizar = interfaz.actualizar;
                            rp.eliminar = interfaz.eliminar;
                            _db.RolInterfaz.Update(rp);
                        }
                        else // INSERT (aunque todos sean false)
                        {
                            var nuevo = new RolInterfaz
                            {
                                rolId = item.rol.rolId,
                                interfazId = interfaz.interfazId,
                                leer = interfaz.leer,
                                agregar = interfaz.agregar,
                                actualizar = interfaz.actualizar,
                                eliminar = interfaz.eliminar
                            };
                            _db.RolInterfaz.Add(nuevo);
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
        public async Task<IActionResult> AgregarInterfazPorNombre([FromBody] AgregarInterfazPorNombreRequest req)
        {
            if (req == null ||
                string.IsNullOrWhiteSpace(req.nombreRol) ||
                string.IsNullOrWhiteSpace(req.nombreInterfaz))
            {
                return BadRequest("Debe enviar nombreRol y nombrePermiso.");
            }

            var nombreRol = req.nombreRol.Trim();
            var nombreInterfaz = req.nombreInterfaz.Trim();

            // Buscar rol por nombre (exacto). Si quieres parcial, usa EF.Functions.Like.
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null)
                return NotFound($"No se encontró el rol '{nombreRol}'.");

            // Buscar interfaz por nombre (exacto)
            var interfaz = await _db.Interfaz.FirstOrDefaultAsync(p => p.nombreInterfaz == nombreInterfaz);
            if (interfaz is null)
                return NotFound($"No se encontró el permiso '{nombreInterfaz}'.");

            // Upsert por (rolId, interfazId)
            var existente = await _db.RolInterfaz
                .FirstOrDefaultAsync(rp => rp.rolId == rol.rolId && rp.interfazId == interfaz.interfazId);

            var accion = "actualizado";
            if (existente is null)
            {
                var nuevo = new RolInterfaz
                {
                    rolId = rol.rolId,
                    interfazId = interfaz.interfazId,
                    leer = req.leer,
                    agregar = req.agregar,
                    actualizar = req.actualizar,
                    eliminar = req.eliminar
                };
                _db.RolInterfaz.Add(nuevo);
                accion = "insertado";
            }
            else
            {
                existente.leer = req.leer;
                existente.agregar = req.agregar;
                existente.actualizar = req.actualizar;
                existente.eliminar = req.eliminar;
                _db.RolInterfaz.Update(existente);
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = $"Interfaz {accion} correctamente.",
                rol = new { rol.rolId, rol.nombreRol },
                permiso = new { interfaz.interfazId, interfaz.nombreInterfaz },
                valores = new { req.leer, req.agregar, req.actualizar, req.eliminar }
            });

        }
}
}

