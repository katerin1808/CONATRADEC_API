using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Flujo explícito para crear una fuente aunque exista un homónimo inactivo
/// o para reactivarlo reemplazando sus datos. Nunca permite duplicados activos.
/// </summary>
[ApiController]
[Route("api/fuente-nutriente")]
public sealed class FuenteNutrienteReactivacionController : ControllerBase
{
    private readonly DBContext db;

    public FuenteNutrienteReactivacionController(DBContext db) =>
        this.db = db;

    [HttpPost("crear-con-elementos-confirmado")]
    public async Task<IActionResult> CrearConfirmada(
        [FromBody] FuenteNutrienteConElementosCrearDto dto,
        CancellationToken cancellationToken)
    {
        IActionResult? error = await ValidarAsync(dto, null, cancellationToken);
        if (error != null)
            return error;

        string nombre = Normalizar(dto.nombreNutriente);

        if (await ExisteActivaAsync(nombre, null, cancellationToken))
        {
            return Conflict(new
            {
                codigo = "FUENTE_NUTRIENTE_ACTIVA_DUPLICADA",
                mensaje = "Ya existe una fuente nutriente activa con ese nombre."
            });
        }

        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var fuente = new FuenteNutriente
            {
                nombreNutriente = nombre,
                descripcionNutriente = dto.descripcionNutriente?.Trim() ?? string.Empty,
                precioNutriente = dto.precioNutriente,
                activo = true
            };

            db.fuenteNutriente.Add(fuente);
            await db.SaveChangesAsync(cancellationToken);

            AgregarAportes(fuente.fuenteNutrientesId, dto.elementosQuimicos);
            await db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            return Ok(Respuesta(
                "Nueva fuente nutriente creada correctamente.",
                fuente));
        }
        catch (Exception ex)
        {
            await transaccion.RollbackAsync(cancellationToken);
            return ErrorInterno(
                "Ocurrió un error al crear la nueva fuente nutriente.",
                ex);
        }
    }

    [HttpPut("reactivar-con-elementos/{id:int}")]
    public async Task<IActionResult> ReactivarConElementos(
        int id,
        [FromBody] FuenteNutrienteConElementosCrearDto dto,
        CancellationToken cancellationToken)
    {
        IActionResult? error = await ValidarAsync(dto, id, cancellationToken);
        if (error != null)
            return error;

        FuenteNutriente? fuente = await db.fuenteNutriente
            .FirstOrDefaultAsync(x => x.fuenteNutrientesId == id, cancellationToken);

        if (fuente == null)
            return NotFound(new { mensaje = "Fuente nutriente no encontrada." });

        if (fuente.activo)
            return Conflict(new { mensaje = "La fuente nutriente ya está activa." });

        string nombre = Normalizar(dto.nombreNutriente);

        if (await ExisteActivaAsync(nombre, id, cancellationToken))
        {
            return Conflict(new
            {
                codigo = "FUENTE_NUTRIENTE_ACTIVA_DUPLICADA",
                mensaje = "Ya existe otra fuente nutriente activa con ese nombre."
            });
        }

        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            fuente.nombreNutriente = nombre;
            fuente.descripcionNutriente = dto.descripcionNutriente?.Trim() ?? string.Empty;
            fuente.precioNutriente = dto.precioNutriente;
            fuente.activo = true;

            await DesactivarConfiguracionAnteriorAsync(id, cancellationToken);
            AgregarAportes(id, dto.elementosQuimicos);

            await db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            return Ok(Respuesta(
                "Fuente nutriente reactivada y actualizada correctamente.",
                fuente));
        }
        catch (Exception ex)
        {
            await transaccion.RollbackAsync(cancellationToken);
            return ErrorInterno(
                "Ocurrió un error al reactivar la fuente nutriente.",
                ex);
        }
    }

    private async Task<IActionResult?> ValidarAsync(
        FuenteNutrienteConElementosCrearDto dto,
        int? idExcluir,
        CancellationToken cancellationToken)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.nombreNutriente))
            return BadRequest(new { mensaje = "El nombre nutriente es obligatorio." });

        string nombre = Normalizar(dto.nombreNutriente);
        if (nombre.Length > 100)
            return BadRequest(new { mensaje = "El nombre no puede superar 100 caracteres." });

        if ((dto.descripcionNutriente?.Trim().Length ?? 0) > 250)
            return BadRequest(new { mensaje = "La descripción no puede superar 250 caracteres." });

        if (dto.precioNutriente <= 0)
            return BadRequest(new { mensaje = "El precio por quintal debe ser mayor a cero." });

        List<ElementoFuenteCrearDto> elementos =
            dto.elementosQuimicos ?? new List<ElementoFuenteCrearDto>();

        if (elementos.GroupBy(x => x.elementoQuimicosId).Any(x => x.Count() > 1))
            return BadRequest(new { mensaje = "No puede repetir elementos químicos." });

        if (elementos.Any(x =>
                x.elementoQuimicosId <= 0 ||
                x.cantidadAporte <= 0 ||
                x.cantidadAporte > 100))
        {
            return BadRequest(new
            {
                mensaje = "Cada aporte debe ser mayor a cero y menor o igual a 100."
            });
        }

        int[] ids = elementos.Select(x => x.elementoQuimicosId).Distinct().ToArray();
        if (ids.Length > 0)
        {
            int existentes = await db.elementoQuimico.CountAsync(
                x => ids.Contains(x.elementoQuimicosId) && x.activo,
                cancellationToken);

            if (existentes != ids.Length)
            {
                return BadRequest(new
                {
                    mensaje = "Uno o más elementos químicos no existen o están inactivos."
                });
            }
        }

        if (idExcluir.HasValue &&
            await ExisteActivaAsync(nombre, idExcluir, cancellationToken))
        {
            return Conflict(new
            {
                mensaje = "Ya existe otra fuente nutriente activa con ese nombre."
            });
        }

        return null;
    }

    private async Task<bool> ExisteActivaAsync(
        string nombre,
        int? idExcluir,
        CancellationToken cancellationToken) =>
        await db.fuenteNutriente.AnyAsync(
            x =>
                x.activo &&
                (!idExcluir.HasValue || x.fuenteNutrientesId != idExcluir.Value) &&
                x.nombreNutriente.Trim().ToUpper() == nombre,
            cancellationToken);

    private async Task DesactivarConfiguracionAnteriorAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var aportes = await db.fuenteNutrienteElementoQuimico
            .Where(x => x.fuenteNutrientesId == id && x.activo)
            .ToListAsync(cancellationToken);
        aportes.ForEach(x => x.activo = false);

        var enmiendas = await db.ParametroEnmiendaCalcarea
            .Where(x => x.fuenteNutrientesId == id && x.activo)
            .ToListAsync(cancellationToken);
        enmiendas.ForEach(x => x.activo = false);

        var mixtas = await db.fuenteFertilizacionMixta
            .Where(x => x.fuenteNutrientesId == id && x.activo)
            .ToListAsync(cancellationToken);
        mixtas.ForEach(x => x.activo = false);
    }

    private void AgregarAportes(
        int fuenteId,
        IEnumerable<ElementoFuenteCrearDto>? elementos)
    {
        var aportes = (elementos ?? Enumerable.Empty<ElementoFuenteCrearDto>())
            .Select(x => new FuenteNutrienteElementoQuimico
            {
                fuenteNutrientesId = fuenteId,
                elementoQuimicosId = x.elementoQuimicosId,
                cantidadAporte = x.cantidadAporte,
                activo = true
            })
            .ToList();

        if (aportes.Count > 0)
            db.fuenteNutrienteElementoQuimico.AddRange(aportes);
    }

    private static object Respuesta(string mensaje, FuenteNutriente fuente) => new
    {
        mensaje,
        data = new
        {
            fuenteNutrientesId = fuente.fuenteNutrientesId,
            nombreNutriente = fuente.nombreNutriente,
            descripcionNutriente = fuente.descripcionNutriente,
            precioNutriente = fuente.precioNutriente,
            activo = fuente.activo
        }
    };

    private ObjectResult ErrorInterno(string mensaje, Exception ex) =>
        StatusCode(StatusCodes.Status500InternalServerError, new
        {
            mensaje,
            detalle = ex.Message
        });

    private static string Normalizar(string valor) =>
        valor.ReplaceLineEndings(" ").Trim().ToUpperInvariant();
}
