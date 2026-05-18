using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public class AnalisisSueloCalculoService
    {
        private readonly DBContext _db;

        public AnalisisSueloCalculoService(DBContext db)
        {
            _db = db;
        }

        public async Task<AnalisisSueloCalculoResponseDto> CalcularAsync(AnalisisSueloCalculoRequestDto dto)
        {
            ValidarEntrada(dto);

            var tipoCultivo = await _db.TipoCultivos
                .FirstOrDefaultAsync(x => x.tipoCultivoId == dto.tipoCultivoId && x.activo);

            if (tipoCultivo == null)
                throw new Exception("El tipo de cultivo no existe o está inactivo.");

            var tipoAnalisis = await _db.TipoAnalisisSuelos
                .FirstOrDefaultAsync(x => x.tipoAnalisisSueloId == dto.tipoAnalisisSueloId && x.activo);

            if (tipoAnalisis == null)
                throw new Exception("El tipo de análisis de suelo no existe o está inactivo.");

            string nombreTipoAnalisis = tipoAnalisis.nombreTipoAnalisisSuelo.Trim().ToUpper();

            if (nombreTipoAnalisis == "REQUERIMIENTO_ANUAL")
            {
                return await CalcularRequerimientoAnualAsync(dto, tipoCultivo, tipoAnalisis);
            }

            throw new Exception($"El tipo de análisis {tipoAnalisis.nombreTipoAnalisisSuelo} aún no está implementado.");
        }

        private async Task<AnalisisSueloCalculoResponseDto> CalcularRequerimientoAnualAsync(
    AnalisisSueloCalculoRequestDto dto,
    TipoCultivo tipoCultivo,
    TipoAnalisisSuelo tipoAnalisis)
        {
            var response = new AnalisisSueloCalculoResponseDto
            {
                terrenoId = dto.terrenoId,
                tipoCultivoId = dto.tipoCultivoId,
                tipoCultivo = tipoCultivo.nombreTipoCultivo,
                tipoAnalisisSueloId = dto.tipoAnalisisSueloId,
                tipoAnalisisSuelo = tipoAnalisis.nombreTipoAnalisisSuelo,
                cantidadQuintalesOro = dto.cantidadQuintalesOro,
                tamanoFinca = dto.tamanoFinca,
                ph = dto.ph,
                acidezTotal = dto.acidezTotal,
                recomendacionGeneral = "Cálculo de requerimiento anual generado con base en extracción por QQ oro y rangos nutricionales del cultivo."
            };

            var elementosIds = dto.elementosQuimicos
                .Select(x => x.elementoQuimicosId)
                .Distinct()
                .ToList();

            var elementos = await _db.elementoQuimico
                .Where(x => elementosIds.Contains(x.elementoQuimicosId) && x.activo)
                .ToListAsync();

            var parametrosExtraccion = await _db.ParametroExtraccionNutrienteCafe
                .Where(x => x.activo && elementosIds.Contains(x.elementoQuimicosId))
                .ToListAsync();

            var rangosCultivo = await _db.ParametroRangoNutrienteCultivo
                .Where(x =>
                    x.activo &&
                    x.tipoCultivoId == dto.tipoCultivoId &&
                    elementosIds.Contains(x.elementoQuimicosId))
                .ToListAsync();

            foreach (var entrada in dto.elementosQuimicos)
            {
                var elemento = elementos
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                if (elemento == null)
                {
                    response.observaciones.Add($"No se encontró el elemento químico con ID {entrada.elementoQuimicosId}.");
                    continue;
                }

                var parametroExtraccion = parametrosExtraccion
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                var rango = rangosCultivo
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                decimal? extraccionPorQQOro = parametroExtraccion?.cantidadExtraidaPorQQOro;
                decimal? extraccionPorProduccion = null;
                decimal? requerimientoCalculado = null;

                if (extraccionPorQQOro.HasValue)
                {
                    extraccionPorProduccion = Math.Round(
                        dto.cantidadQuintalesOro * extraccionPorQQOro.Value,
                        4
                    );
                }

                if (rango != null && extraccionPorProduccion.HasValue)
                {
                    decimal baseNutricionalMz = ConvertirKgHaALbMz(rango.valorMaximo);

                    requerimientoCalculado = Math.Round(
                        baseNutricionalMz + extraccionPorProduccion.Value,
                        4
                    );
                }

               var simboloLimpio = elemento.simboloElementoQuimico.Trim();

response.elementos.Add(new ResultadoElementoCalculoDto
{
    elementoQuimicosId = elemento.elementoQuimicosId,
    simboloElementoQuimico = simboloLimpio,
    nombreElementoQuimico = elemento.nombreElementoQuimico.Trim(),
    cantidadIngresada = entrada.cantidadElemento,

    extraccionPorQQOro = extraccionPorQQOro,
    extraccionPorProduccion = extraccionPorProduccion,

    rangoMinimo = rango?.valorMinimo,
    rangoMaximo = rango?.valorMaximo,

    requerimientoCalculado = requerimientoCalculado,
    unidadBase = rango?.unidadBase,

    observacion = CrearObservacionRequerimientoAnual(
        simboloLimpio,
        parametroExtraccion,
        rango,
        requerimientoCalculado
    )
});
            }



            if (!response.elementos.Any())
            {
                response.observaciones.Add("No se calcularon elementos químicos válidos.");
            }

            if (dto.ph < 5.5m)
            {
                response.observaciones.Add("El pH ingresado indica acidez en el suelo. Puede ser necesario evaluar enmienda calcárea.");
            }

            return response;
        }

        private decimal ConvertirKgHaALbMz(decimal valorKgHa)
        {
            decimal factorKgALb = 2.2m;
            decimal factorHaAMz = 0.7m;

            return Math.Round(valorKgHa * factorKgALb * factorHaAMz, 4);
        }

        private string CrearObservacionRequerimientoAnual(
    string simbolo,
    ParametroExtraccionNutrienteCafe? parametroExtraccion,
    ParametroRangoNutrienteCultivo? rango,
    decimal? requerimientoCalculado)
        {
            if (parametroExtraccion == null)
                return $"El elemento {simbolo} no tiene parámetro de extracción por QQ oro configurado.";

            if (rango == null)
                return $"El elemento {simbolo} no tiene rango nutricional configurado para el tipo de cultivo seleccionado.";

            if (!requerimientoCalculado.HasValue)
                return $"No fue posible calcular el requerimiento anual para {simbolo}.";

            return $"Requerimiento anual calculado para {simbolo}.";
        }

        private void ValidarEntrada(AnalisisSueloCalculoRequestDto dto)
        {
            if (dto.terrenoId <= 0)
                throw new Exception("Debe seleccionar un terreno válido.");

            if (dto.tipoCultivoId <= 0)
                throw new Exception("Debe seleccionar un tipo de cultivo válido.");

            if (dto.tipoAnalisisSueloId <= 0)
                throw new Exception("Debe seleccionar un tipo de análisis válido.");

            if (dto.cantidadQuintalesOro <= 0)
                throw new Exception("La cantidad de quintales oro debe ser mayor que cero.");

            if (dto.tamanoFinca <= 0)
                throw new Exception("El tamaño de la finca debe ser mayor que cero.");

            if (dto.ph <= 0 || dto.ph > 14)
                throw new Exception("El pH debe estar entre 0 y 14.");

            if (dto.elementosQuimicos == null || !dto.elementosQuimicos.Any())
                throw new Exception("Debe ingresar al menos un elemento químico.");
        }
    }
}