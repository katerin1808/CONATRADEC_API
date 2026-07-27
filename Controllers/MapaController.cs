using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.MapaWebDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints optimizados para mapas del portal administrativo.
    /// </summary>
    [ApiController]
    [Route("api/mapa")]
    public sealed class MapaController : ControllerBase
    {
        private readonly DBContext _db;

        public MapaController(DBContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Devuelve únicamente los datos necesarios para pintar los terrenos
        /// activos en el mapa. No carga fotografías ni análisis completos.
        /// </summary>
        [HttpGet("terrenos")]
        public async Task<ActionResult<List<TerrenoMapaDto>>> ListarTerrenos(
            int? departamentoId = null,
            int? municipioId = null,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Terreno> query = _db.Terreno
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.latitud >= -90 &&
                    item.latitud <= 90 &&
                    item.longitud >= -180 &&
                    item.longitud <= 180 &&
                    /*
                     * Evita mostrar el punto 0,0 cuando una ubicación no fue
                     * capturada correctamente.
                     */
                    !(item.latitud == 0 && item.longitud == 0));

            if (departamentoId is > 0)
            {
                query = query.Where(item =>
                    item.Municipio.DepartamentoId ==
                    departamentoId.Value);
            }

            if (municipioId is > 0)
            {
                query = query.Where(item =>
                    item.municipioId == municipioId.Value);
            }

            /*
             * La subconsulta correlacionada obtiene solamente el cálculo más
             * reciente de cada terreno. SQL Server realiza el trabajo sin
             * descargar el historial completo a memoria.
             */
            List<TerrenoMapaDto> terrenos = await query
                .OrderBy(item => item.codigoTerreno)
                .Select(item => new TerrenoMapaDto
                {
                    terrenoId = item.terrenoId,
                    codigo = item.codigoTerreno,
                    nombre = item.direccionTerreno,
                    productor = item.nombrePropietarioTerreno,
                    latitud = item.latitud,
                    longitud = item.longitud,
                    departamento =
                        item.Municipio.Departamento.NombreDepartamento,
                    municipio = item.Municipio.NombreMunicipio,
                    extensionManzanas = item.extensionManzanaTerreno,
                    estado = _db.AnalisisSueloCalculos
                        .Any(calculo =>
                            calculo.activo &&
                            calculo.terrenoId == item.terrenoId)
                                ? "Con análisis"
                                : "Sin análisis",
                    ultimoPh = _db.AnalisisSueloCalculos
                        .Where(calculo =>
                            calculo.activo &&
                            calculo.terrenoId == item.terrenoId)
                        .OrderByDescending(calculo =>
                            calculo.fechaCalculo)
                        .ThenByDescending(calculo =>
                            calculo.analisisSueloCalculoId)
                        .Select(calculo =>
                            (decimal?)calculo.phAnalisisSuelo)
                        .FirstOrDefault(),
                    fechaUltimoAnalisis = _db.AnalisisSueloCalculos
                        .Where(calculo =>
                            calculo.activo &&
                            calculo.terrenoId == item.terrenoId)
                        .OrderByDescending(calculo =>
                            calculo.fechaCalculo)
                        .ThenByDescending(calculo =>
                            calculo.analisisSueloCalculoId)
                        .Select(calculo =>
                            (DateTime?)calculo.fechaCalculo)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return Ok(terrenos);
        }
    }
}
