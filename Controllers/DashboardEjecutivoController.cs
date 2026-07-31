using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/dashboard-ejecutivo")]
    public sealed class DashboardEjecutivoController : ControllerBase
    {
        private readonly DBContext db;
        private readonly DispositivosConexionDbContext dispositivosDb;
        private readonly AlertasAgricolasDbContext alertasDb;
        private readonly UmbralesAlertasService umbralesService;

        public DashboardEjecutivoController(
            DBContext db,
            DispositivosConexionDbContext dispositivosDb,
            AlertasAgricolasDbContext alertasDb,
            UmbralesAlertasService umbralesService)
        {
            this.db = db;
            this.dispositivosDb = dispositivosDb;
            this.alertasDb = alertasDb;
            this.umbralesService = umbralesService;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult<DashboardEjecutivoDto>> ObtenerResumen(
            CancellationToken cancellationToken = default)
        {
            DateTime ahoraUtc = DateTime.UtcNow;
            DateTime hoy = DateTime.Now;
            DateTime inicioMes = new(hoy.Year, hoy.Month, 1);
            DateTime inicio30Dias = hoy.Date.AddDays(-29);
            DateTime inicioSerie = inicioMes.AddMonths(-5);
            DateTime finSerie = inicioMes.AddMonths(1);

            UmbralesAlertas umbrales =
                await umbralesService.ObtenerAsync(cancellationToken);

            int totalTerrenos = await db.Terreno.AsNoTracking()
                .CountAsync(x => x.activo, cancellationToken);

            decimal extensionTotal = await db.Terreno.AsNoTracking()
                .Where(x => x.activo)
                .SumAsync(x => (decimal?)x.extensionManzanaTerreno, cancellationToken) ?? 0m;

            decimal produccionTotal = await db.Terreno.AsNoTracking()
                .Where(x => x.activo)
                .SumAsync(x => (decimal?)x.cantidadQuintalesOro, cancellationToken) ?? 0m;

            int totalAnalisis = await db.AnalisisSuelos.AsNoTracking()
                .CountAsync(x => x.activo, cancellationToken);

            int analisisMes = await db.AnalisisSuelos.AsNoTracking()
                .CountAsync(x => x.activo && x.fechaCreacionAnalisisSuelo >= inicioMes,
                    cancellationToken);

            int analisis30Dias = await db.AnalisisSuelos.AsNoTracking()
                .CountAsync(x => x.activo && x.fechaCreacionAnalisisSuelo >= inicio30Dias,
                    cancellationToken);

            int usuariosActivos = await db.Usuarios.AsNoTracking()
                .CountAsync(x => x.activo, cancellationToken);

            int usuariosInternos = await db.Usuarios.AsNoTracking()
                .CountAsync(x => x.activo &&
                    x.Procedencia.nombreProcedencia == "Interno",
                    cancellationToken);

            int usuariosExternos = Math.Max(0, usuariosActivos - usuariosInternos);

            DateTime corteConexion = ahoraUtc.AddMinutes(-2);

            int dispositivosConectados = await dispositivosDb.DispositivosConexion
                .AsNoTracking()
                .CountAsync(x => x.Activo && x.ConectadoReportado &&
                    x.UltimoLatidoUtc >= corteConexion,
                    cancellationToken);

            int usuariosConectados = await dispositivosDb.DispositivosConexion
                .AsNoTracking()
                .Where(x => x.Activo && x.ConectadoReportado &&
                    x.UltimoLatidoUtc >= corteConexion)
                .Select(x => x.UsuarioId)
                .Distinct()
                .CountAsync(cancellationToken);

            List<int> ultimosIds = await db.AnalisisSueloCalculos.AsNoTracking()
                .Where(x => x.activo)
                .GroupBy(x => x.terrenoId)
                .Select(g => g
                    .OrderByDescending(x => x.fechaCalculo)
                    .ThenByDescending(x => x.analisisSueloCalculoId)
                    .Select(x => x.analisisSueloCalculoId)
                    .First())
                .ToListAsync(cancellationToken);

            List<AnalisisSueloCalculo> ultimos = ultimosIds.Count == 0
                ? []
                : await db.AnalisisSueloCalculos.AsNoTracking()
                    .Where(x => ultimosIds.Contains(x.analisisSueloCalculoId))
                    .ToListAsync(cancellationToken);

            int terrenosConAnalisis = ultimos.Select(x => x.terrenoId).Distinct().Count();
            int terrenosSinAnalisis = Math.Max(0, totalTerrenos - terrenosConAnalisis);

            int phBajoCritico = ultimos.Count(x =>
                x.phAnalisisSuelo < umbrales.PhBajoCriticoMaximo);

            int phAltoCritico = ultimos.Count(x =>
                x.phAnalisisSuelo >= umbrales.PhAltoCriticoMinimo);

            int phBajoAtencion = ultimos.Count(x =>
                x.phAnalisisSuelo >= umbrales.PhBajoCriticoMaximo &&
                x.phAnalisisSuelo < umbrales.PhBajoAtencionMaximo);

            int phAltoAtencion = ultimos.Count(x =>
                x.phAnalisisSuelo >= umbrales.PhAltoAtencionMinimo &&
                x.phAnalisisSuelo < umbrales.PhAltoCriticoMinimo);

            int materiaOrganicaBaja = ultimos.Count(x =>
                x.materiaOrganica.HasValue &&
                x.materiaOrganica.Value < umbrales.MateriaOrganicaBajaMaxima);

            int acidezAlta = ultimos.Count(x =>
                x.acidezTotal.HasValue &&
                x.acidezTotal.Value > umbrales.AcidezAltaMinima);

            int alertasCriticas = phBajoCritico + phAltoCritico + acidezAlta;
            int alertasAtencion = phBajoAtencion + phAltoAtencion +
                                  materiaOrganicaBaja + terrenosSinAnalisis;

            int terrenosNormales = ultimos.Count(x =>
                x.phAnalisisSuelo >= umbrales.PhBajoAtencionMaximo &&
                x.phAnalisisSuelo < umbrales.PhAltoAtencionMinimo &&
                (!x.materiaOrganica.HasValue ||
                 x.materiaOrganica.Value >= umbrales.MateriaOrganicaBajaMaxima) &&
                (!x.acidezTotal.HasValue ||
                 x.acidezTotal.Value <= umbrales.AcidezAltaMinima));

            var seguimientos = await alertasDb.Seguimientos.AsNoTracking()
                .Where(x => x.Activo)
                .ToListAsync(cancellationToken);

            int pendientes = seguimientos.Count(x => x.Estado == "PENDIENTE");
            int enProceso = seguimientos.Count(x => x.Estado == "EN_PROCESO");
            int atendidos = seguimientos.Count(x => x.Estado == "ATENDIDA");
            int descartados = seguimientos.Count(x => x.Estado == "DESCARTADA");
            int sinAsignar = seguimientos.Count(x =>
                x.Estado != "ATENDIDA" && x.Estado != "DESCARTADA" &&
                !x.UsuarioAsignadoId.HasValue);

            int cerrados = atendidos + descartados;

            List<DashboardSerieMesDto> serie =
                await ConstruirSerieAsync(inicioSerie, finSerie, cancellationToken);

            List<DashboardDepartamentoDto> departamentos =
                await ConstruirDepartamentosAsync(cancellationToken);

            List<DashboardAlertaDto> alertas =
                await ConstruirAlertasAsync(ultimosIds, umbrales, cancellationToken);

            List<DashboardTecnicoDto> tecnicos =
                await ConstruirTecnicosAsync(seguimientos, cancellationToken);

            return Ok(new DashboardEjecutivoDto
            {
                fechaConsultaUtc = ahoraUtc,
                totalTerrenos = totalTerrenos,
                terrenosConAnalisis = terrenosConAnalisis,
                terrenosSinAnalisis = terrenosSinAnalisis,
                extensionTotalManzanas = decimal.Round(extensionTotal, 2),
                produccionEstimadaQuintalesOro = decimal.Round(produccionTotal, 2),
                totalAnalisis = totalAnalisis,
                analisisMesActual = analisisMes,
                analisisUltimos30Dias = analisis30Dias,
                usuariosActivos = usuariosActivos,
                usuariosInternos = usuariosInternos,
                usuariosExternos = usuariosExternos,
                dispositivosConectados = dispositivosConectados,
                usuariosConectados = usuariosConectados,
                alertasCriticas = alertasCriticas,
                alertasAtencion = alertasAtencion,
                terrenosNormales = terrenosNormales,
                seguimientosPendientes = pendientes,
                seguimientosEnProceso = enProceso,
                seguimientosAtendidos = atendidos,
                seguimientosDescartados = descartados,
                seguimientosSinAsignar = sinAsignar,
                porcentajeTerrenosAnalizados = Porcentaje(terrenosConAnalisis, totalTerrenos),
                porcentajeSeguimientosCerrados = Porcentaje(cerrados, seguimientos.Count),
                analisisPorMes = serie,
                departamentos = departamentos,
                alertasRecientes = alertas,
                tecnicos = tecnicos,
                distribucionAlertas =
                [
                    new()
                    {
                        nombre = "pH muy bajo",
                        cantidad = phBajoCritico,
                        nivel = "CRITICA",
                        descripcion = $"Menor a {umbrales.PhBajoCriticoMaximo:N2}"
                    },
                    new()
                    {
                        nombre = "pH muy alto",
                        cantidad = phAltoCritico,
                        nivel = "CRITICA",
                        descripcion = $"Desde {umbrales.PhAltoCriticoMinimo:N2}"
                    },
                    new()
                    {
                        nombre = "Acidez alta",
                        cantidad = acidezAlta,
                        nivel = "CRITICA",
                        descripcion = $"Mayor a {umbrales.AcidezAltaMinima:N2}"
                    },
                    new()
                    {
                        nombre = "pH fuera del rango",
                        cantidad = phBajoAtencion + phAltoAtencion,
                        nivel = "ATENCION",
                        descripcion = "Requiere atención técnica"
                    },
                    new()
                    {
                        nombre = "Materia orgánica baja",
                        cantidad = materiaOrganicaBaja,
                        nivel = "ATENCION",
                        descripcion = $"Menor a {umbrales.MateriaOrganicaBajaMaxima:N2}%"
                    },
                    new()
                    {
                        nombre = "Sin análisis",
                        cantidad = terrenosSinAnalisis,
                        nivel = "ATENCION",
                        descripcion = "Terrenos sin análisis activo"
                    }
                ]
            });
        }

        private async Task<List<DashboardSerieMesDto>> ConstruirSerieAsync(
            DateTime desde,
            DateTime hasta,
            CancellationToken cancellationToken)
        {
            var datos = await db.AnalisisSuelos.AsNoTracking()
                .Where(x => x.activo &&
                    x.fechaCreacionAnalisisSuelo >= desde &&
                    x.fechaCreacionAnalisisSuelo < hasta)
                .GroupBy(x => new
                {
                    x.fechaCreacionAnalisisSuelo.Year,
                    x.fechaCreacionAnalisisSuelo.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Cantidad = g.Count()
                })
                .ToListAsync(cancellationToken);

            var mapa = datos.ToDictionary(x => (x.Year, x.Month), x => x.Cantidad);
            CultureInfo cultura = CultureInfo.GetCultureInfo("es-NI");
            var resultado = new List<DashboardSerieMesDto>();

            for (int i = 0; i < 6; i++)
            {
                DateTime mes = desde.AddMonths(i);
                mapa.TryGetValue((mes.Year, mes.Month), out int cantidad);
                string nombre = cultura.DateTimeFormat
                    .GetAbbreviatedMonthName(mes.Month)
                    .TrimEnd('.');

                resultado.Add(new DashboardSerieMesDto
                {
                    mes = char.ToUpperInvariant(nombre[0]) + nombre[1..],
                    cantidad = cantidad
                });
            }

            return resultado;
        }

        private async Task<List<DashboardDepartamentoDto>> ConstruirDepartamentosAsync(
            CancellationToken cancellationToken)
        {
            var datos = await db.Terreno.AsNoTracking()
                .Where(x => x.activo)
                .GroupBy(x => x.Municipio.Departamento.NombreDepartamento)
                .Select(g => new
                {
                    Departamento = g.Key,
                    Terrenos = g.Count(),
                    Extension = g.Sum(x => x.extensionManzanaTerreno),
                    Analizados = g.Count(t => db.AnalisisSueloCalculos
                        .Any(c => c.activo && c.terrenoId == t.terrenoId))
                })
                .OrderByDescending(x => x.Terrenos)
                .Take(8)
                .ToListAsync(cancellationToken);

            return datos.Select(x => new DashboardDepartamentoDto
            {
                departamento = x.Departamento,
                terrenos = x.Terrenos,
                terrenosAnalizados = x.Analizados,
                extensionManzanas = decimal.Round(x.Extension, 2),
                coberturaAnalisisPorcentaje = Porcentaje(x.Analizados, x.Terrenos)
            }).ToList();
        }

        private async Task<List<DashboardAlertaDto>> ConstruirAlertasAsync(
            IReadOnlyCollection<int> ultimosIds,
            UmbralesAlertas umbrales,
            CancellationToken cancellationToken)
        {
            if (ultimosIds.Count == 0)
                return [];

            var datos = await (
                from calculo in db.AnalisisSueloCalculos.AsNoTracking()
                join terreno in db.Terreno.AsNoTracking()
                    on calculo.terrenoId equals terreno.terrenoId
                where ultimosIds.Contains(calculo.analisisSueloCalculoId) && terreno.activo
                orderby calculo.fechaCalculo descending
                select new
                {
                    Calculo = calculo,
                    Terreno = terreno,
                    Propietario =
                        terreno.RelacionesPropietario
                            .Where(relacion =>
                                relacion.activo &&
                                relacion.Propietario.activo)
                            .Select(relacion =>
                                relacion.Propietario.nombreCompleto)
                            .FirstOrDefault() ??
                        string.Empty,
                    Municipio = terreno.Municipio.NombreMunicipio,
                    Departamento = terreno.Municipio.Departamento.NombreDepartamento
                })
                .Take(40)
                .ToListAsync(cancellationToken);

            var abiertos = await alertasDb.Seguimientos.AsNoTracking()
                .Where(x => x.Activo &&
                    x.Estado != "ATENDIDA" && x.Estado != "DESCARTADA")
                .ToListAsync(cancellationToken);

            int[] responsablesIds = abiertos
                .Where(x => x.UsuarioAsignadoId.HasValue)
                .Select(x => x.UsuarioAsignadoId!.Value)
                .Distinct()
                .ToArray();

            Dictionary<int, string> responsables = responsablesIds.Length == 0
                ? []
                : await db.Usuarios.AsNoTracking()
                    .Where(x => responsablesIds.Contains(x.UsuarioId))
                    .ToDictionaryAsync(x => x.UsuarioId,
                        x => x.nombreCompletoUsuario,
                        cancellationToken);

            var resultado = new List<DashboardAlertaDto>();

            foreach (var item in datos)
            {
                void Agregar(string nivel, string tipo, string mensaje,
                    decimal? valor, string unidad)
                {
                    var seguimiento = abiertos.FirstOrDefault(x =>
                        x.TerrenoId == item.Terreno.terrenoId &&
                        string.Equals(x.TipoAlerta, tipo,
                            StringComparison.OrdinalIgnoreCase));

                    resultado.Add(new DashboardAlertaDto
                    {
                        terrenoId = item.Terreno.terrenoId,
                        codigoTerreno = item.Terreno.codigoTerreno,
                        propietario = item.Propietario,
                        departamento = item.Departamento,
                        municipio = item.Municipio,
                        nivel = nivel,
                        tipo = tipo,
                        mensaje = mensaje,
                        valor = valor,
                        unidad = unidad,
                        fechaAnalisis = item.Calculo.fechaCalculo,
                        seguimientoId = seguimiento?.SeguimientoAlertaAgricolaId,
                        estadoSeguimiento = seguimiento?.Estado,
                        responsable = seguimiento?.UsuarioAsignadoId is int id &&
                            responsables.TryGetValue(id, out string? nombre)
                                ? nombre
                                : null
                    });
                }

                decimal ph = item.Calculo.phAnalisisSuelo;

                if (ph < umbrales.PhBajoCriticoMaximo)
                    Agregar("CRITICA", "pH muy bajo",
                        "El pH requiere atención inmediata.", ph, "pH");
                else if (ph < umbrales.PhBajoAtencionMaximo)
                    Agregar("ATENCION", "pH bajo",
                        "El pH está por debajo del rango operativo.", ph, "pH");
                else if (ph >= umbrales.PhAltoCriticoMinimo)
                    Agregar("CRITICA", "pH muy alto",
                        "El pH requiere atención inmediata.", ph, "pH");
                else if (ph >= umbrales.PhAltoAtencionMinimo)
                    Agregar("ATENCION", "pH alto",
                        "El pH está por encima del rango operativo.", ph, "pH");

                if (item.Calculo.acidezTotal.HasValue &&
                    item.Calculo.acidezTotal.Value > umbrales.AcidezAltaMinima)
                    Agregar("CRITICA", "Acidez alta",
                        "La acidez total supera el umbral operativo.",
                        item.Calculo.acidezTotal.Value, "meq/100g");

                if (item.Calculo.materiaOrganica.HasValue &&
                    item.Calculo.materiaOrganica.Value < umbrales.MateriaOrganicaBajaMaxima)
                    Agregar("ATENCION", "Materia orgánica baja",
                        "La materia orgánica requiere seguimiento.",
                        item.Calculo.materiaOrganica.Value, "%");
            }

            return resultado
                .OrderBy(x => x.nivel == "CRITICA" ? 0 : 1)
                .ThenByDescending(x => x.fechaAnalisis)
                .Take(12)
                .ToList();
        }

        private async Task<List<DashboardTecnicoDto>> ConstruirTecnicosAsync(
            IReadOnlyCollection<SeguimientoAlertaAgricola> seguimientos,
            CancellationToken cancellationToken)
        {
            int[] ids = seguimientos
                .Where(x => x.UsuarioAsignadoId.HasValue)
                .Select(x => x.UsuarioAsignadoId!.Value)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return [];

            Dictionary<int, string> nombres = await db.Usuarios.AsNoTracking()
                .Where(x => ids.Contains(x.UsuarioId))
                .ToDictionaryAsync(x => x.UsuarioId,
                    x => x.nombreCompletoUsuario,
                    cancellationToken);

            return seguimientos
                .Where(x => x.UsuarioAsignadoId.HasValue)
                .GroupBy(x => x.UsuarioAsignadoId!.Value)
                .Select(g => new DashboardTecnicoDto
                {
                    usuarioId = g.Key,
                    nombre = nombres.TryGetValue(g.Key, out string? nombre)
                        ? nombre
                        : $"Usuario #{g.Key}",
                    pendientes = g.Count(x => x.Estado == "PENDIENTE"),
                    enProceso = g.Count(x => x.Estado == "EN_PROCESO"),
                    atendidos = g.Count(x => x.Estado == "ATENDIDA"),
                    totalAbiertos = g.Count(x =>
                        x.Estado != "ATENDIDA" && x.Estado != "DESCARTADA")
                })
                .OrderByDescending(x => x.totalAbiertos)
                .ThenBy(x => x.nombre)
                .Take(8)
                .ToList();
        }

        private static decimal Porcentaje(int valor, int total)
        {
            if (total <= 0)
                return 0m;

            return decimal.Round(valor * 100m / total, 1);
        }
    }
}
