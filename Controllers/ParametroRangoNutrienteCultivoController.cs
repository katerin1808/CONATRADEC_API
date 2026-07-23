using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.ParametroRangoNutrienteCultivoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/configuracion/rangos-nutrientes")]
    public class ParametroRangoNutrienteCultivoController :
        ControllerBase
    {
        /*
         * La API y la interfaz trabajan en lb/Mz.
         *
         * Internamente se conserva kg/Ha para mantener compatibilidad
         * con la lógica histórica del cálculo, que convierte esos valores
         * a lb/Mz. De esta manera no se alteran los análisis existentes.
         */
        private const string UnidadApi =
            "lb/Mz";

        private const string UnidadInterna =
            "kg/Ha";

        private const decimal FactorKgHaALbMz =
            1.54m;

        private readonly DBContext _db;

        public ParametroRangoNutrienteCultivoController(
            DBContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            List<ParametroRangoNutrienteCultivo>
                entidades =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .AsNoTracking()
                        .Include(x => x.TipoCultivo)
                        .Include(x => x.ElementoQuimico)
                        .Where(x => x.activo)
                        .OrderBy(x =>
                            x.TipoCultivo
                                .nombreTipoCultivo)
                        .ThenBy(x =>
                            x.ElementoQuimico
                                .nombreElementoQuimico)
                        .ToListAsync();

            var data =
                entidades.Select(MapearRespuesta)
                    .ToList();

            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id)
        {
            ParametroRangoNutrienteCultivo?
                entidad =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .AsNoTracking()
                        .Include(x => x.TipoCultivo)
                        .Include(x => x.ElementoQuimico)
                        .FirstOrDefaultAsync(x =>
                            x.parametroRangoNutrienteCultivoId ==
                                id &&
                            x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Rango de aporte no encontrado."
                });
            }

            return Ok(
                MapearRespuesta(entidad));
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody]
            CrearParametroRangoNutrienteCultivoDto
                dto)
        {
            string? error =
                await ValidarDatos(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro);

            if (error != null)
            {
                return BadRequest(new
                {
                    mensaje = error
                });
            }

            ParametroRangoNutrienteCultivo?
                existente =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .FirstOrDefaultAsync(x =>
                            x.tipoCultivoId ==
                                dto.tipoCultivoId &&
                            x.elementoQuimicosId ==
                                dto.elementoQuimicosId);

            if (existente != null &&
                existente.activo)
            {
                return Conflict(new
                {
                    mensaje =
                        "Ya existe un rango activo para este tipo de cultivo y elemento químico."
                });
            }

            decimal minimoInterno =
                ConvertirEntradaAKgHa(
                    dto.valorMinimo,
                    dto.unidadBase);

            decimal maximoInterno =
                ConvertirEntradaAKgHa(
                    dto.valorMaximo,
                    dto.unidadBase);

            if (existente != null &&
                !existente.activo)
            {
                existente.valorMinimo =
                    minimoInterno;

                existente.valorMaximo =
                    maximoInterno;

                existente.unidadBase =
                    UnidadInterna;

                existente.descripcionParametro =
                    dto.descripcionParametro
                        .Trim();

                existente.activo = true;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje =
                        "Rango de aporte reactivado correctamente.",

                    data =
                        await ObtenerDetalle(
                            existente
                                .parametroRangoNutrienteCultivoId)
                });
            }

            var entidad =
                new ParametroRangoNutrienteCultivo
                {
                    tipoCultivoId =
                        dto.tipoCultivoId,

                    elementoQuimicosId =
                        dto.elementoQuimicosId,

                    valorMinimo =
                        minimoInterno,

                    valorMaximo =
                        maximoInterno,

                    unidadBase =
                        UnidadInterna,

                    descripcionParametro =
                        dto.descripcionParametro
                            .Trim(),

                    activo = true
                };

            _db.ParametroRangoNutrienteCultivo
                .Add(entidad);

            await _db.SaveChangesAsync();

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    mensaje =
                        "Rango de aporte creado correctamente.",

                    data =
                        await ObtenerDetalle(
                            entidad
                                .parametroRangoNutrienteCultivoId)
                });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody]
            ActualizarParametroRangoNutrienteCultivoDto
                dto)
        {
            ParametroRangoNutrienteCultivo?
                entidad =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .FirstOrDefaultAsync(x =>
                            x.parametroRangoNutrienteCultivoId ==
                                id &&
                            x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Rango de aporte no encontrado."
                });
            }

            string? error =
                await ValidarDatos(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro);

            if (error != null)
            {
                return BadRequest(new
                {
                    mensaje = error
                });
            }

            bool existeOtro =
                await _db
                    .ParametroRangoNutrienteCultivo
                    .AnyAsync(x =>
                        x.parametroRangoNutrienteCultivoId !=
                            id &&
                        x.tipoCultivoId ==
                            dto.tipoCultivoId &&
                        x.elementoQuimicosId ==
                            dto.elementoQuimicosId &&
                        x.activo);

            if (existeOtro)
            {
                return Conflict(new
                {
                    mensaje =
                        "Ya existe otro rango activo para este tipo de cultivo y elemento químico."
                });
            }

            entidad.tipoCultivoId =
                dto.tipoCultivoId;

            entidad.elementoQuimicosId =
                dto.elementoQuimicosId;

            entidad.valorMinimo =
                ConvertirEntradaAKgHa(
                    dto.valorMinimo,
                    dto.unidadBase);

            entidad.valorMaximo =
                ConvertirEntradaAKgHa(
                    dto.valorMaximo,
                    dto.unidadBase);

            entidad.unidadBase =
                UnidadInterna;

            entidad.descripcionParametro =
                dto.descripcionParametro
                    .Trim();

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    "Rango de aporte actualizado correctamente.",

                data =
                    await ObtenerDetalle(id)
            });
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(
            int id)
        {
            ParametroRangoNutrienteCultivo?
                entidad =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .FirstOrDefaultAsync(x =>
                            x.parametroRangoNutrienteCultivoId ==
                            id);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Rango de aporte no encontrado."
                });
            }

            if (!entidad.activo)
            {
                return BadRequest(new
                {
                    mensaje =
                        "El rango de aporte ya se encuentra eliminado."
                });
            }

            entidad.activo = false;

            _db.Entry(entidad)
                .Property(x => x.activo)
                .IsModified = true;

            int filasActualizadas =
                await _db.SaveChangesAsync();

            await _db.Entry(entidad)
                .ReloadAsync();

            return Ok(new
            {
                mensaje =
                    "Rango de aporte eliminado correctamente.",

                filasActualizadas,

                data = new
                {
                    entidad
                        .parametroRangoNutrienteCultivoId,
                    entidad.tipoCultivoId,
                    entidad.elementoQuimicosId,
                    entidad.activo
                }
            });
        }

        private async Task<string?> ValidarDatos(
            int tipoCultivoId,
            int elementoQuimicosId,
            decimal valorMinimo,
            decimal valorMaximo,
            string? unidadBase,
            string descripcionParametro)
        {
            if (tipoCultivoId <= 0)
            {
                return
                    "Debe seleccionar un tipo de cultivo válido.";
            }

            if (elementoQuimicosId <= 0)
            {
                return
                    "Debe seleccionar un elemento químico válido.";
            }

            if (valorMinimo < 0)
            {
                return
                    "El valor mínimo no puede ser negativo.";
            }

            if (valorMaximo <= valorMinimo)
            {
                return
                    "El valor máximo debe ser mayor que el valor mínimo.";
            }

            if (!EsUnidadSoportada(
                    unidadBase))
            {
                return
                    "La unidad base de los rangos debe ser lb/Mz.";
            }

            if (string.IsNullOrWhiteSpace(
                    descripcionParametro))
            {
                return
                    "La descripción es obligatoria.";
            }

            bool cultivoExiste =
                await _db.TipoCultivos
                    .AnyAsync(x =>
                        x.tipoCultivoId ==
                            tipoCultivoId &&
                        x.activo);

            if (!cultivoExiste)
            {
                return
                    "El tipo de cultivo no existe o está inactivo.";
            }

            bool elementoExiste =
                await _db.elementoQuimico
                    .AnyAsync(x =>
                        x.elementoQuimicosId ==
                            elementoQuimicosId &&
                        x.activo);

            if (!elementoExiste)
            {
                return
                    "El elemento químico no existe o está inactivo.";
            }

            return null;
        }

        private async Task<object?>
            ObtenerDetalle(
                int id)
        {
            ParametroRangoNutrienteCultivo?
                entidad =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .AsNoTracking()
                        .Include(x => x.TipoCultivo)
                        .Include(x => x.ElementoQuimico)
                        .FirstOrDefaultAsync(x =>
                            x.parametroRangoNutrienteCultivoId ==
                            id);

            return entidad == null
                ? null
                : MapearRespuesta(entidad);
        }

        private static object MapearRespuesta(
            ParametroRangoNutrienteCultivo
                entidad)
        {
            return new
            {
                entidad
                    .parametroRangoNutrienteCultivoId,

                entidad.tipoCultivoId,

                nombreTipoCultivo =
                    entidad.TipoCultivo
                        .nombreTipoCultivo,

                entidad.elementoQuimicosId,

                nombreElementoQuimico =
                    entidad.ElementoQuimico
                        .nombreElementoQuimico,

                simboloElementoQuimico =
                    entidad.ElementoQuimico
                        .simboloElementoQuimico,

                valorMinimo =
                    ConvertirAlmacenadoALbMz(
                        entidad.valorMinimo,
                        entidad.unidadBase),

                valorMaximo =
                    ConvertirAlmacenadoALbMz(
                        entidad.valorMaximo,
                        entidad.unidadBase),

                unidadBase =
                    UnidadApi,

                entidad.descripcionParametro,
                entidad.activo
            };
        }

        private static bool EsUnidadSoportada(
            string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            return
                normalizada == "LB/MZ" ||
                normalizada == "KG/HA";
        }

        private static decimal
            ConvertirEntradaAKgHa(
                decimal valor,
                string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            if (normalizada == "KG/HA")
            {
                return Math.Round(
                    valor,
                    4);
            }

            return Math.Round(
                valor /
                FactorKgHaALbMz,
                4);
        }

        private static decimal
            ConvertirAlmacenadoALbMz(
                decimal valor,
                string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            if (normalizada == "LB/MZ")
            {
                return Math.Round(
                    valor,
                    4);
            }

            return Math.Round(
                valor *
                FactorKgHaALbMz,
                4);
        }

        private static string NormalizarUnidad(
            string? unidad)
        {
            return
                (unidad ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .ToUpperInvariant();
        }
    }
}
