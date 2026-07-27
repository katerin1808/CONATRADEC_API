using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using static CONATRADEC_API.DTOs.DashboardWebDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Indicadores resumidos para el portal administrativo.
    /// Las consultas se ejecutan directamente en SQL y solo retornan totales.
    /// </summary>
    [ApiController]
    [Route("api/dashboard")]
    public sealed class DashboardController : ControllerBase
    {
        private readonly DBContext _db;

        public DashboardController(DBContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Obtiene los indicadores generales y la cantidad de análisis
        /// registrados durante los últimos seis meses.
        /// </summary>
        [HttpGet("resumen")]
        public async Task<ActionResult<ResumenDto>> ObtenerResumen(
            CancellationToken cancellationToken = default)
        {
            /*
             * Se toma el primer día del quinto mes anterior para formar un
             * intervalo de seis meses incluyendo el mes actual.
             */
            DateTime hoy = DateTime.Now;
            DateTime primerMes = new(
                hoy.Year,
                hoy.Month,
                1);

            DateTime fechaDesde = primerMes.AddMonths(-5);
            DateTime fechaHasta = primerMes.AddMonths(1);

            /*
             * Se ejecutan una por una porque un mismo DbContext de Entity
             * Framework no admite varias consultas activas simultáneamente.
             */
            int totalTerrenos = await _db.Terreno
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            int totalAnalisis = await _db.AnalisisSuelos
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            int usuariosActivos = await _db.Usuarios
                .AsNoTracking()
                .CountAsync(
                    item => item.activo,
                    cancellationToken);

            var agrupados = await _db.AnalisisSuelos
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

            var cantidades = agrupados.ToDictionary(
                item => (item.Year, item.Month),
                item => item.Cantidad);

            var cultura = CultureInfo.GetCultureInfo("es-NI");
            var analisisPorMes = new List<AnalisisMesDto>(6);

            for (int indice = 0; indice < 6; indice++)
            {
                DateTime mesActual = fechaDesde.AddMonths(indice);

                cantidades.TryGetValue(
                    (mesActual.Year, mesActual.Month),
                    out int cantidad);

                string nombreMes = cultura.DateTimeFormat
                    .GetAbbreviatedMonthName(mesActual.Month)
                    .TrimEnd('.');

                analisisPorMes.Add(new AnalisisMesDto
                {
                    mes = char.ToUpperInvariant(nombreMes[0]) +
                          nombreMes[1..],
                    cantidad = cantidad
                });
            }

            var respuesta = new ResumenDto
            {
                totalTerrenos = totalTerrenos,
                totalAnalisis = totalAnalisis,
                usuariosActivos = usuariosActivos,
                totalDiagnosticos = 0,
                analisisPorMes = analisisPorMes
            };

            return Ok(respuesta);
        }
    }
}
