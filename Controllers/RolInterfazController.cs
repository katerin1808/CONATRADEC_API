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

        // ===========================================================
        //  Helper: resolver IDs por nombre (sin exponer IDs al front)
        // ===========================================================
        private async Task<(Rol rol, Interfaz interfaz)?> ResolveIdsAsync(string nombreRol, string nombreInterfaz)
        {
            var r = await _db.Roles.FirstOrDefaultAsync(x => x.nombreRol == nombreRol.Trim());
            if (r is null) return null;

            var i = await _db.Interfaz.FirstOrDefaultAsync(x => x.nombreInterfaz == nombreInterfaz.Trim());
            if (i is null) return null;

            return (r, i);
        }

        // ===========================================================
        // 3) STREAM AGRUPADO POR ROL
        // ===========================================================
        [HttpGet("matriz-por-rol")]
        public async Task<ActionResult<IEnumerable<RolConInterfazDto>>> ListarRolConInterfazStream()
        {
            var rolesQ = _db.Roles.AsNoTracking().Where(r => r.activo);
            var interfazQ = _db.Interfaz.AsNoTracking().Where(p => p.activo);

            var rows = await (
                from r in rolesQ
                from i in interfazQ
                join ri0 in _db.RolInterfaz.AsNoTracking()
                    on new { r.rolId, i.interfazId } equals new { ri0.rolId, ri0.interfazId } into grp
                from ri in grp.DefaultIfEmpty()
                orderby r.nombreRol, i.nombreInterfaz
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    i.interfazId,
                    i.nombreInterfaz,
                    leer = ri != null && ri.leer,
                    agregar = ri != null && ri.agregar,
                    actualizar = ri != null && ri.actualizar,
                    eliminar = ri != null && ri.eliminar
                }
            ).ToListAsync();

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
        // 4) STREAM FILTRADO POR NOMBRE DE ROL
        // ===========================================================
        [HttpGet("matriz-por-rol-nombre")]
        public async Task<ActionResult<IEnumerable<RolConInterfazDto>>> ListarRolConInterfazPorNombre([FromQuery] string nombreRol)
        {
            if (string.IsNullOrWhiteSpace(nombreRol))
                return BadRequest("Debe proporcionar un nombre de rol.");

            var rolesQ = _db.Roles.AsNoTracking()
                .Where(r => r.activo && r.nombreRol == nombreRol.Trim());

            var interfazQ = _db.Interfaz.AsNoTracking().Where(p => p.activo);

            var rows = await (
                from r in rolesQ
                from i in interfazQ
                join ri0 in _db.RolInterfaz.AsNoTracking()
                    on new { r.rolId, i.interfazId } equals new { ri0.rolId, ri0.interfazId } into grp
                from ri in grp.DefaultIfEmpty()
                orderby r.nombreRol, i.nombreInterfaz
                select new
                {
                    r.rolId,
                    r.nombreRol,
                    i.interfazId,
                    i.nombreInterfaz,
                    leer = ri != null && ri.leer,
                    agregar = ri != null && ri.agregar,
                    actualizar = ri != null && ri.actualizar,
                    eliminar = ri != null && ri.eliminar
                }
            ).ToListAsync();

            if (rows.Count == 0)
                return NotFound($"No se encontró el rol '{nombreRol}' o no tiene interfaces activas.");

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
        // 5) PUT UPSERT: Inserta/actualiza interfaces de un rol
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

                var rolesSet = (await _db.Roles
                    .Where(r => rolIds.Contains(r.rolId))
                    .Select(r => r.rolId)
                    .ToListAsync()).ToHashSet();

                var interfazSet = (await _db.Interfaz
                    .Where(p => interfazIds.Contains(p.interfazId))
                    .Select(p => p.interfazId)
                    .ToListAsync()).ToHashSet();

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
                        else // INSERT
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
                return Ok(new { mensaje = "Permisos actualizados correctamente." });
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }

        // ===========================================================
        // 6) POST: Agregar o actualizar interfaz por nombre
        // ===========================================================
        [HttpPost("agregar-interfaz-por-nombre")]
        public async Task<IActionResult> AgregarInterfazPorNombre([FromBody] AgregarInterfazPorNombreRequest req)
        {
            if (req == null ||
                string.IsNullOrWhiteSpace(req.nombreRol) ||
                string.IsNullOrWhiteSpace(req.nombreInterfaz))
            {
                return BadRequest("Debe enviar nombreRol y nombreInterfaz.");
            }

            var nombreRol = req.nombreRol.Trim();
            var nombreInterfaz = req.nombreInterfaz.Trim();

            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null)
                return NotFound($"No se encontró el rol '{nombreRol}'.");

            var interfaz = await _db.Interfaz.FirstOrDefaultAsync(p => p.nombreInterfaz == nombreInterfaz);
            if (interfaz is null)
                return NotFound($"No se encontró la interfaz '{nombreInterfaz}'.");

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
                interfaz = new { interfaz.interfazId, interfaz.nombreInterfaz },
                valores = new { req.leer, req.agregar, req.actualizar, req.eliminar }
            });
        }
}
}

