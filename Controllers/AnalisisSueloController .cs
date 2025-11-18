using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/analisis-suelo")]
public class AnalisisSueloController : ControllerBase
{
    private readonly DBContext _db;

    public AnalisisSueloController(DBContext db)
    {
        _db = db;
    }

    // ============================
    // 1️⃣ CREAR ANÁLISIS DE SUELO
    // ============================
    [HttpPost("crear")]
    public async Task<IActionResult> Crear([FromBody] AnalisisSueloCrearDto req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        bool existe = await _db.AnalisisSuelos
            .AnyAsync(a => a.identificadorAnalisisSuelo == req.identificadorAnalisisSuelo);
        if (existe)
            return Conflict("El identificador ya existe.");

        var analisis = new AnalisisSuelo
        {
            fechaAnalisisSuelo = req.fechaAnalisisSuelo,
            laboratorioAnalasisSuelo = req.laboratorioAnalasisSuelo.Trim().ToUpper(),
            identificadorAnalisisSuelo = req.identificadorAnalisisSuelo.Trim().ToUpper(),
            activo = true
        };

        _db.AnalisisSuelos.Add(analisis);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(ObtenerPorId), new { id = analisis.analisisSueloId }, analisis);
    }

    // ============================
    // 2️⃣ AGREGAR ELEMENTO QUÍMICO
    // ============================
    [HttpPost("{analisisId:int}/agregar-elemento")]
    public async Task<IActionResult> AgregarElemento(int analisisId, [FromBody] AnalisisSueloElementoCrearDto dto)
    {
        var analisis = await _db.AnalisisSuelos.FindAsync(analisisId);
        if (analisis == null || !analisis.activo)
            return NotFound("Análisis no encontrado o inactivo.");

        var elemento = new AnalisisSueloElementoQuimico
        {
            cantidadElemento = dto.cantidadElemento,
            analisisSueloId = analisisId,
            elementoQuimicosId = dto.elementoQuimicosId,
            unidadMedidaId = dto.unidadMedidaId,
            activo = true
        };

        _db.AnalisisSueloElementos.Add(elemento);
        await _db.SaveChangesAsync();

        return Ok(new { mensaje = "Elemento agregado correctamente." });
    }

    // ============================
    // 3️⃣ OBTENER ANÁLISIS CON ELEMENTOS
    // ============================
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AnalisisSueloConElementosDto>> ObtenerPorId(int id)
    {
        var analisis = await _db.AnalisisSuelos
            .Where(a => a.analisisSueloId == id && a.activo)
            .Select(a => new AnalisisSueloConElementosDto
            {
                analisisSueloId = a.analisisSueloId,
                fechaAnalisisSuelo = a.fechaAnalisisSuelo,
                laboratorioAnalasisSuelo = a.laboratorioAnalasisSuelo,
                identificadorAnalisisSuelo = a.identificadorAnalisisSuelo,
                activo = a.activo,
                elementos = a.Elementos
                    .Where(e => e.activo)
                    .Select(e => new AnalisisSueloElementoDto
                    {
                        analisisSueloElementoQuimicoId = e.analisisSueloElementoQuimicoId,
                        cantidadElemento = e.cantidadElemento,
                        elementoQuimicosId = e.elementoQuimicosId,
                        nombreElementoQuimico = e.ElementoQuimicos.nombreElementoQuimico,
                        simboloElementoQuimico = e.ElementoQuimicos.simboloElementoQuimico,
                        unidadMedidaId = e.unidadMedidaId,
                        nombreUnidadMedida = e.UnidadMedida.nombreUnidadMedida,
                        activo = e.activo
                    }).ToList()
            })
            .FirstOrDefaultAsync();

        if (analisis == null)
            return NotFound("Análisis no encontrado.");

        return Ok(analisis);
    }

    // ============================
    // 4️⃣ LISTAR TODOS
    // ============================
    [HttpGet("listar")]
    public async Task<ActionResult<IEnumerable<AnalisisSueloDto>>> Listar()
    {
        var lista = await _db.AnalisisSuelos
            .Where(a => a.activo)
            .Select(a => new AnalisisSueloDto
            {
                analisisSueloId = a.analisisSueloId,
                fechaAnalisisSuelo = a.fechaAnalisisSuelo,
                laboratorioAnalasisSuelo = a.laboratorioAnalasisSuelo,
                identificadorAnalisisSuelo = a.identificadorAnalisisSuelo,
                activo = a.activo
            })
            .ToListAsync();

        return Ok(lista);
    }

    // ============================
    // 5️⃣ DESACTIVAR (Eliminar lógico)
    // ============================
    [HttpPut("desactivar/{id:int}")]
    public async Task<IActionResult> Desactivar(int id)
    {
        var analisis = await _db.AnalisisSuelos.FindAsync(id);
        if (analisis == null)
            return NotFound();

        analisis.activo = false;
        await _db.SaveChangesAsync();

        return Ok("Análisis desactivado correctamente.");
    }
}
