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

            var unidadResultado = await _db.UnidadMedidas
                .FirstOrDefaultAsync(x => x.nombreUnidadMedida == "lb/Mz" && x.activo);

            if (unidadResultado == null)
                throw new Exception("No existe la unidad de medida lb/Mz configurada.");
      

            foreach (var entrada in dto.elementosQuimicos)
            {
                var elemento = elementos
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                if (elemento == null)
                {
                    response.observaciones.Add($"No se encontró el elemento químico con ID {entrada.elementoQuimicosId}.");
                    continue;
                }

                var errorUnidad = ValidarUnidadElemento(elemento, entrada.unidadMedidaId);

                if (errorUnidad != null)
                {
                    response.observaciones.Add(errorUnidad);
                    continue;
                }

                var parametroExtraccion = parametrosExtraccion
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                var rango = rangosCultivo
                    .FirstOrDefault(x => x.elementoQuimicosId == entrada.elementoQuimicosId);

                decimal? extraccionPorQQOro = parametroExtraccion?.cantidadExtraidaPorQQOro;
                decimal? extraccionPorProduccion = null;
                decimal? requerimientoCalculado = null;
                decimal? cantidadConvertidaLbMz = ConvertirEntradaALbMz(
                    entrada.cantidadElemento,
                    entrada.unidadMedidaId,
                    elemento,
                    dto.materiaOrganica
                );
                decimal? rangoMinimoLbMz = null;
                decimal? rangoMaximoLbMz = null;
                string clasificacion = "SIN_CLASIFICACION";

                if (extraccionPorQQOro.HasValue)
                {
                    extraccionPorProduccion = Math.Round(
                        dto.cantidadQuintalesOro * extraccionPorQQOro.Value,
                        4
                    );
                }

                if (rango != null)
                {
                    rangoMinimoLbMz = ConvertirKgHaALbMz(rango.valorMinimo);
                    rangoMaximoLbMz = ConvertirKgHaALbMz(rango.valorMaximo);
                }

                if (rango != null && extraccionPorProduccion.HasValue)
                {
                    decimal baseNutricionalMz = rangoMaximoLbMz ?? 0;

                    requerimientoCalculado = Math.Round(
                        baseNutricionalMz + extraccionPorProduccion.Value,
                        4
                    );
                }

                clasificacion = ClasificarElemento(
                    cantidadConvertidaLbMz,
                    rangoMinimoLbMz,
                    rangoMaximoLbMz
                );

                var simboloLimpio = elemento.simboloElementoQuimico.Trim();

                response.elementos.Add(new ResultadoElementoCalculoDto
                {
                    elementoQuimicosId = elemento.elementoQuimicosId,
                    simboloElementoQuimico = simboloLimpio,
                    nombreElementoQuimico = elemento.nombreElementoQuimico.Trim(),
                    cantidadIngresada = entrada.cantidadElemento,

                    cantidadConvertidaLbMz = cantidadConvertidaLbMz,

                    extraccionPorQQOro = extraccionPorQQOro,
                    extraccionPorProduccion = extraccionPorProduccion,

                    rangoMinimo = rango?.valorMinimo,
                    rangoMaximo = rango?.valorMaximo,

                    rangoMinimoLbMz = rangoMinimoLbMz,
                    rangoMaximoLbMz = rangoMaximoLbMz,

                    requerimientoCalculado = requerimientoCalculado,
                    unidadBase = rango?.unidadBase,

                    unidadMedidaResultadoId = unidadResultado.unidadMedidaId,
                    unidadResultado = "lb/Mz",

                    clasificacion = clasificacion,

                    observacion = CrearObservacionRequerimientoAnual(
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
                response.observaciones.Add("No se calcularon elementos químicos válidos.");
            }

            response.observaciones.Add(InterpretarPhCafe(dto.ph));

            return response;
        }

        private string InterpretarPhCafe(decimal ph)
        {
            if (ph < 4.5m)
                return "pH muy ácido. El suelo presenta acidez severa; se recomienda evaluar enmienda calcárea.";

            if (ph < 5.5m)
                return "pH ácido. Puede limitar la disponibilidad de nutrientes; se recomienda evaluar enmienda calcárea.";

            if (ph <= 6.5m)
                return "pH adecuado para café. Se encuentra dentro del rango recomendado para el cultivo.";

            if (ph <= 7.3m)
                return "pH cercano a neutro. Revisar la disponibilidad de nutrientes antes de recomendar fertilización.";

            if (ph <= 8.4m)
                return "pH alcalino. Puede afectar la disponibilidad de micronutrientes.";

            return "pH fuertemente alcalino. Se recomienda revisión técnica especializada antes de aplicar fertilización.";
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
            decimal? cantidadConvertidaLbMz,
            decimal? rangoMinimoLbMz,
            decimal? rangoMaximoLbMz,
            decimal? requerimientoCalculado,
            string? clasificacion)
        {
            if (parametroExtraccion == null)
                return $"El elemento {simbolo} no tiene parámetro de extracción por QQ oro configurado.";

            if (rango == null)
                return $"El elemento {simbolo} no tiene rango nutricional configurado para el tipo de cultivo seleccionado.";

            if (!cantidadConvertidaLbMz.HasValue)
                return $"No fue posible convertir el valor ingresado de {simbolo} a lb/Mz.";

            if (!requerimientoCalculado.HasValue)
                return $"No fue posible calcular el requerimiento anual para {simbolo}.";

            return
                $"Elemento {simbolo}: clasificación {clasificacion}. " +
                $"Cantidad convertida: {cantidadConvertidaLbMz.Value:0.####} lb/Mz. " +
                $"Rango de referencia: {rangoMinimoLbMz:0.####} - {rangoMaximoLbMz:0.####} lb/Mz. " +
                $"Requerimiento anual calculado: {requerimientoCalculado.Value:0.####} lb/Mz.";
        }

        private decimal? ConvertirEntradaALbMz(
            decimal cantidad,
            int unidadMedidaId,
            ElementoQuimico elemento,
            decimal materiaOrganica)
        {
            var unidad = _db.UnidadMedidas
                .FirstOrDefault(x => x.unidadMedidaId == unidadMedidaId && x.activo);

            if (unidad == null)
                return null;

            string nombreUnidad = unidad.nombreUnidadMedida.Trim().ToLower();
            string simbolo = elemento.simboloElementoQuimico.Trim().ToUpper();

            // N según Excel:
            // MO * N total = % N en materia orgánica
            // masa suelo = 2,000,000 kg/Ha
            // mineralización = 0.015
            // kg/Ha -> lb/Ha -> lb/Mz
            if (simbolo == "N" && nombreUnidad == "%")
            {
                decimal constanteMineralizacion = 0.015m;

                if (materiaOrganica <= 0)
                    return null;

                if (cantidad <= 0 || cantidad > 100)
                    return null;

                // Ejemplo:
                // materiaOrganica = 2 significa 2,000,000 kg/Ha
                decimal masaSueloKgHa = materiaOrganica * 1000000m;

                decimal fraccionNitrogeno = cantidad / 100m;

                decimal nitrogenoDisponibleKgHa =
                    masaSueloKgHa *
                    fraccionNitrogeno *
                    constanteMineralizacion;

                decimal nitrogenoDisponibleLbMz =
                    nitrogenoDisponibleKgHa * 2.2m * 0.7m;

                return Math.Round(nitrogenoDisponibleLbMz, 4);
            }
            // P: ppm -> kg/Ha -> lb/Ha -> lb/Mz
            if (nombreUnidad == "ppm")
                return Math.Round(cantidad * 2m * 2.2m * 0.7m, 4);

            // K, Ca, Mg: meq/100g -> ppm -> kg/Ha -> lb/Ha -> lb/Mz
            if (nombreUnidad == "meq/100g" || nombreUnidad == "meq")
            {
                decimal pesoEquivalente = elemento.pesoEquivalenteElementoQuimico;

                if (pesoEquivalente <= 0)
                    return null;

                decimal ppm = cantidad * pesoEquivalente * 10m;
                decimal kgHa = ppm * 2m;
                decimal lbHa = kgHa * 2.2m;
                decimal lbMz = lbHa * 0.7m;

                return Math.Round(lbMz, 4);
            }

            // Por si algún laboratorio ya entrega convertido
            if (nombreUnidad == "kg/ha")
                return Math.Round(cantidad * 2.2m * 0.7m, 4);

            if (nombreUnidad == "lb/ha")
                return Math.Round(cantidad * 0.7m, 4);

            if (nombreUnidad == "lb/mz")
                return Math.Round(cantidad, 4);

            return null;
        }

        private string ClasificarElemento(
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

            decimal valor = cantidadConvertidaLbMz.Value;
            decimal minimo = rangoMinimoLbMz.Value;
            decimal maximo = rangoMaximoLbMz.Value;

            decimal limiteMuyBajo = minimo * 0.50m;
            decimal limiteBajo = minimo * 0.75m;
            decimal limiteAlto = maximo * 1.50m;

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

            if (dto.materiaOrganica <= 0)
                throw new Exception("La materia orgánica debe ser mayor a cero y debe ingresarse en porcentaje. Ejemplo: 2 para 2%.");

            if (dto.ph <= 0 || dto.ph > 14)
                throw new Exception("El pH debe estar entre 0 y 14.");

            if (dto.elementosQuimicos == null || !dto.elementosQuimicos.Any())
                throw new Exception("Debe ingresar al menos un elemento químico.");
        }

        private string? ValidarUnidadElemento(
    ElementoQuimico elemento,
    int unidadMedidaId)
        {
            var unidad = _db.UnidadMedidas
                .FirstOrDefault(x => x.unidadMedidaId == unidadMedidaId && x.activo);

            if (unidad == null)
                return "La unidad de medida no existe o está inactiva.";

            string simbolo = elemento.simboloElementoQuimico.Trim().ToUpper();
            string nombreUnidad = unidad.nombreUnidadMedida.Trim().ToLower();

            var unidadesPermitidas = simbolo switch
            {
                "N" => new[] { "%" },

                "P" => new[] { "ppm", "kg/ha", "lb/ha", "lb/mz" },

                "K" => new[] { "meq/100g", "meq", "kg/ha", "lb/ha", "lb/mz" },

                "CA" => new[] { "meq/100g", "meq", "kg/ha", "lb/ha", "lb/mz" },

                "MG" => new[] { "meq/100g", "meq", "kg/ha", "lb/ha", "lb/mz" },

                _ => new[] { "ppm", "kg/ha", "lb/ha", "lb/mz" }
            };

            if (!unidadesPermitidas.Contains(nombreUnidad))
            {
                return $"La unidad '{unidad.nombreUnidadMedida}' no es válida para el elemento {simbolo}. " +
                       $"Unidades permitidas: {string.Join(", ", unidadesPermitidas)}.";
            }

            return null;
        }
    }
}