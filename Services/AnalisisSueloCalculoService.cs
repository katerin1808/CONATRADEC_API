using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public class AnalisisSueloCalculoService
    {
        private readonly DBContext _db;

        private readonly UnidadConversionService
            unidadConversionService;

        public AnalisisSueloCalculoService(DBContext db)
        {
            _db = db;
            unidadConversionService =
                new UnidadConversionService(db);
        }

        public async Task<AnalisisSueloCalculoResponseDto> CalcularAsync(
            AnalisisSueloCalculoRequestDto dto)
        {
            ValidarEntrada(dto);

            decimal materiaOrganicaPorcentaje =
                await unidadConversionService
                    .ConvertirMateriaOrganicaAPorcentajeAsync(
                        dto.materiaOrganica,
                        dto.unidadMedidaMateriaOrganicaId);

            if (materiaOrganicaPorcentaje <= 0 ||
                materiaOrganicaPorcentaje > 20)
            {
                throw new Exception(
                    "La materia orgánica debe estar entre 0 y 20%.");
            }

            TipoCultivo? tipoCultivo =
                await _db.TipoCultivos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId == dto.tipoCultivoId &&
                        x.activo);

            if (tipoCultivo == null)
            {
                throw new Exception(
                    "El tipo de cultivo no existe o está inactivo.");
            }

            TipoAnalisisSuelo? tipoAnalisis =
                await _db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.tipoAnalisisSueloId ==
                            dto.tipoAnalisisSueloId &&
                        x.activo);

            if (tipoAnalisis == null)
            {
                throw new Exception(
                    "El tipo de análisis de suelo no existe o está inactivo.");
            }

            string nombreTipoAnalisis =
                tipoAnalisis.nombreTipoAnalisisSuelo
                    .Trim()
                    .ToUpperInvariant();

            if (nombreTipoAnalisis == "REQUERIMIENTO_ANUAL")
            {
                return await CalcularRequerimientoAnualAsync(
                    dto,
                    tipoCultivo,
                    tipoAnalisis,
                    materiaOrganicaPorcentaje);
            }

            throw new Exception(
                $"El tipo de análisis " +
                $"{tipoAnalisis.nombreTipoAnalisisSuelo} " +
                "aún no está implementado.");
        }

        private async Task<AnalisisSueloCalculoResponseDto>
            CalcularRequerimientoAnualAsync(
                AnalisisSueloCalculoRequestDto dto,
                TipoCultivo tipoCultivo,
                TipoAnalisisSuelo tipoAnalisis,
                decimal materiaOrganicaPorcentaje)
        {
            AnalisisSueloCalculoResponseDto response =
                new()
                {
                    terrenoId = dto.terrenoId,
                    tipoCultivoId = dto.tipoCultivoId,
                    tipoCultivo =
                        tipoCultivo.nombreTipoCultivo,
                    tipoAnalisisSueloId =
                        dto.tipoAnalisisSueloId,
                    tipoAnalisisSuelo =
                        tipoAnalisis
                            .nombreTipoAnalisisSuelo,
                    cantidadQuintalesOro =
                        dto.cantidadQuintalesOro,
                    tamanoFinca = dto.tamanoFinca,
                    ph = dto.ph,
                    acidezTotal = dto.acidezTotal,
                    materiaOrganica =
                        dto.materiaOrganica,
                    unidadMedidaMateriaOrganicaId =
                        dto
                            .unidadMedidaMateriaOrganicaId,
                    recomendacionGeneral =
                        "Cálculo de requerimiento anual generado " +
                        "con base en extracción por QQ oro y rangos " +
                        "nutricionales del cultivo."
                };

            List<int> elementosIds =
                dto.elementosQuimicos
                    .Select(x =>
                        x.elementoQuimicosId)
                    .Distinct()
                    .ToList();

            List<ElementoQuimico> elementos =
                await _db.elementoQuimico
                    .AsNoTracking()
                    .Where(x =>
                        elementosIds.Contains(
                            x.elementoQuimicosId) &&
                        x.activo)
                    .ToListAsync();

            List<ParametroExtraccionNutrienteCafe>
                parametrosExtraccion =
                    await _db
                        .ParametroExtraccionNutrienteCafe
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            elementosIds.Contains(
                                x.elementoQuimicosId))
                        .ToListAsync();

            List<ParametroRangoNutrienteCultivo>
                rangosCultivo =
                    await _db
                        .ParametroRangoNutrienteCultivo
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            x.tipoCultivoId ==
                                dto.tipoCultivoId &&
                            elementosIds.Contains(
                                x.elementoQuimicosId))
                        .ToListAsync();

            UnidadMedida? unidadResultado =
                await _db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.activo &&
                        x.nombreUnidadMedida
                            .ToLower() == "lb/mz");

            if (unidadResultado == null)
            {
                throw new Exception(
                    "No existe la unidad de medida lb/Mz configurada.");
            }

            UnidadMedida? unidadRangoKgHa =
                await _db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.activo &&
                        x.nombreUnidadMedida
                            .ToLower() == "kg/ha");

            if (unidadRangoKgHa == null)
            {
                throw new Exception(
                    "No existe la unidad interna kg/ha para convertir los rangos nutricionales.");
            }

            foreach (
                AnalisisSueloElementoEntradaDto entrada
                in dto.elementosQuimicos)
            {
                ElementoQuimico? elemento =
                    elementos.FirstOrDefault(x =>
                        x.elementoQuimicosId ==
                            entrada.elementoQuimicosId);

                if (elemento == null)
                {
                    response.observaciones.Add(
                        "No se encontró el elemento químico " +
                        $"con ID {entrada.elementoQuimicosId}.");

                    continue;
                }

                ParametroExtraccionNutrienteCafe?
                    parametroExtraccion =
                        parametrosExtraccion
                            .FirstOrDefault(x =>
                                x.elementoQuimicosId ==
                                    entrada
                                        .elementoQuimicosId);

                ParametroRangoNutrienteCultivo? rango =
                    rangosCultivo
                        .FirstOrDefault(x =>
                            x.elementoQuimicosId ==
                                entrada
                                    .elementoQuimicosId);

                decimal? extraccionPorQQOro =
                    parametroExtraccion?
                        .cantidadExtraidaPorQQOro;

                decimal? extraccionPorProduccion =
                    null;

                decimal? requerimientoCalculado =
                    null;

                ResultadoConversionUnidad conversionEntrada =
                    await unidadConversionService
                        .ConvertirElementoALbMzAsync(
                            elemento.elementoQuimicosId,
                            entrada.unidadMedidaId,
                            entrada.cantidadElemento,
                            materiaOrganicaPorcentaje);

                decimal? cantidadConvertidaLbMz =
                    conversionEntrada.valorConvertido;

                decimal? rangoMinimoLbMz = null;
                decimal? rangoMaximoLbMz = null;

                if (extraccionPorQQOro.HasValue)
                {
                    extraccionPorProduccion =
                        Math.Round(
                            dto.cantidadQuintalesOro *
                            extraccionPorQQOro.Value,
                            4);
                }

                if (rango != null)
                {
                    ResultadoConversionUnidad conversionRangoMinimo =
                        await unidadConversionService
                            .ConvertirElementoALbMzAsync(
                                elemento.elementoQuimicosId,
                                unidadRangoKgHa.unidadMedidaId,
                                rango.valorMinimo,
                                materiaOrganicaPorcentaje);

                    ResultadoConversionUnidad conversionRangoMaximo =
                        await unidadConversionService
                            .ConvertirElementoALbMzAsync(
                                elemento.elementoQuimicosId,
                                unidadRangoKgHa.unidadMedidaId,
                                rango.valorMaximo,
                                materiaOrganicaPorcentaje);

                    rangoMinimoLbMz =
                        conversionRangoMinimo.valorConvertido;

                    rangoMaximoLbMz =
                        conversionRangoMaximo.valorConvertido;
                }

                if (rango != null &&
                    extraccionPorProduccion.HasValue)
                {
                    decimal baseNutricionalMz =
                        rangoMaximoLbMz ?? 0;

                    requerimientoCalculado =
                        Math.Round(
                            baseNutricionalMz +
                            extraccionPorProduccion
                                .Value,
                            4);
                }

                string clasificacion =
                    ClasificarElemento(
                        cantidadConvertidaLbMz,
                        rangoMinimoLbMz,
                        rangoMaximoLbMz);

                string simboloLimpio =
                    elemento
                        .simboloElementoQuimico
                        .Trim();

                bool incluirComplementarios =
                    !string.Equals(
                        clasificacion,
                        "EXCESIVO",
                        StringComparison
                            .OrdinalIgnoreCase);

                response.elementos.Add(
                    new ResultadoElementoCalculoDto
                    {
                        elementoQuimicosId =
                            elemento
                                .elementoQuimicosId,
                        simboloElementoQuimico =
                            simboloLimpio,
                        nombreElementoQuimico =
                            elemento
                                .nombreElementoQuimico
                                .Trim(),
                        cantidadIngresada =
                            entrada.cantidadElemento,
                        cantidadConvertidaLbMz =
                            cantidadConvertidaLbMz,
                        extraccionPorQQOro =
                            extraccionPorQQOro,
                        extraccionPorProduccion =
                            extraccionPorProduccion,
                        rangoMinimo =
                            rango?.valorMinimo,
                        rangoMaximo =
                            rango?.valorMaximo,
                        rangoMinimoLbMz =
                            rangoMinimoLbMz,
                        rangoMaximoLbMz =
                            rangoMaximoLbMz,
                        requerimientoCalculado =
                            requerimientoCalculado,
                        unidadBase =
                            rango?.unidadBase,
                        unidadMedidaResultadoId =
                            unidadResultado
                                .unidadMedidaId,
                        unidadResultado =
                            "lb/Mz",
                        clasificacion =
                            clasificacion,
                        incluirCalculosComplementarios =
                            incluirComplementarios,
                        observacion =
                            CrearObservacionRequerimientoAnual(
                                simboloLimpio,
                                parametroExtraccion,
                                rango,
                                cantidadConvertidaLbMz,
                                rangoMinimoLbMz,
                                rangoMaximoLbMz,
                                requerimientoCalculado,
                                clasificacion)
                    });
            }

            if (!response.elementos.Any())
            {
                response.observaciones.Add(
                    "No se calcularon elementos químicos válidos.");
            }

            if (dto.ph > 0)
            {
                response.observaciones.Add(
                    InterpretarPhCafe(dto.ph));
            }

            return response;
        }

        private static string InterpretarPhCafe(
            decimal ph)
        {
            if (ph < 4.5m)
            {
                return
                    "pH muy ácido. El suelo presenta acidez severa; " +
                    "se recomienda evaluar enmienda calcárea.";
            }

            if (ph < 5.5m)
            {
                return
                    "pH ácido. Puede limitar la disponibilidad de " +
                    "nutrientes; se recomienda evaluar enmienda calcárea.";
            }

            if (ph <= 6.5m)
            {
                return
                    "pH adecuado para café. Se encuentra dentro del " +
                    "rango recomendado para el cultivo.";
            }

            if (ph <= 7.3m)
            {
                return
                    "pH cercano a neutro. Revisar la disponibilidad " +
                    "de nutrientes antes de recomendar fertilización.";
            }

            if (ph <= 8.4m)
            {
                return
                    "pH alcalino. Puede afectar la disponibilidad " +
                    "de micronutrientes.";
            }

            return
                "pH fuertemente alcalino. Se recomienda revisión " +
                "técnica especializada antes de aplicar fertilización.";
        }

        private static string
            CrearObservacionRequerimientoAnual(
                string simbolo,
                ParametroExtraccionNutrienteCafe?
                    parametroExtraccion,
                ParametroRangoNutrienteCultivo? rango,
                decimal? cantidadConvertidaLbMz,
                decimal? rangoMinimoLbMz,
                decimal? rangoMaximoLbMz,
                decimal? requerimientoCalculado,
                string? clasificacion)
        {
            if (parametroExtraccion == null)
            {
                return
                    $"El elemento {simbolo} no tiene parámetro " +
                    "de extracción por QQ oro configurado.";
            }

            if (rango == null)
            {
                return
                    $"El elemento {simbolo} no tiene rango " +
                    "nutricional configurado para el tipo de " +
                    "cultivo seleccionado.";
            }

            if (!cantidadConvertidaLbMz.HasValue)
            {
                return
                    $"No fue posible convertir el elemento " +
                    $"{simbolo} a lb/Mz.";
            }

            if (!requerimientoCalculado.HasValue)
            {
                return
                    "No fue posible calcular el requerimiento " +
                    $"anual para {simbolo}.";
            }

            return
                $"Elemento {simbolo}: clasificación " +
                $"{clasificacion}. " +
                "Cantidad convertida: " +
                $"{cantidadConvertidaLbMz.Value:0.####} lb/Mz. " +
                "Rango de referencia: " +
                $"{rangoMinimoLbMz:0.####} - " +
                $"{rangoMaximoLbMz:0.####} lb/Mz. " +
                "Requerimiento anual calculado: " +
                $"{requerimientoCalculado.Value:0.####} lb/Mz.";
        }

        private static string ClasificarElemento(
            decimal? cantidadConvertidaLbMz,
            decimal? rangoMinimoLbMz,
            decimal? rangoMaximoLbMz)
        {
            if (!cantidadConvertidaLbMz.HasValue ||
                !rangoMinimoLbMz.HasValue ||
                !rangoMaximoLbMz.HasValue ||
                rangoMinimoLbMz.Value <= 0 ||
                rangoMaximoLbMz.Value <= 0)
            {
                return "SIN_CLASIFICACION";
            }

            decimal valor =
                cantidadConvertidaLbMz.Value;

            decimal minimo =
                rangoMinimoLbMz.Value;

            decimal maximo =
                rangoMaximoLbMz.Value;

            decimal limiteMuyBajo =
                minimo *
                0.50m;

            decimal limiteBajo =
                minimo *
                0.75m;

            decimal limiteAlto =
                maximo *
                1.50m;

            if (valor < limiteMuyBajo)
                return "MUY_BAJO";

            if (valor < limiteBajo)
                return "BAJO";

            if (valor < minimo)
                return "MEDIO_BAJO";

            if (valor <= maximo)
                return "ADECUADO";

            if (valor <= limiteAlto)
                return "ALTO";

            return "EXCESIVO";
        }

        private static void ValidarEntrada(
            AnalisisSueloCalculoRequestDto dto)
        {
            if (dto.terrenoId <= 0)
            {
                throw new Exception(
                    "Debe seleccionar un terreno válido.");
            }

            if (dto.tipoCultivoId <= 0)
            {
                throw new Exception(
                    "Debe seleccionar un tipo de cultivo válido.");
            }

            if (dto.tipoAnalisisSueloId <= 0)
            {
                throw new Exception(
                    "Debe seleccionar un tipo de análisis válido.");
            }

            if (dto.cantidadQuintalesOro <= 0)
            {
                throw new Exception(
                    "La cantidad de quintales oro debe ser mayor que cero.");
            }

            if (dto.tamanoFinca <= 0)
            {
                throw new Exception(
                    "El tamaño de la finca debe ser mayor que cero.");
            }

            if (dto.materiaOrganica <= 0)
            {
                throw new Exception(
                    "La materia orgánica debe ser mayor a cero.");
            }

            if (dto.unidadMedidaMateriaOrganicaId <= 0)
            {
                throw new Exception(
                    "Debe seleccionar la unidad de medida de la materia orgánica.");
            }

            if (dto.ph < 0 ||
                dto.ph > 14)
            {
                throw new Exception(
                    "El pH debe estar entre 0 y 14.");
            }

            if (dto.elementosQuimicos == null ||
                !dto.elementosQuimicos.Any())
            {
                throw new Exception(
                    "Debe ingresar al menos un elemento químico.");
            }
        }
    }
}
