using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
                    leer = rp != null && rp.leer == true ? true : false,
                    agregar = rp != null && rp.agregar == true ? true : false,
                    actualizar = rp != null && rp.actualizar == true ? true : false,
                    eliminar = rp != null && rp.eliminar == true ? true : false
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
                    leer = rp != null && rp.leer == true ? true : false,
                    agregar = rp != null && rp.agregar == true ? true : false,
                    actualizar = rp != null && rp.actualizar == true ? true : false,
                    eliminar = rp != null && rp.eliminar == true ? true : false
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
        public async Task<IActionResult> ActualizarPermisos([FromBody] RolConPermisosDto dto)
        {
            if (dto is null || dto.rol is null || dto.interfaz is null || dto.interfaz.Count == 0)
                return BadRequest("El objeto está vacío o mal formado.");

            await using var trx = await _db.Database.BeginTransactionAsync();
            try
            {
                int rolId = dto.rol.rolId;

                // 1️⃣ Validar que el rol exista
                bool rolExiste = await _db.Roles.AnyAsync(r => r.rolId == rolId);
                if (!rolExiste)
                    return NotFound($"El rol con ID {rolId} no existe.");

                // 2️⃣ Obtener IDs de interfaces válidas
                var interfazIds = dto.interfaz.Select(i => i.interfazId).Distinct().ToList();

                var interfazValidas = await _db.Interfaz
                    .Where(i => interfazIds.Contains(i.interfazId))
                    .Select(i => i.interfazId)
                    .ToListAsync();

                // 3️⃣ Traer las relaciones existentes del rol
                var existentes = await _db.RolInteraz
                    .Where(rp => rp.rolId == rolId)
                    .ToListAsync();

                // Crear diccionario (clave: interfazId → valor: entidad RolInteraz)
                var map = existentes.ToDictionary(k => k.interfazId, v => v);

                // 4️⃣ Recorrer todas las interfaces del DTO
                foreach (var permiso in dto.interfaz)
                {
                    if (!interfazValidas.Contains(permiso.interfazId))
                        continue;

                    if (map.TryGetValue(permiso.interfazId, out var rp)) // UPDATE
                    {
                        rp.leer = permiso.leer;
                        rp.agregar = permiso.agregar;
                        rp.actualizar = permiso.actualizar;
                        rp.eliminar = permiso.eliminar;

                        _db.RolInteraz.Update(rp);
                    }
                    else // INSERT
                    {
                        var nuevo = new RolInteraz
                        {
                            rolId = dto.rol.rolId,
                            interfazId = permiso.interfazId,
                            leer = permiso.leer == true ? true : false,
                            agregar = permiso.agregar == true ? true : false,
                            actualizar = permiso.actualizar == true ? true : false,
                            eliminar = permiso.eliminar == true ? true : false,
                        };
                        if(nuevo.leer.Value.Equals(true) || nuevo.agregar.Value.Equals(true) || nuevo.actualizar.Value.Equals(true) || nuevo.eliminar.Value.Equals(true))
                            _db.RolInteraz.Add(nuevo);

                    }
                }

                // 5️⃣ Guardar y confirmar
                await _db.SaveChangesAsync();
                await trx.CommitAsync();


                return Ok(new
                {
                    message = "Permisos actualizados correctamente.",
                });
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync();
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
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
