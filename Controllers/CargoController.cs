using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    
        [ApiController]
        [Route("api/[controller]")]
        public class CargosController : ControllerBase
        {
            private readonly RolContext _context;

            public CargosController(RolContext context)
            {
                _context = context;
            }

        // POST /crearCargo → Crea un nuevo cargo
        [HttpPost("crearCargo")]
        public async Task<IActionResult> CrearCargo([FromBody] CargoCreateDto dto)
        {
            // 1️⃣ Limpiar y normalizar el nombre
            var nombre = dto.nombreCargo.Trim();
            var nombreLower = nombre.ToLower();

            // 2️⃣ Verificar si ya existe un cargo activo con el mismo nombre
            var existeActivo = await _context.Cargos
                .AnyAsync(c => c.activo && c.nombreCargo.ToLower() == nombreLower);

            if (existeActivo)
                return Conflict("Ya existe un cargo activo con ese nombre.");

            // 3️⃣ Crear un nuevo cargo (aunque haya uno inactivo)
            var nuevoCargo = new Cargo
            {
                nombreCargo = nombre,
                descripcionCargo = dto.descripcionCargo,
                activo = true
            };

            _context.Cargos.Add(nuevoCargo);
            await _context.SaveChangesAsync();

            // 4️⃣ Devolver mensaje y datos del cargo creado
            return CreatedAtAction(nameof(BuscarCargo), new { id = nuevoCargo.cargoId }, new
            {
                message = "Cargo creado correctamente",
                nuevoCargo.cargoId,
                nuevoCargo.nombreCargo,
                nuevoCargo.descripcionCargo
            });
        }

        // 🔹 LISTAR (solo activos)
        [HttpGet("listarCargos")]
            public async Task<IActionResult> ListarCargos()
            {
                var cargos = await _context.Cargos
                    .Where(c => c.activo)
                    .Select(c => new
                    {
                        c.cargoId,
                        c.nombreCargo,
                        c.descripcionCargo
                    })
                    .ToListAsync();

                if (!cargos.Any())
                    return NotFound("No hay cargos activos registrados.");

                return Ok(cargos);
            }

            // 🔹 BUSCAR (solo activos)
            [HttpGet("buscarCargo/{id:int}")]
            public async Task<IActionResult> BuscarCargo(int id)
            {
                var cargo = await _context.Cargos.FindAsync(id);

                if (cargo == null || !cargo.activo)
                    return NotFound("No se encontró un cargo activo con ese ID.");

                return Ok(new
                {
                    cargo.cargoId,
                    cargo.nombreCargo,
                    cargo.descripcionCargo
                });
            }

        [HttpPut("editarCargo/{id:int}")]
        public async Task<IActionResult> UpdateCargo(int id, [FromBody] CargoUpdateDto dto)
        {
            // 1️⃣ Buscar el cargo por ID
            var cargo = await _context.Cargos.FindAsync(id);

            // 2️⃣ Validar que exista y esté activo
            if (cargo == null || !cargo.activo)
                return NotFound("No se encontró un cargo activo con ese ID");

            // 3️⃣ Actualizar solo los campos permitidos (sin tocar ID ni activo)
            cargo.nombreCargo = dto.nombreCargo.Trim();
            cargo.descripcionCargo = dto.descripcionCargo;

            // 4️⃣ Guardar cambios
            await _context.SaveChangesAsync();

            // 5️⃣ Devolver mensaje de éxito con los datos actualizados
            return Ok(new
            {
                mensaje = "Cargo actualizado correctamente",
                cargo = new
                {
                    
                    cargo.nombreCargo,
                    cargo.descripcionCargo
                }
            });
        }

        // DELETE /eliminarCargo/{id} → Borrado lógico (activo = false)
        [HttpDelete("eliminarCargo/{id:int}")]
        public async Task<IActionResult> DeleteCargo(int id)
        {
            // 1️ Buscar el cargo por ID
            var cargo = await _context.Cargos.FindAsync(id);
            if (cargo == null)
                return NotFound($"No se encontró un cargo con ID {id}");

            // 2️ Si ya está inactivo, devolver mensaje informativo
            if (!cargo.activo)
                return Ok(new { message = "El cargo ya estaba inactivo" });

            // 3️ Cambiar el estado a inactivo (borrado lógico)
            cargo.activo = false;

            // 4️ Guardar los cambios
            await _context.SaveChangesAsync();

            //  5️ Devolver solo mensaje (sin datos del cargo)
            return Ok(new { message = "Cargo desactivado (borrado lógico) correctamente" });
        }
    }
    }


