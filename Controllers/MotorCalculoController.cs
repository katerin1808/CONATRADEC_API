using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/motor-calculo")]
    public sealed class MotorCalculoController : ControllerBase
    {
        private const string InterfazDatosSinConexion =
            "datosSinConexionPage";

        private readonly DBContext db;

        private static readonly JsonSerializerOptions HashJsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                WriteIndented = false
            };

        public MotorCalculoController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("estado")]
        public async Task<IActionResult> ObtenerEstado(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(cancellationToken);

            if (acceso != null)
                return acceso;

            MotorCalculoPaqueteDto paquete =
                await ConstruirPaqueteAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Estado del motor obtenido correctamente.",
                data = new MotorCalculoEstadoDto
                {
                    versionEsquema = paquete.versionEsquema,
                    versionMotorBase = paquete.versionMotorBase,
                    versionPaquete = paquete.versionPaquete,
                    hashSha256 = paquete.hashSha256,
                    versionMinimaAplicacion =
                        paquete.versionMinimaAplicacion,
                    fechaGeneracionUtc = paquete.fechaGeneracionUtc,
                    modulos = paquete.modulos
                }
            });
        }

        [HttpGet("paquete")]
        public async Task<IActionResult> DescargarPaquete(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(cancellationToken);

            if (acceso != null)
                return acceso;

            MotorCalculoPaqueteDto paquete =
                await ConstruirPaqueteAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Paquete completo del motor de cálculo generado correctamente.",
                data = paquete
            });
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            CancellationToken cancellationToken)
        {
            string usuarioIdTexto =
                Request.Headers["X-Usuario-Id"].ToString();

            if (!int.TryParse(usuarioIdTexto, out int usuarioId) ||
                usuarioId <= 0)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "No se recibió una sesión válida."
                });
            }

            var usuario =
                await db.Usuarios
                    .AsNoTracking()
                    .Where(item =>
                        item.UsuarioId == usuarioId &&
                        item.activo)
                    .Select(item => new
                    {
                        item.UsuarioId,
                        item.rolId
                    })
                    .FirstOrDefaultAsync(cancellationToken);

            if (usuario == null)
            {
                return Unauthorized(new
                {
                    success = false,
                    message =
                        "La sesión no pertenece a un usuario activo."
                });
            }

            bool permitido =
                await (
                    from relacion in db.RolInterfaz.AsNoTracking()
                    join interfaz in db.Interfaz.AsNoTracking()
                        on relacion.interfazId equals interfaz.interfazId
                    where
                        relacion.rolId == usuario.rolId &&
                        interfaz.activo &&
                        interfaz.nombreInterfaz ==
                            InterfazDatosSinConexion &&
                        relacion.leer == true
                    select relacion.rolInterfazId
                )
                .AnyAsync(cancellationToken);

            if (!permitido)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        message =
                            "Su usuario no tiene habilitado el trabajo sin conexión."
                    });
            }

            return null;
        }

        private async Task<MotorCalculoPaqueteDto>
            ConstruirPaqueteAsync(
                CancellationToken cancellationToken)
        {
            var contenido =
                new MotorCalculoContenidoDto();

            UnidadMedida? unidadResultado =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.activo &&
                            item.nombreUnidadMedida.Trim().ToLower() ==
                                "lb/mz",
                        cancellationToken);

            if (unidadResultado == null)
            {
                throw new InvalidOperationException(
                    "No existe la unidad de resultado lb/Mz.");
            }

            UnidadMedida? unidadKgHa =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.activo &&
                            item.nombreUnidadMedida.Trim().ToLower() ==
                                "kg/ha",
                        cancellationToken);

            if (unidadKgHa == null)
            {
                throw new InvalidOperationException(
                    "No existe la unidad interna kg/ha.");
            }

            contenido.unidadResultadoId =
                unidadResultado.unidadMedidaId;

            contenido.unidadResultado =
                unidadResultado.nombreUnidadMedida.Trim();

            contenido.unidadRangoKgHaId =
                unidadKgHa.unidadMedidaId;

            contenido.tiposCultivo =
                await db.TipoCultivos
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.nombreTipoCultivo)
                    .Select(item => new MotorTipoCultivoDto
                    {
                        tipoCultivoId = item.tipoCultivoId,
                        nombreTipoCultivo =
                            item.nombreTipoCultivo,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.tiposAnalisis =
                await db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item =>
                        item.nombreTipoAnalisisSuelo)
                    .Select(item => new MotorTipoAnalisisDto
                    {
                        tipoAnalisisSueloId =
                            item.tipoAnalisisSueloId,
                        nombreTipoAnalisisSuelo =
                            item.nombreTipoAnalisisSuelo,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.elementos =
                await db.elementoQuimico
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item =>
                        item.nombreElementoQuimico)
                    .Select(item => new MotorElementoDto
                    {
                        elementoQuimicosId =
                            item.elementoQuimicosId,
                        simboloElementoQuimico =
                            item.simboloElementoQuimico,
                        nombreElementoQuimico =
                            item.nombreElementoQuimico,
                        pesoEquivalenteElementoQuimico =
                            item.pesoEquivalenteElementoQuimico,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.unidades =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.nombreUnidadMedida)
                    .Select(item => new MotorUnidadDto
                    {
                        unidadMedidaId = item.unidadMedidaId,
                        nombreUnidadMedida =
                            item.nombreUnidadMedida,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.conversionesElementos =
                await db.Set<ElementoQuimicoUnidadMedida>()
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.elementoQuimicosId)
                    .ThenBy(item => item.orden)
                    .Select(item =>
                        new MotorConversionElementoDto
                        {
                            elementoQuimicosId =
                                item.elementoQuimicosId,
                            unidadMedidaId =
                                item.unidadMedidaId,
                            codigoFormulaConversion =
                                item.codigoFormulaConversion,
                            factorPrincipal =
                                item.factorPrincipal,
                            factorSecundario =
                                item.factorSecundario,
                            factorTerciario =
                                item.factorTerciario,
                            divisor = item.divisor,
                            desplazamiento =
                                item.desplazamiento,
                            activo = item.activo
                        })
                    .ToListAsync(cancellationToken);

            contenido.conversionesMateriaOrganica =
                await db.Set<MateriaOrganicaUnidadMedida>()
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.orden)
                    .Select(item =>
                        new MotorConversionMateriaOrganicaDto
                        {
                            unidadMedidaId =
                                item.unidadMedidaId,
                            codigoFormulaConversion =
                                item.codigoFormulaConversion,
                            factorPrincipal =
                                item.factorPrincipal,
                            factorSecundario =
                                item.factorSecundario,
                            factorTerciario =
                                item.factorTerciario,
                            divisor = item.divisor,
                            desplazamiento =
                                item.desplazamiento,
                            activo = item.activo
                        })
                    .ToListAsync(cancellationToken);

            contenido.parametrosExtraccion =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.elementoQuimicosId)
                    .Select(item => new MotorExtraccionDto
                    {
                        elementoQuimicosId =
                            item.elementoQuimicosId,
                        cantidadExtraidaPorQQOro =
                            item.cantidadExtraidaPorQQOro,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.rangosCultivo =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.tipoCultivoId)
                    .ThenBy(item => item.elementoQuimicosId)
                    .Select(item => new MotorRangoCultivoDto
                    {
                        tipoCultivoId = item.tipoCultivoId,
                        elementoQuimicosId =
                            item.elementoQuimicosId,
                        valorMinimo = item.valorMinimo,
                        valorMaximo = item.valorMaximo,
                        unidadBase = item.unidadBase,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            List<int> fuentesEnmiendaIds =
                await db.ParametroEnmiendaCalcarea
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .Select(item => item.fuenteNutrientesId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

            contenido.fuentesFertilizacionMixtaIds =
                await db.fuenteFertilizacionMixta
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .Select(item => item.fuenteNutrientesId)
                    .Distinct()
                    .OrderBy(item => item)
                    .ToListAsync(cancellationToken);

            contenido.fuentesNutrientes =
                await db.fuenteNutriente
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.nombreNutriente)
                    .Select(item => new MotorFuenteNutrienteDto
                    {
                        fuenteNutrientesId =
                            item.fuenteNutrientesId,
                        nombreNutriente =
                            item.nombreNutriente,
                        descripcionNutriente =
                            item.descripcionNutriente,
                        precioNutriente =
                            item.precioNutriente,
                        habilitadaEnmiendaCalcarea =
                            fuentesEnmiendaIds.Contains(
                                item.fuenteNutrientesId),
                        habilitadaFertilizacionMixta =
                            contenido
                                .fuentesFertilizacionMixtaIds
                                .Contains(
                                    item.fuenteNutrientesId),
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.aportesFuentes =
                await db.fuenteNutrienteElementoQuimico
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        item.fuenteNutriente != null &&
                        item.fuenteNutriente.activo &&
                        item.elementoQuimico != null &&
                        item.elementoQuimico.activo)
                    .OrderBy(item => item.fuenteNutrientesId)
                    .ThenBy(item => item.elementoQuimicosId)
                    .Select(item => new MotorFuenteAporteDto
                    {
                        fuenteNutrienteElementoQuimicoId =
                            item.fuenteNutrienteElementoQuimicoId,
                        fuenteNutrientesId =
                            item.fuenteNutrientesId,
                        elementoQuimicosId =
                            item.elementoQuimicosId,
                        cantidadAporte =
                            item.cantidadAporte,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            contenido.parametrosEnmiendaCalcarea =
                await db.ParametroEnmiendaCalcarea
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        item.FuenteNutriente.activo)
                    .OrderBy(item => item.fuenteNutrientesId)
                    .Select(item => new MotorParametroEnmiendaDto
                    {
                        parametroEnmiendaCalcareaId =
                            item.parametroEnmiendaCalcareaId,
                        fuenteNutrientesId =
                            item.fuenteNutrientesId,
                        saturacionBasesDeseada =
                            item.saturacionBasesDeseada,
                        prnt = item.prnt,
                        factorTonHaALbHa =
                            item.factorTonHaALbHa,
                        factorHaAMz =
                            item.factorHaAMz,
                        factorTonHaAKgHa =
                            item.factorTonHaAKgHa,
                        descripcionParametro =
                            item.descripcionParametro,
                        activo = item.activo
                    })
                    .ToListAsync(cancellationToken);

            string contenidoJson =
                JsonSerializer.Serialize(
                    contenido,
                    HashJsonOptions);

            string hash =
                Convert.ToHexString(
                        SHA256.HashData(
                            Encoding.UTF8.GetBytes(
                                contenidoJson)))
                    .ToLowerInvariant();

            return new MotorCalculoPaqueteDto
            {
                versionEsquema = 2,
                versionMotorBase = "2.0.0",
                versionPaquete =
                    $"motor-completo-{hash[..16]}",
                hashSha256 = hash,
                fechaGeneracionUtc = DateTime.UtcNow,
                versionMinimaAplicacion = "1.0.0",
                modulos = new MotorCalculoModulosDto
                {
                    requerimientoAnual = true,
                    enmiendaCalcarea = true,
                    balanceFormula = true,
                    fertilizacionMixta = true,
                    guardadoLocal = true,
                    sincronizacion = true,
                    reportePdfLocal = true
                },
                contenido = contenido
            };
        }
    }
}
