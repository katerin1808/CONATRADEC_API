using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using static CONATRADEC_API.DTOs.DashboardWebDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Indicadores ejecutivos para el portal administrativo.
    ///
    /// Esta primera etapa utiliza únicamente datos existentes. No crea tablas,
    /// no altera relaciones y no requiere ejecutar scripts SQL.
    /// </summary>
    [ApiController]
    [Route("api/dashboard")]
    public sealed class DashboardController : ControllerBase
    {
        /*
         * Umbrales operativos iniciales.
         * Más adelante pueden trasladarse a una tabla de configuración.
         */
        private const decimal PhCriticoMaximo = 5.50m;
        private const decimal PhAtencionMaximo = 6.00m;
        private const decimal MateriaOrganicaBajaMaxima = 3.00m;
        private const decimal AcidezAltaMinima = 1.00m;

        private readonly DBContext db;
        private readonly DispositivosConexionDbContext dispositivosDb;

        public DashboardController(
            DBContext db,
            DispositivosConexionDbContext dispositivosDb)
        {
            this.db = db;
            this.dispositivosDb = dispositivosDb;
        }

        /// <summary>
        /// Devuelve el resumen ejecutivo, cobertura territorial, actividad
        /// mensual y alertas calculadas desde el análisis vigente de cada
        /// terreno.
        /// </summary>
        [HttpGet("resumen")]
        public async Task<ActionResult<ResumenDto>> ObtenerResumen(
            CancellationToken cancellationToken = default)
        {
            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime hoyLocal = DateTime.Now;
            DateTime primerDiaMes = new(
                hoyLocal.Year,
                hoyLocal.Month,
                1);

            DateTime inicio30Dias = hoyLocal.Date.AddDays(-29);
            DateTime inicioSerie = primerDiaMes.AddMonths(-5);
            DateTime finSerie = primerDiaMes.AddMonths(1);

            int totalTerrenos = await db.Terreno
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            decimal extensionTotal = await db.Terreno
                .AsNoTracking()
                .Where(item => item.activo)
                .SumAsync(
                    item => (decimal?)item.extensionManzanaTerreno,
                    cancellationToken) ?? 0m;

            decimal produccionEstimada = await db.Terreno
                .AsNoTracking()
                .Where(item => item.activo)
                .SumAsync(
                    item => (decimal?)item.cantidadQuintalesOro,
                    cancellationToken) ?? 0m;

            int totalAnalisis = await db.AnalisisSuelos
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            int analisisMesActual = await db.AnalisisSuelos
                .AsNoTracking()
                .CountAsync(
                    item =>
                        item.activo &&
                        item.fechaCreacionAnalisisSuelo >= primerDiaMes,
                    cancellationToken);

            int analisisUltimos30Dias = await db.AnalisisSuelos
                .AsNoTracking()
                .CountAsync(
                    item =>
                        item.activo &&
                        item.fechaCreacionAnalisisSuelo >= inicio30Dias,
                    cancellationToken);

            int usuariosActivos = await db.Usuarios
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            /*
             * Procedencia 1/2 no se asume. Se clasifica por el nombre real.
             */
            int usuariosInternos = await db.Usuarios
                .AsNoTracking()
                .CountAsync(
                    item =>
                        item.activo &&
                        item.Procedencia.nombreProcedencia == "Interno",
                    cancellationToken);

            int usuariosExternos = Math.Max(
                0,
                usuariosActivos - usuariosInternos);

            /*
             * Un dispositivo se considera conectado si reportó latido dentro
             * de los dos minutos anteriores, igual que el módulo administrativo.
             */
            DateTime corteConexionUtc = ahoraUtc.AddMinutes(-2);

            int dispositivosConectados =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            item.Activo &&
                            item.ConectadoReportado &&
                            item.UltimoLatidoUtc >= corteConexionUtc,
                        cancellationToken);

            int usuariosConectados =
                await dispositivosDb.DispositivosConexion
                    .AsNoTracking()
                    .Where(item =>
                        item.Activo &&
                        item.ConectadoReportado &&
                        item.UltimoLatidoUtc >= corteConexionUtc)
                    .Select(item => item.UsuarioId)
                    .Distinct()
                    .CountAsync(cancellationToken);

            /*
             * Se selecciona únicamente el cálculo activo más reciente de cada
             * terreno para evitar que el historial multiplique las alertas.
             */
            List<int> ultimosCalculosIds =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .GroupBy(item => item.terrenoId)
                    .Select(grupo =>
                        grupo
                            .OrderByDescending(item => item.fechaCalculo)
                            .ThenByDescending(item =>
                                item.analisisSueloCalculoId)
                            .Select(item =>
                                item.analisisSueloCalculoId)
                            .First())
                    .ToListAsync(cancellationToken);

            List<AnalisisSueloCalculo> ultimosCalculos =
                ultimosCalculosIds.Count == 0
                    ? new List<AnalisisSueloCalculo>()
                    : await db.AnalisisSueloCalculos
                        .AsNoTracking()
                        .Where(item =>
                            ultimosCalculosIds.Contains(
                                item.analisisSueloCalculoId))
                        .ToListAsync(cancellationToken);

            int terrenosConAnalisis = ultimosCalculos
                .Select(item => item.terrenoId)
                .Distinct()
                .Count();

            int terrenosSinAnalisis = Math.Max(
                0,
                totalTerrenos - terrenosConAnalisis);

            int terrenosPhCritico = ultimosCalculos.Count(item =>
                item.phAnalisisSuelo > 0m &&
                item.phAnalisisSuelo < PhCriticoMaximo);

            int terrenosPhAtencion = ultimosCalculos.Count(item =>
                item.phAnalisisSuelo >= PhCriticoMaximo &&
                item.phAnalisisSuelo < PhAtencionMaximo);

            int terrenosMateriaOrganicaBaja =
                ultimosCalculos.Count(item =>
                    item.materiaOrganica.HasValue &&
                    item.materiaOrganica.Value <
                        MateriaOrganicaBajaMaxima);

            int terrenosAcidezAlta =
                ultimosCalculos.Count(item =>
                    item.acidezTotal.HasValue &&
                    item.acidezTotal.Value >
                        AcidezAltaMinima);

            int alertasCriticas =
                terrenosPhCritico +
                terrenosAcidezAlta;

            int alertasAtencion =
                terrenosPhAtencion +
                terrenosMateriaOrganicaBaja +
                terrenosSinAnalisis;

            List<AnalisisMesDto> analisisPorMes =
                await ConstruirSerieMensualAsync(
                    inicioSerie,
                    finSerie,
                    cancellationToken);

            List<DepartamentoResumenDto> departamentos =
                await ConstruirDepartamentosAsync(
                    cancellationToken);

            List<AlertaTerrenoDto> alertasRecientes =
                await ConstruirAlertasRecientesAsync(
                    ultimosCalculosIds,
                    cancellationToken);

            decimal porcentajeTerrenosAnalizados =
                CalcularPorcentaje(
                    terrenosConAnalisis,
                    totalTerrenos);

            decimal porcentajePhCritico =
                CalcularPorcentaje(
                    terrenosPhCritico,
                    Math.Max(1, terrenosConAnalisis));

            var respuesta = new ResumenDto
            {
                fechaConsultaUtc = ahoraUtc,

                totalTerrenos = totalTerrenos,
                terrenosConAnalisis = terrenosConAnalisis,
                terrenosSinAnalisis = terrenosSinAnalisis,

                totalAnalisis = totalAnalisis,
                analisisMesActual = analisisMesActual,
                analisisUltimos30Dias = analisisUltimos30Dias,

                usuariosActivos = usuariosActivos,
                usuariosInternos = usuariosInternos,
                usuariosExternos = usuariosExternos,

                dispositivosConectados = dispositivosConectados,
                usuariosConectados = usuariosConectados,

                extensionTotalManzanas =
                    decimal.Round(extensionTotal, 2),
                produccionEstimadaQuintalesOro =
                    decimal.Round(produccionEstimada, 2),

                alertasCriticas = alertasCriticas,
                alertasAtencion = alertasAtencion,

                terrenosPhCritico = terrenosPhCritico,
                terrenosMateriaOrganicaBaja =
                    terrenosMateriaOrganicaBaja,
                terrenosAcidezAlta = terrenosAcidezAlta,

                porcentajeTerrenosAnalizados =
                    porcentajeTerrenosAnalizados,
                porcentajePhCritico = porcentajePhCritico,

                analisisPorMes = analisisPorMes,
                departamentos = departamentos,
                alertasRecientes = alertasRecientes,

                distribucionAlertas =
                [
                    new IndicadorAlertaDto
                    {
                        nombre = "pH crítico",
                        cantidad = terrenosPhCritico,
                        nivel = "CRITICA",
                        descripcion =
                            $"pH menor a {PhCriticoMaximo:N2}"
                    },
                    new IndicadorAlertaDto
                    {
                        nombre = "Acidez alta",
                        cantidad = terrenosAcidezAlta,
                        nivel = "CRITICA",
                        descripcion =
                            $"Acidez mayor a {AcidezAltaMinima:N2}"
                    },
                    new IndicadorAlertaDto
                    {
                        nombre = "Materia orgánica baja",
                        cantidad = terrenosMateriaOrganicaBaja,
                        nivel = "ATENCION",
                        descripcion =
                            $"Materia orgánica menor a {MateriaOrganicaBajaMaxima:N2}"
                    },
                    new IndicadorAlertaDto
                    {
                        nombre = "Sin análisis",
                        cantidad = terrenosSinAnalisis,
                        nivel = "ATENCION",
                        descripcion =
                            "Terrenos activos sin cálculo de suelo"
                    }
                ]
            };

            return Ok(respuesta);
        }

        private async Task<List<AnalisisMesDto>>
            ConstruirSerieMensualAsync(
                DateTime fechaDesde,
                DateTime fechaHasta,
                CancellationToken cancellationToken)
        {
            var agrupados = await db.AnalisisSuelos
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.fechaCreacionAnalisisSuelo >= fechaDesde &&
                    item.fechaCreacionAnalisisSuelo < fechaHasta)
                .GroupBy(item => new
                {
                    item.fechaCreacionAnalisisSuelo.Year,
                    item.fechaCreacionAnalisisSuelo.Month
                })
                .Select(grupo => new
                {
                    grupo.Key.Year,
                    grupo.Key.Month,
                    Cantidad = grupo.Count()
                })
                .ToListAsync(cancellationToken);

            Dictionary<(int Year, int Month), int> cantidades =
                agrupados.ToDictionary(
                    item => (item.Year, item.Month),
                    item => item.Cantidad);

            CultureInfo cultura =
                CultureInfo.GetCultureInfo("es-NI");

            var resultado = new List<AnalisisMesDto>(6);

            for (int indice = 0; indice < 6; indice++)
            {
                DateTime mes = fechaDesde.AddMonths(indice);

                cantidades.TryGetValue(
                    (mes.Year, mes.Month),
                    out int cantidad);

                string nombre = cultura.DateTimeFormat
                    .GetAbbreviatedMonthName(mes.Month)
                    .TrimEnd('.');

                resultado.Add(new AnalisisMesDto
                {
                    mes = char.ToUpperInvariant(nombre[0]) +
                          nombre[1..],
                    cantidad = cantidad
                });
            }

            return resultado;
        }

        private async Task<List<DepartamentoResumenDto>>
            ConstruirDepartamentosAsync(
                CancellationToken cancellationToken)
        {
            var datos = await db.Terreno
                .AsNoTracking()
                .Where(item => item.activo)
                .GroupBy(item =>
                    item.Municipio.Departamento.NombreDepartamento)
                .Select(grupo => new
                {
                    Departamento = grupo.Key,
                    Terrenos = grupo.Count(),
                    Extension = grupo.Sum(item =>
                        item.extensionManzanaTerreno),
                    Analizados = grupo.Count(terreno =>
                        db.AnalisisSueloCalculos.Any(calculo =>
                            calculo.activo &&
                            calculo.terrenoId ==
                                terreno.terrenoId))
                })
                .OrderByDescending(item => item.Terrenos)
                .Take(8)
                .ToListAsync(cancellationToken);

            return datos.Select(item =>
                new DepartamentoResumenDto
                {
                    departamento = item.Departamento,
                    terrenos = item.Terrenos,
                    terrenosAnalizados = item.Analizados,
                    extensionManzanas =
                        decimal.Round(item.Extension, 2),
                    coberturaAnalisisPorcentaje =
                        CalcularPorcentaje(
                            item.Analizados,
                            item.Terrenos)
                })
                .ToList();
        }

        private async Task<List<AlertaTerrenoDto>>
            ConstruirAlertasRecientesAsync(
                IReadOnlyCollection<int> ultimosCalculosIds,
                CancellationToken cancellationToken)
        {
            if (ultimosCalculosIds.Count == 0)
                return new List<AlertaTerrenoDto>();

            var datos = await (
                from calculo in db.AnalisisSueloCalculos.AsNoTracking()
                join terreno in db.Terreno.AsNoTracking()
                    on calculo.terrenoId equals terreno.terrenoId
                where
                    ultimosCalculosIds.Contains(
                        calculo.analisisSueloCalculoId) &&
                    terreno.activo &&
                    (
                        calculo.phAnalisisSuelo < PhAtencionMaximo ||
                        (calculo.materiaOrganica.HasValue &&
                         calculo.materiaOrganica.Value <
                            MateriaOrganicaBajaMaxima) ||
                        (calculo.acidezTotal.HasValue &&
                         calculo.acidezTotal.Value >
                            AcidezAltaMinima)
                    )
                orderby calculo.fechaCalculo descending
                select new
                {
                    Terreno = terreno,
                    Calculo = calculo,
                    Departamento =
                        terreno.Municipio.Departamento
                            .NombreDepartamento,
                    Municipio =
                        terreno.Municipio.NombreMunicipio
                })
                .Take(30)
                .ToListAsync(cancellationToken);

            var alertas = new List<AlertaTerrenoDto>();

            foreach (var item in datos)
            {
                if (item.Calculo.phAnalisisSuelo <
                    PhCriticoMaximo)
                {
                    alertas.Add(CrearAlerta(
                        item.Terreno,
                        item.Departamento,
                        item.Municipio,
                        item.Calculo.fechaCalculo,
                        "CRITICA",
                        "pH crítico",
                        "El pH del suelo requiere atención inmediata.",
                        item.Calculo.phAnalisisSuelo,
                        "pH"));
                }
                else if (item.Calculo.phAnalisisSuelo <
                         PhAtencionMaximo)
                {
                    alertas.Add(CrearAlerta(
                        item.Terreno,
                        item.Departamento,
                        item.Municipio,
                        item.Calculo.fechaCalculo,
                        "ATENCION",
                        "pH bajo",
                        "El pH se encuentra por debajo del rango operativo.",
                        item.Calculo.phAnalisisSuelo,
                        "pH"));
                }

                if (item.Calculo.acidezTotal.HasValue &&
                    item.Calculo.acidezTotal.Value >
                    AcidezAltaMinima)
                {
                    alertas.Add(CrearAlerta(
                        item.Terreno,
                        item.Departamento,
                        item.Municipio,
                        item.Calculo.fechaCalculo,
                        "CRITICA",
                        "Acidez alta",
                        "La acidez total supera el umbral operativo.",
                        item.Calculo.acidezTotal.Value,
                        "meq/100g"));
                }

                if (item.Calculo.materiaOrganica.HasValue &&
                    item.Calculo.materiaOrganica.Value <
                    MateriaOrganicaBajaMaxima)
                {
                    alertas.Add(CrearAlerta(
                        item.Terreno,
                        item.Departamento,
                        item.Municipio,
                        item.Calculo.fechaCalculo,
                        "ATENCION",
                        "Materia orgánica baja",
                        "La materia orgánica se encuentra por debajo del umbral.",
                        item.Calculo.materiaOrganica.Value,
                        "%"));
                }
            }

            return alertas
                .OrderBy(item =>
                    item.nivel == "CRITICA" ? 0 : 1)
                .ThenByDescending(item => item.fechaAnalisis)
                .Take(12)
                .ToList();
        }

        private static AlertaTerrenoDto CrearAlerta(
            Terreno terreno,
            string departamento,
            string municipio,
            DateTime fechaAnalisis,
            string nivel,
            string tipo,
            string mensaje,
            decimal? valor,
            string unidad) =>
            new()
            {
                terrenoId = terreno.terrenoId,
                codigoTerreno = terreno.codigoTerreno,
                propietario = terreno.nombrePropietarioTerreno,
                departamento = departamento,
                municipio = municipio,
                nivel = nivel,
                tipo = tipo,
                mensaje = mensaje,
                valor = valor,
                unidad = unidad,
                fechaAnalisis = fechaAnalisis
            };

        private static decimal CalcularPorcentaje(
            int valor,
            int total)
        {
            if (total <= 0)
                return 0m;

            return decimal.Round(
                valor * 100m / total,
                1);
        }
    }
}
