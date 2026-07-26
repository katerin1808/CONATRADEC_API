using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Motor parametrizable de unidades y conversiones utilizado por el
    /// análisis de suelo.
    ///
    /// Todas las entradas de elementos químicos se normalizan a lb/Mz.
    /// La materia orgánica se normaliza primero a porcentaje.
    /// </summary>
    public sealed class UnidadConversionService
    {
        public const string FormulaLineal =
            "LINEAL";

        public const string FormulaMeqPesoEquivalente =
            "MEQ_PESO_EQUIVALENTE";

        public const string FormulaNitrogenoMateriaOrganicaLegado =
            "NITROGENO_MO_LEGADO";

        public const string FormulaNitrogenoMateriaOrganicaEstandar =
            "NITROGENO_MO_ESTANDAR";

        private readonly DBContext db;

        public UnidadConversionService(
            DBContext db)
        {
            this.db = db;
        }

        public async Task<decimal>
            ConvertirMateriaOrganicaAPorcentajeAsync(
                decimal valor,
                int unidadMedidaId,
                CancellationToken cancellationToken =
                    default)
        {
            if (valor <= 0)
            {
                throw new InvalidOperationException(
                    "La materia orgánica debe ser mayor que cero.");
            }

            MateriaOrganicaUnidadMedida? configuracion =
                await db
                    .Set<MateriaOrganicaUnidadMedida>()
                    .AsNoTracking()
                    .Include(x => x.UnidadMedida)
                    .FirstOrDefaultAsync(
                        x =>
                            x.unidadMedidaId ==
                                unidadMedidaId &&
                            x.activo &&
                            x.UnidadMedida.activo,
                        cancellationToken);

            if (configuracion == null)
            {
                throw new InvalidOperationException(
                    "La unidad seleccionada no está configurada para materia orgánica.");
            }

            decimal convertido =
                AplicarFormulaLineal(
                    valor,
                    configuracion.factorPrincipal,
                    configuracion.factorSecundario,
                    configuracion.factorTerciario,
                    configuracion.divisor,
                    configuracion.desplazamiento);

            if (convertido <= 0 ||
                convertido > 20)
            {
                throw new InvalidOperationException(
                    "La materia orgánica convertida debe estar entre 0 y 20%.");
            }

            return Math.Round(
                convertido,
                4);
        }

        public async Task<ResultadoConversionUnidad>
            ConvertirElementoALbMzAsync(
                int elementoQuimicosId,
                int unidadMedidaId,
                decimal valor,
                decimal materiaOrganicaPorcentaje,
                CancellationToken cancellationToken =
                    default)
        {
            if (elementoQuimicosId <= 0)
            {
                throw new InvalidOperationException(
                    "El elemento químico no es válido.");
            }

            if (unidadMedidaId <= 0)
            {
                throw new InvalidOperationException(
                    "La unidad de medida no es válida.");
            }

            if (valor < 0)
            {
                throw new InvalidOperationException(
                    "El valor reportado no puede ser negativo.");
            }

            ElementoQuimicoUnidadMedida? configuracion =
                await db
                    .Set<ElementoQuimicoUnidadMedida>()
                    .AsNoTracking()
                    .Include(x => x.ElementoQuimico)
                    .Include(x => x.UnidadMedida)
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId ==
                                elementoQuimicosId &&
                            x.unidadMedidaId ==
                                unidadMedidaId &&
                            x.activo &&
                            x.ElementoQuimico.activo &&
                            x.UnidadMedida.activo,
                        cancellationToken);

            if (configuracion == null)
            {
                ElementoQuimico? elemento =
                    await db.elementoQuimico
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.elementoQuimicosId ==
                                    elementoQuimicosId,
                            cancellationToken);

                string nombreElemento =
                    elemento == null
                        ? $"ID {elementoQuimicosId}"
                        : elemento
                            .simboloElementoQuimico
                            .Trim();

                throw new InvalidOperationException(
                    $"La unidad seleccionada no está configurada para el elemento {nombreElemento}.");
            }

            decimal resultado =
                AplicarFormulaElemento(
                    configuracion,
                    valor,
                    materiaOrganicaPorcentaje);

            UnidadMedida unidadDestino =
                await ObtenerUnidadResultadoAsync(
                    cancellationToken);

            return new ResultadoConversionUnidad
            {
                elementoQuimicosId =
                    configuracion.elementoQuimicosId,
                elemento =
                    $"{configuracion.ElementoQuimico.nombreElementoQuimico.Trim()} " +
                    $"({configuracion.ElementoQuimico.simboloElementoQuimico.Trim()})",
                unidadOrigenId =
                    configuracion.unidadMedidaId,
                unidadOrigen =
                    configuracion.UnidadMedida
                        .nombreUnidadMedida
                        .Trim(),
                unidadDestinoId =
                    unidadDestino.unidadMedidaId,
                unidadDestino =
                    unidadDestino.nombreUnidadMedida.Trim(),
                valorReportado =
                    valor,
                valorConvertido =
                    Math.Round(resultado, 4),
                codigoFormulaConversion =
                    NormalizarCodigoFormula(
                        configuracion
                            .codigoFormulaConversion),
                descripcion =
                    CrearDescripcionFormula(
                        configuracion
                            .codigoFormulaConversion)
            };
        }

        public async Task<ConfiguracionFormularioAnalisisDto>
            ObtenerConfiguracionFormularioAsync(
                CancellationToken cancellationToken =
                    default)
        {
            UnidadMedida unidadResultado =
                await ObtenerUnidadResultadoAsync(
                    cancellationToken);

            List<MateriaOrganicaUnidadMedida>
                configuracionesMateria =
                    await db
                        .Set<MateriaOrganicaUnidadMedida>()
                        .AsNoTracking()
                        .Include(x => x.UnidadMedida)
                        .Where(x =>
                            x.activo &&
                            x.visibleEnFormulario &&
                            x.UnidadMedida.activo)
                        .OrderBy(x => x.orden)
                        .ThenBy(x =>
                            x.UnidadMedida
                                .nombreUnidadMedida)
                        .ToListAsync(
                            cancellationToken);

            List<ElementoQuimico> elementos =
                await db.elementoQuimico
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x =>
                        x.nombreElementoQuimico)
                    .ToListAsync(
                        cancellationToken);

            List<int> elementosIds =
                elementos
                    .Select(x =>
                        x.elementoQuimicosId)
                    .ToList();

            List<ElementoQuimicoUnidadMedida>
                configuracionesElementos =
                    await db
                        .Set<ElementoQuimicoUnidadMedida>()
                        .AsNoTracking()
                        .Include(x => x.UnidadMedida)
                        .Where(x =>
                            elementosIds.Contains(
                                x.elementoQuimicosId) &&
                            x.activo &&
                            x.visibleEnFormulario &&
                            x.UnidadMedida.activo)
                        .OrderBy(x => x.orden)
                        .ThenBy(x =>
                            x.UnidadMedida
                                .nombreUnidadMedida)
                        .ToListAsync(
                            cancellationToken);

            return new ConfiguracionFormularioAnalisisDto
            {
                unidadResultadoId =
                    unidadResultado.unidadMedidaId,
                unidadResultado =
                    unidadResultado.nombreUnidadMedida.Trim(),
                unidadesMateriaOrganica =
                    configuracionesMateria
                        .Select(MapearMateriaOrganica)
                        .ToList(),
                elementos =
                    elementos
                        .Select(elemento =>
                        {
                            List<ElementoQuimicoUnidadMedida>
                                unidades =
                                    configuracionesElementos
                                        .Where(x =>
                                            x.elementoQuimicosId ==
                                                elemento
                                                    .elementoQuimicosId)
                                        .ToList();

                            return new
                                ElementoConfiguracionUnidadesDto
                                {
                                    elementoQuimicosId =
                                        elemento
                                            .elementoQuimicosId,
                                    simboloElementoQuimico =
                                        elemento
                                            .simboloElementoQuimico
                                            .Trim(),
                                    nombreElementoQuimico =
                                        elemento
                                            .nombreElementoQuimico
                                            .Trim(),
                                    pesoEquivalenteElementoQuimico =
                                        elemento
                                            .pesoEquivalenteElementoQuimico,
                                    unidadPredeterminadaId =
                                        unidades
                                            .FirstOrDefault(x =>
                                                x
                                                    .unidadPredeterminada)?.unidadMedidaId,
                                    unidades =
                                        unidades
                                            .Select(
                                                MapearElemento)
                                            .ToList()
                                };
                        })
                        .ToList()
            };
        }

        public async Task<ElementoConfiguracionUnidadesDto?>
            ObtenerConfiguracionElementoAsync(
                int elementoQuimicosId,
                bool incluirInactivas,
                CancellationToken cancellationToken =
                    default)
        {
            ElementoQuimico? elemento =
                await db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId ==
                                elementoQuimicosId,
                        cancellationToken);

            if (elemento == null)
                return null;

            IQueryable<ElementoQuimicoUnidadMedida>
                query =
                    db
                        .Set<ElementoQuimicoUnidadMedida>()
                        .AsNoTracking()
                        .Include(x => x.UnidadMedida)
                        .Where(x =>
                            x.elementoQuimicosId ==
                                elementoQuimicosId);

            if (!incluirInactivas)
            {
                query = query.Where(x =>
                    x.activo);
            }

            List<ElementoQuimicoUnidadMedida>
                configuraciones =
                    await query
                        .OrderBy(x => x.orden)
                        .ThenBy(x =>
                            x.UnidadMedida
                                .nombreUnidadMedida)
                        .ToListAsync(
                            cancellationToken);

            return new ElementoConfiguracionUnidadesDto
            {
                elementoQuimicosId =
                    elemento.elementoQuimicosId,
                simboloElementoQuimico =
                    elemento
                        .simboloElementoQuimico
                        .Trim(),
                nombreElementoQuimico =
                    elemento
                        .nombreElementoQuimico
                        .Trim(),
                pesoEquivalenteElementoQuimico =
                    elemento
                        .pesoEquivalenteElementoQuimico,
                unidadPredeterminadaId =
                    configuraciones
                        .FirstOrDefault(x =>
                            x.activo &&
                            x.unidadPredeterminada)?.unidadMedidaId,
                unidades =
                    configuraciones
                        .Select(MapearElemento)
                        .ToList()
            };
        }

        public async Task<List<UnidadConversionConfiguradaDto>>
            ObtenerConfiguracionMateriaOrganicaAsync(
                bool incluirInactivas,
                CancellationToken cancellationToken =
                    default)
        {
            IQueryable<MateriaOrganicaUnidadMedida>
                query =
                    db
                        .Set<MateriaOrganicaUnidadMedida>()
                        .AsNoTracking()
                        .Include(x => x.UnidadMedida);

            if (!incluirInactivas)
            {
                query = query.Where(x =>
                    x.activo);
            }

            return await query
                .OrderBy(x => x.orden)
                .ThenBy(x =>
                    x.UnidadMedida
                        .nombreUnidadMedida)
                .Select(x =>
                    new UnidadConversionConfiguradaDto
                    {
                        configuracionId =
                            x
                                .materiaOrganicaUnidadMedidaId,
                        unidadMedidaId =
                            x.unidadMedidaId,
                        nombreUnidadMedida =
                            x.UnidadMedida
                                .nombreUnidadMedida,
                        codigoFormulaConversion =
                            x.codigoFormulaConversion,
                        factorPrincipal =
                            x.factorPrincipal,
                        factorSecundario =
                            x.factorSecundario,
                        factorTerciario =
                            x.factorTerciario,
                        divisor =
                            x.divisor,
                        desplazamiento =
                            x.desplazamiento,
                        unidadPredeterminada =
                            x.unidadPredeterminada,
                        visibleEnFormulario =
                            x.visibleEnFormulario,
                        orden =
                            x.orden,
                        observacion =
                            x.observacion ??
                            string.Empty,
                        activo =
                            x.activo
                    })
                .ToListAsync(
                    cancellationToken);
        }

        public async Task GuardarConfiguracionElementoAsync(
            int elementoQuimicosId,
            GuardarConfiguracionElementoUnidadesDto dto,
            CancellationToken cancellationToken =
                default)
        {
            bool elementoExiste =
                await db.elementoQuimico
                    .AnyAsync(
                        x =>
                            x.elementoQuimicosId ==
                                elementoQuimicosId,
                        cancellationToken);

            if (!elementoExiste)
            {
                throw new InvalidOperationException(
                    "El elemento químico indicado no existe.");
            }

            ValidarListaConfiguraciones(
                dto.unidades);

            await ValidarUnidadesExistentesAsync(
                dto.unidades,
                cancellationToken);

            await ValidarConversionInternaKgHaAsync(
                dto.unidades,
                cancellationToken);

            List<ElementoQuimicoUnidadMedida>
                existentes =
                    await db
                        .Set<ElementoQuimicoUnidadMedida>()
                        .Where(x =>
                            x.elementoQuimicosId ==
                                elementoQuimicosId)
                        .ToListAsync(
                            cancellationToken);

            HashSet<int> unidadesRecibidas =
                dto.unidades
                    .Select(x =>
                        x.unidadMedidaId)
                    .ToHashSet();

            foreach (
                ElementoQuimicoUnidadMedida existente
                in existentes)
            {
                if (!unidadesRecibidas.Contains(
                        existente.unidadMedidaId))
                {
                    existente.activo = false;
                    existente.unidadPredeterminada =
                        false;
                }
            }

            foreach (
                GuardarUnidadConversionDto item
                in dto.unidades)
            {
                ElementoQuimicoUnidadMedida?
                    configuracion =
                        existentes.FirstOrDefault(x =>
                            x.unidadMedidaId ==
                                item.unidadMedidaId);

                if (configuracion == null)
                {
                    configuracion =
                        new
                            ElementoQuimicoUnidadMedida
                            {
                                elementoQuimicosId =
                                    elementoQuimicosId,
                                unidadMedidaId =
                                    item.unidadMedidaId
                            };

                    db.Set<ElementoQuimicoUnidadMedida>()
                        .Add(configuracion);
                }

                AplicarDatos(
                    configuracion,
                    item);
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }

        public async Task
            GuardarConfiguracionMateriaOrganicaAsync(
                GuardarConfiguracionMateriaOrganicaDto dto,
                CancellationToken cancellationToken =
                    default)
        {
            ValidarListaConfiguraciones(
                dto.unidades);

            await ValidarUnidadesExistentesAsync(
                dto.unidades,
                cancellationToken);

            List<MateriaOrganicaUnidadMedida>
                existentes =
                    await db
                        .Set<MateriaOrganicaUnidadMedida>()
                        .ToListAsync(
                            cancellationToken);

            HashSet<int> unidadesRecibidas =
                dto.unidades
                    .Select(x =>
                        x.unidadMedidaId)
                    .ToHashSet();

            foreach (
                MateriaOrganicaUnidadMedida existente
                in existentes)
            {
                if (!unidadesRecibidas.Contains(
                        existente.unidadMedidaId))
                {
                    existente.activo = false;
                    existente.unidadPredeterminada =
                        false;
                }
            }

            foreach (
                GuardarUnidadConversionDto item
                in dto.unidades)
            {
                MateriaOrganicaUnidadMedida?
                    configuracion =
                        existentes.FirstOrDefault(x =>
                            x.unidadMedidaId ==
                                item.unidadMedidaId);

                if (configuracion == null)
                {
                    configuracion =
                        new MateriaOrganicaUnidadMedida
                        {
                            unidadMedidaId =
                                item.unidadMedidaId
                        };

                    db.Set<MateriaOrganicaUnidadMedida>()
                        .Add(configuracion);
                }

                AplicarDatos(
                    configuracion,
                    item);
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }

        public async Task<ResultadoPruebaConversionDto>
            ProbarConversionAsync(
                ProbarConversionUnidadDto dto,
                CancellationToken cancellationToken =
                    default)
        {
            string contexto =
                (dto.contexto ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            if (contexto == "MATERIA_ORGANICA")
            {
                decimal convertido =
                    await ConvertirMateriaOrganicaAPorcentajeAsync(
                        dto.valorReportado,
                        dto.unidadMedidaId,
                        cancellationToken);

                UnidadMedida unidadOrigen =
                    await ObtenerUnidadAsync(
                        dto.unidadMedidaId,
                        cancellationToken);

                UnidadMedida? unidadPorcentaje =
                    await db.UnidadMedidas
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.activo &&
                                x.nombreUnidadMedida
                                    .Trim() == "%",
                            cancellationToken);

                return new ResultadoPruebaConversionDto
                {
                    contexto =
                        contexto,
                    unidadOrigenId =
                        unidadOrigen.unidadMedidaId,
                    unidadOrigen =
                        unidadOrigen
                            .nombreUnidadMedida
                            .Trim(),
                    unidadDestinoId =
                        unidadPorcentaje?.unidadMedidaId ??
                        0,
                    unidadDestino =
                        unidadPorcentaje?.nombreUnidadMedida
                            .Trim() ??
                        "%",
                    valorReportado =
                        dto.valorReportado,
                    valorConvertido =
                        convertido,
                    codigoFormulaConversion =
                        FormulaLineal,
                    descripcion =
                        "Materia orgánica normalizada a porcentaje."
                };
            }

            if (contexto != "ELEMENTO")
            {
                throw new InvalidOperationException(
                    "El contexto debe ser ELEMENTO o MATERIA_ORGANICA.");
            }

            if (!dto.elementoQuimicosId.HasValue ||
                dto.elementoQuimicosId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "Debe seleccionar un elemento químico.");
            }

            ResultadoConversionUnidad resultado =
                await ConvertirElementoALbMzAsync(
                    dto.elementoQuimicosId.Value,
                    dto.unidadMedidaId,
                    dto.valorReportado,
                    dto.materiaOrganicaPorcentaje ??
                        0,
                    cancellationToken);

            return new ResultadoPruebaConversionDto
            {
                contexto =
                    contexto,
                elementoQuimicosId =
                    resultado.elementoQuimicosId,
                elemento =
                    resultado.elemento,
                unidadOrigenId =
                    resultado.unidadOrigenId,
                unidadOrigen =
                    resultado.unidadOrigen,
                unidadDestinoId =
                    resultado.unidadDestinoId,
                unidadDestino =
                    resultado.unidadDestino,
                valorReportado =
                    resultado.valorReportado,
                valorConvertido =
                    resultado.valorConvertido,
                codigoFormulaConversion =
                    resultado
                        .codigoFormulaConversion,
                descripcion =
                    resultado.descripcion
            };
        }

        public static List<FormulaConversionDisponibleDto>
            ObtenerFormulasDisponibles()
        {
            return new()
            {
                new FormulaConversionDisponibleDto
                {
                    codigo =
                        FormulaLineal,
                    nombre =
                        "Conversión lineal",
                    descripcion =
                        "Aplica valor × factor principal × factor secundario × factor terciario ÷ divisor + desplazamiento.",
                    requiereElementoQuimico =
                        false,
                    requiereMateriaOrganica =
                        false
                },
                new FormulaConversionDisponibleDto
                {
                    codigo =
                        FormulaMeqPesoEquivalente,
                    nombre =
                        "meq/100g con peso equivalente",
                    descripcion =
                        "Multiplica el valor por el peso equivalente del elemento y los factores configurados.",
                    requiereElementoQuimico =
                        true,
                    requiereMateriaOrganica =
                        false
                },
                new FormulaConversionDisponibleDto
                {
                    codigo =
                        FormulaNitrogenoMateriaOrganicaLegado,
                    nombre =
                        "Nitrógeno con materia orgánica — fórmula actual",
                    descripcion =
                        "Conserva la fórmula histórica utilizada por CONATRADEC para no alterar resultados existentes.",
                    requiereElementoQuimico =
                        true,
                    requiereMateriaOrganica =
                        true
                },
                new FormulaConversionDisponibleDto
                {
                    codigo =
                        FormulaNitrogenoMateriaOrganicaEstandar,
                    nombre =
                        "Nitrógeno con materia orgánica — fórmula estándar",
                    descripcion =
                        "Permite configurar masa de suelo, mineralización y conversión final a lb/Mz.",
                    requiereElementoQuimico =
                        true,
                    requiereMateriaOrganica =
                        true
                }
            };
        }

        private async Task<UnidadMedida>
            ObtenerUnidadResultadoAsync(
                CancellationToken cancellationToken)
        {
            UnidadMedida? unidad =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.activo &&
                            x.nombreUnidadMedida
                                .Trim()
                                .ToLower() ==
                                "lb/mz",
                        cancellationToken);

            if (unidad == null)
            {
                throw new InvalidOperationException(
                    "No existe la unidad de resultado lb/Mz.");
            }

            return unidad;
        }

        private async Task<UnidadMedida>
            ObtenerUnidadAsync(
                int unidadMedidaId,
                CancellationToken cancellationToken)
        {
            UnidadMedida? unidad =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.unidadMedidaId ==
                                unidadMedidaId,
                        cancellationToken);

            if (unidad == null)
            {
                throw new InvalidOperationException(
                    "La unidad de medida no existe.");
            }

            return unidad;
        }

        private decimal AplicarFormulaElemento(
            ElementoQuimicoUnidadMedida configuracion,
            decimal valor,
            decimal materiaOrganicaPorcentaje)
        {
            decimal divisor =
                ValidarDivisor(
                    configuracion.divisor);

            string codigo =
                NormalizarCodigoFormula(
                    configuracion
                        .codigoFormulaConversion);

            decimal resultado =
                codigo switch
                {
                    FormulaLineal =>
                        AplicarFormulaLineal(
                            valor,
                            configuracion
                                .factorPrincipal,
                            configuracion
                                .factorSecundario,
                            configuracion
                                .factorTerciario,
                            divisor,
                            configuracion
                                .desplazamiento),

                    FormulaMeqPesoEquivalente =>
                        AplicarFormulaMeq(
                            valor,
                            configuracion
                                .ElementoQuimico
                                .pesoEquivalenteElementoQuimico,
                            configuracion
                                .factorPrincipal,
                            configuracion
                                .factorSecundario,
                            configuracion
                                .factorTerciario,
                            divisor,
                            configuracion
                                .desplazamiento),

                    FormulaNitrogenoMateriaOrganicaLegado =>
                        AplicarFormulaNitrogenoLegado(
                            valor,
                            materiaOrganicaPorcentaje,
                            configuracion
                                .factorPrincipal,
                            configuracion
                                .factorSecundario,
                            configuracion
                                .factorTerciario,
                            divisor,
                            configuracion
                                .desplazamiento),

                    FormulaNitrogenoMateriaOrganicaEstandar =>
                        AplicarFormulaNitrogenoEstandar(
                            valor,
                            materiaOrganicaPorcentaje,
                            configuracion
                                .factorPrincipal,
                            configuracion
                                .factorSecundario,
                            configuracion
                                .factorTerciario,
                            divisor,
                            configuracion
                                .desplazamiento),

                    _ => throw new InvalidOperationException(
                        $"La fórmula '{configuracion.codigoFormulaConversion}' no está soportada.")
                };

            if (resultado < 0)
            {
                throw new InvalidOperationException(
                    "La conversión produjo un valor negativo. Revise los factores configurados.");
            }

            return resultado;
        }

        private static decimal AplicarFormulaLineal(
            decimal valor,
            decimal factorPrincipal,
            decimal factorSecundario,
            decimal factorTerciario,
            decimal divisor,
            decimal desplazamiento)
        {
            divisor =
                ValidarDivisor(divisor);

            return
                (
                    valor *
                    factorPrincipal *
                    factorSecundario *
                    factorTerciario
                ) /
                divisor +
                desplazamiento;
        }

        private static decimal AplicarFormulaMeq(
            decimal valor,
            decimal pesoEquivalente,
            decimal factorPrincipal,
            decimal factorSecundario,
            decimal factorTerciario,
            decimal divisor,
            decimal desplazamiento)
        {
            if (pesoEquivalente <= 0)
            {
                throw new InvalidOperationException(
                    "El elemento no tiene un peso equivalente válido.");
            }

            return
                (
                    valor *
                    pesoEquivalente *
                    factorPrincipal *
                    factorSecundario *
                    factorTerciario
                ) /
                ValidarDivisor(divisor) +
                desplazamiento;
        }

        private static decimal
            AplicarFormulaNitrogenoLegado(
                decimal nitrogenoPorcentaje,
                decimal materiaOrganicaPorcentaje,
                decimal factorPrincipal,
                decimal factorSecundario,
                decimal factorTerciario,
                decimal divisor,
                decimal desplazamiento)
        {
            ValidarPorcentajesNitrogeno(
                nitrogenoPorcentaje,
                materiaOrganicaPorcentaje);

            return
                (
                    nitrogenoPorcentaje *
                    materiaOrganicaPorcentaje *
                    materiaOrganicaPorcentaje *
                    factorPrincipal *
                    factorSecundario *
                    factorTerciario
                ) /
                ValidarDivisor(divisor) +
                desplazamiento;
        }

        private static decimal
            AplicarFormulaNitrogenoEstandar(
                decimal nitrogenoPorcentaje,
                decimal materiaOrganicaPorcentaje,
                decimal masaSueloKgHa,
                decimal factorMineralizacion,
                decimal factorKgHaALbMz,
                decimal divisorPorcentajes,
                decimal desplazamiento)
        {
            ValidarPorcentajesNitrogeno(
                nitrogenoPorcentaje,
                materiaOrganicaPorcentaje);

            return
                (
                    nitrogenoPorcentaje *
                    materiaOrganicaPorcentaje *
                    masaSueloKgHa *
                    factorMineralizacion *
                    factorKgHaALbMz
                ) /
                ValidarDivisor(
                    divisorPorcentajes) +
                desplazamiento;
        }

        private static void ValidarPorcentajesNitrogeno(
            decimal nitrogenoPorcentaje,
            decimal materiaOrganicaPorcentaje)
        {
            if (nitrogenoPorcentaje < 0 ||
                nitrogenoPorcentaje > 100)
            {
                throw new InvalidOperationException(
                    "El nitrógeno en porcentaje debe estar entre 0 y 100.");
            }

            if (materiaOrganicaPorcentaje <= 0 ||
                materiaOrganicaPorcentaje > 20)
            {
                throw new InvalidOperationException(
                    "La materia orgánica debe estar entre 0 y 20% para calcular nitrógeno.");
            }
        }

        private static decimal ValidarDivisor(
            decimal divisor)
        {
            if (divisor == 0)
            {
                throw new InvalidOperationException(
                    "El divisor de la conversión no puede ser cero.");
            }

            return divisor;
        }

        private static string NormalizarCodigoFormula(
            string? codigo)
        {
            return (
                codigo ??
                string.Empty)
                .Trim()
                .ToUpperInvariant();
        }

        private static string CrearDescripcionFormula(
            string? codigo)
        {
            string normalizado =
                NormalizarCodigoFormula(codigo);

            return ObtenerFormulasDisponibles()
                .FirstOrDefault(x =>
                    x.codigo == normalizado)?.descripcion ??
                normalizado;
        }

        private static UnidadConversionConfiguradaDto
            MapearElemento(
                ElementoQuimicoUnidadMedida x)
        {
            return new UnidadConversionConfiguradaDto
            {
                configuracionId =
                    x.elementoQuimicoUnidadMedidaId,
                unidadMedidaId =
                    x.unidadMedidaId,
                nombreUnidadMedida =
                    x.UnidadMedida
                        .nombreUnidadMedida
                        .Trim(),
                codigoFormulaConversion =
                    x.codigoFormulaConversion,
                factorPrincipal =
                    x.factorPrincipal,
                factorSecundario =
                    x.factorSecundario,
                factorTerciario =
                    x.factorTerciario,
                divisor =
                    x.divisor,
                desplazamiento =
                    x.desplazamiento,
                unidadPredeterminada =
                    x.unidadPredeterminada,
                visibleEnFormulario =
                    x.visibleEnFormulario,
                orden =
                    x.orden,
                observacion =
                    x.observacion ??
                    string.Empty,
                activo =
                    x.activo
            };
        }

        private static UnidadConversionConfiguradaDto
            MapearMateriaOrganica(
                MateriaOrganicaUnidadMedida x)
        {
            return new UnidadConversionConfiguradaDto
            {
                configuracionId =
                    x.materiaOrganicaUnidadMedidaId,
                unidadMedidaId =
                    x.unidadMedidaId,
                nombreUnidadMedida =
                    x.UnidadMedida
                        .nombreUnidadMedida
                        .Trim(),
                codigoFormulaConversion =
                    x.codigoFormulaConversion,
                factorPrincipal =
                    x.factorPrincipal,
                factorSecundario =
                    x.factorSecundario,
                factorTerciario =
                    x.factorTerciario,
                divisor =
                    x.divisor,
                desplazamiento =
                    x.desplazamiento,
                unidadPredeterminada =
                    x.unidadPredeterminada,
                visibleEnFormulario =
                    x.visibleEnFormulario,
                orden =
                    x.orden,
                observacion =
                    x.observacion ??
                    string.Empty,
                activo =
                    x.activo
            };
        }

        private static void AplicarDatos(
            ElementoQuimicoUnidadMedida destino,
            GuardarUnidadConversionDto origen)
        {
            destino.codigoFormulaConversion =
                ValidarFormula(
                    origen
                        .codigoFormulaConversion);

            destino.factorPrincipal =
                origen.factorPrincipal;
            destino.factorSecundario =
                origen.factorSecundario;
            destino.factorTerciario =
                origen.factorTerciario;
            destino.divisor =
                ValidarDivisor(origen.divisor);
            destino.desplazamiento =
                origen.desplazamiento;
            destino.unidadPredeterminada =
                origen.activo &&
                origen.visibleEnFormulario &&
                origen.unidadPredeterminada;
            destino.visibleEnFormulario =
                origen.visibleEnFormulario;
            destino.orden =
                origen.orden;
            destino.observacion =
                origen.observacion?.Trim();
            destino.activo =
                origen.activo;
        }

        private static void AplicarDatos(
            MateriaOrganicaUnidadMedida destino,
            GuardarUnidadConversionDto origen)
        {
            string formula =
                ValidarFormula(
                    origen
                        .codigoFormulaConversion);

            if (formula != FormulaLineal)
            {
                throw new InvalidOperationException(
                    "La materia orgánica utiliza únicamente la fórmula LINEAL.");
            }

            destino.codigoFormulaConversion =
                formula;
            destino.factorPrincipal =
                origen.factorPrincipal;
            destino.factorSecundario =
                origen.factorSecundario;
            destino.factorTerciario =
                origen.factorTerciario;
            destino.divisor =
                ValidarDivisor(origen.divisor);
            destino.desplazamiento =
                origen.desplazamiento;
            destino.unidadPredeterminada =
                origen.activo &&
                origen.visibleEnFormulario &&
                origen.unidadPredeterminada;
            destino.visibleEnFormulario =
                origen.visibleEnFormulario;
            destino.orden =
                origen.orden;
            destino.observacion =
                origen.observacion?.Trim();
            destino.activo =
                origen.activo;
        }

        private static string ValidarFormula(
            string? codigo)
        {
            string normalizado =
                NormalizarCodigoFormula(codigo);

            bool existe =
                ObtenerFormulasDisponibles()
                    .Any(x =>
                        x.codigo ==
                            normalizado);

            if (!existe)
            {
                throw new InvalidOperationException(
                    $"La fórmula '{codigo}' no está soportada.");
            }

            return normalizado;
        }

        private static void ValidarListaConfiguraciones(
            List<GuardarUnidadConversionDto>? unidades)
        {
            if (unidades == null ||
                unidades.Count == 0)
            {
                throw new InvalidOperationException(
                    "Debe configurar al menos una unidad.");
            }

            bool hayDuplicadas =
                unidades
                    .GroupBy(x =>
                        x.unidadMedidaId)
                    .Any(grupo =>
                        grupo.Count() > 1);

            if (hayDuplicadas)
            {
                throw new InvalidOperationException(
                    "No puede repetir una unidad dentro de la misma configuración.");
            }

            foreach (
                GuardarUnidadConversionDto unidad
                in unidades)
            {
                ValidarFormula(
                    unidad.codigoFormulaConversion);

                ValidarDivisor(
                    unidad.divisor);
            }

            int predeterminadas =
                unidades.Count(x =>
                    x.activo &&
                    x.visibleEnFormulario &&
                    x.unidadPredeterminada);

            if (predeterminadas > 1)
            {
                throw new InvalidOperationException(
                    "Solo puede seleccionar una unidad predeterminada entre las unidades activas y visibles.");
            }
        }

        private async Task ValidarUnidadesExistentesAsync(
            List<GuardarUnidadConversionDto> unidades,
            CancellationToken cancellationToken)
        {
            List<int> ids =
                unidades
                    .Select(x =>
                        x.unidadMedidaId)
                    .Distinct()
                    .ToList();

            List<UnidadMedida> existentes =
                await db.UnidadMedidas
                    .Where(x =>
                        ids.Contains(
                            x.unidadMedidaId))
                    .ToListAsync(
                        cancellationToken);

            if (existentes.Count != ids.Count)
            {
                throw new InvalidOperationException(
                    "Una o varias unidades de medida no existen.");
            }

            HashSet<int> unidadesActivas =
                existentes
                    .Where(x => x.activo)
                    .Select(x =>
                        x.unidadMedidaId)
                    .ToHashSet();

            bool configuracionActivaConUnidadInactiva =
                unidades.Any(x =>
                    x.activo &&
                    !unidadesActivas.Contains(
                        x.unidadMedidaId));

            if (configuracionActivaConUnidadInactiva)
            {
                throw new InvalidOperationException(
                    "No puede activar una conversión cuya unidad de medida está inactiva.");
            }
        }

        private async Task ValidarConversionInternaKgHaAsync(
            List<GuardarUnidadConversionDto> unidades,
            CancellationToken cancellationToken)
        {
            UnidadMedida? unidadKgHa =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.activo &&
                            x.nombreUnidadMedida
                                .Trim()
                                .ToLower() ==
                                "kg/ha",
                        cancellationToken);

            if (unidadKgHa == null)
            {
                throw new InvalidOperationException(
                    "No existe la unidad interna kg/ha.");
            }

            bool contieneConversionInterna =
                unidades.Any(x =>
                    x.unidadMedidaId ==
                        unidadKgHa.unidadMedidaId &&
                    x.activo);

            if (!contieneConversionInterna)
            {
                throw new InvalidOperationException(
                    "La configuración debe conservar activa la conversión desde kg/ha, porque los rangos nutricionales se almacenan en esa unidad. Puede ocultarla del formulario, pero no desactivarla.");
            }
        }

    }

    public sealed class ResultadoConversionUnidad
    {
        public int elementoQuimicosId { get; set; }

        public string elemento { get; set; } =
            string.Empty;

        public int unidadOrigenId { get; set; }

        public string unidadOrigen { get; set; } =
            string.Empty;

        public int unidadDestinoId { get; set; }

        public string unidadDestino { get; set; } =
            string.Empty;

        public decimal valorReportado { get; set; }

        public decimal valorConvertido { get; set; }

        public string codigoFormulaConversion { get; set; } =
            string.Empty;

        public string descripcion { get; set; } =
            string.Empty;
    }
}
