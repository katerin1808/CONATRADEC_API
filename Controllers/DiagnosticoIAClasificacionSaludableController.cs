using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Corrige de forma determinística la clasificación del Álbum Botánico
    /// para fotografías que Gemini identificó como plantas de café
    /// aparentemente sanas.
    ///
    /// La categoría fitosanitaria continúa siendo NO_APLICA. Este controlador
    /// únicamente separa ese resultado de la clasificación editorial del
    /// Álbum Botánico, donde sí corresponde utilizar el capítulo Plantas sanas.
    /// No crea categorías ni fichas automáticamente.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia-clasificacion")]
    public sealed class DiagnosticoIAClasificacionSaludableController :
        ControllerBase
    {
        private const string CategoriaPredeterminada =
            "Plantas sanas";

        private const string FichaPredeterminada =
            "Planta de café aparentemente sana";

        private readonly DiagnosticoIADbContext db;

        public DiagnosticoIAClasificacionSaludableController(
            DiagnosticoIADbContext db)
        {
            this.db = db;
        }

        [HttpPost("{diagnosticoId:int}/normalizar-plantas-sanas")]
        public async Task<IActionResult> NormalizarPlantasSanas(
            int diagnosticoId,
            CancellationToken cancellationToken = default)
        {
            if (diagnosticoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La inspección seleccionada no es válida."
                });
            }

            DiagnosticoIA? diagnostico = await db.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == diagnosticoId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró la inspección fitosanitaria."
                });
            }

            List<DiagnosticoIAImagenResultadoIA> resultadosSanos =
                diagnostico.Imagenes
                    .Where(item => item.ResultadoIA != null)
                    .Select(item => item.ResultadoIA!)
                    .Where(EsResultadoAparentementeSano)
                    .ToList();

            if (resultadosSanos.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    message =
                        "La inspección no contiene resultados aparentemente sanos pendientes de normalización.",
                    data = new
                    {
                        fotografiasRevisadas = 0,
                        fotografiasActualizadas = 0,
                        fichaExistenteEncontrada = false
                    }
                });
            }

            List<GeminiCategoriaAlbum> categorias =
                await db.CategoriasAlbum
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderBy(item => item.NombreCategoria)
                    .Select(item => new GeminiCategoriaAlbum
                    {
                        CategoriaAlbumBotanicoId =
                            item.CategoriaAlbumBotanicoId,
                        NombreCategoria = item.NombreCategoria
                    })
                    .ToListAsync(cancellationToken);

            GeminiCategoriaAlbum? categoriaSana = categorias
                .FirstOrDefault(item =>
                    EsNombreCategoriaPlantasSanas(
                        item.NombreCategoria));

            GeminiRegistroAlbum? fichaExistente = null;

            if (categoriaSana != null)
            {
                List<GeminiRegistroAlbum> fichas =
                    await db.RegistrosAlbum
                        .AsNoTracking()
                        .Where(item =>
                            item.Activo &&
                            item.CategoriaAlbumBotanicoId ==
                                categoriaSana.CategoriaAlbumBotanicoId)
                        .OrderBy(item => item.Titulo)
                        .Select(item => new GeminiRegistroAlbum
                        {
                            AlbumBotanicoCafeId =
                                item.AlbumBotanicoCafeId,
                            CategoriaAlbumBotanicoId =
                                item.CategoriaAlbumBotanicoId,
                            Titulo = item.Titulo,
                            NombreCientifico =
                                item.NombreCientifico ?? string.Empty,
                            Descripcion = item.Descripcion,
                            Sintomas = item.Sintomas ?? string.Empty
                        })
                        .ToListAsync(cancellationToken);

                fichaExistente = fichas.FirstOrDefault(item =>
                    EsFichaGeneralDePlantaSana(item.Titulo));
            }

            int actualizadas = 0;

            foreach (DiagnosticoIAImagenResultadoIA resultado
                     in resultadosSanos)
            {
                if (!PuedeNormalizarse(resultado))
                    continue;

                bool cambio = fichaExistente != null
                    ? AplicarFichaExistente(
                        resultado,
                        categoriaSana!,
                        fichaExistente)
                    : AplicarPropuestaNueva(
                        resultado,
                        categoriaSana);

                if (cambio)
                    actualizadas++;
            }

            if (actualizadas > 0)
                await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = actualizadas == 0
                    ? "La clasificación de plantas sanas ya estaba actualizada."
                    : fichaExistente != null
                        ? "Las fotografías aparentemente sanas se vincularon con una ficha activa del Álbum Botánico."
                        : categoriaSana != null
                            ? "Las fotografías aparentemente sanas quedaron propuestas dentro del capítulo Plantas sanas."
                            : "Las fotografías aparentemente sanas quedaron marcadas para crear el capítulo y la ficha correspondientes.",
                data = new
                {
                    fotografiasRevisadas = resultadosSanos.Count,
                    fotografiasActualizadas = actualizadas,
                    categoriaAlbumBotanicoId =
                        categoriaSana?.CategoriaAlbumBotanicoId,
                    albumBotanicoCafeId =
                        fichaExistente?.AlbumBotanicoCafeId,
                    fichaExistenteEncontrada =
                        fichaExistente != null
                }
            });
        }

        private static bool EsResultadoAparentementeSano(
            DiagnosticoIAImagenResultadoIA resultado)
        {
            if (string.Equals(
                    resultado.EstadoGeneral,
                    DiagnosticoIAFlujo.EstadoGeneral.Sana,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool categoriaNoAplica = string.Equals(
                resultado.CategoriaPrincipal,
                DiagnosticoIAFlujo.Categoria.NoAplica,
                StringComparison.OrdinalIgnoreCase);

            string diagnostico =
                resultado.DiagnosticoProbable ?? string.Empty;

            bool diagnosticoSano =
                diagnostico.Contains(
                    "aparentemente sana",
                    StringComparison.OrdinalIgnoreCase) ||
                diagnostico.Contains(
                    "aparentemente sano",
                    StringComparison.OrdinalIgnoreCase);

            return categoriaNoAplica && diagnosticoSano;
        }

        private static bool PuedeNormalizarse(
            DiagnosticoIAImagenResultadoIA resultado)
        {
            if (resultado.AlbumBotanicoCafeIdSeleccionado is > 0 ||
                resultado.CategoriaAlbumBotanicoIdSeleccionada is > 0)
            {
                return false;
            }

            string estado =
                resultado.EstadoClasificacionAlbum ?? string.Empty;

            return string.IsNullOrWhiteSpace(estado) ||
                string.Equals(
                    estado,
                    DiagnosticoIAFlujo.ClasificacionAlbum.NoAplica,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    estado,
                    DiagnosticoIAFlujo.ClasificacionAlbum.PendienteAnalizador,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    estado,
                    DiagnosticoIAFlujo.ClasificacionAlbum.PendienteDecisionTecnico,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool AplicarFichaExistente(
            DiagnosticoIAImagenResultadoIA resultado,
            GeminiCategoriaAlbum categoria,
            GeminiRegistroAlbum ficha)
        {
            bool cambio =
                resultado.CategoriaAlbumBotanicoIdSugerida !=
                    categoria.CategoriaAlbumBotanicoId ||
                resultado.AlbumBotanicoCafeIdSugerido !=
                    ficha.AlbumBotanicoCafeId ||
                !resultado.CoincideCatalogoAlbum ||
                resultado.RequiereDecisionClasificacion ||
                !string.Equals(
                    resultado.EstadoClasificacionAlbum,
                    DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaAutomatica,
                    StringComparison.OrdinalIgnoreCase);

            resultado.CategoriaAlbumBotanicoIdSugerida =
                categoria.CategoriaAlbumBotanicoId;
            resultado.AlbumBotanicoCafeIdSugerido =
                ficha.AlbumBotanicoCafeId;
            resultado.CategoriaAlbumSugerida =
                Limitar(categoria.NombreCategoria, 150);
            resultado.ClasificacionAlbumSugerida =
                Limitar(ficha.Titulo, 200);
            resultado.NombreCientificoSugerido =
                Limitar(ficha.NombreCientifico, 200);
            resultado.CoincideCatalogoAlbum = true;
            resultado.RequiereDecisionClasificacion = false;
            resultado.EstadoClasificacionAlbum =
                DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaAutomatica;
            resultado.MotivoClasificacionAlbum =
                "La fotografía fue identificada como una planta de café aparentemente sana y coincide con una ficha activa del capítulo Plantas sanas.";

            return cambio;
        }

        private static bool AplicarPropuestaNueva(
            DiagnosticoIAImagenResultadoIA resultado,
            GeminiCategoriaAlbum? categoria)
        {
            string nombreCategoria =
                categoria?.NombreCategoria ?? CategoriaPredeterminada;

            int? categoriaId =
                categoria?.CategoriaAlbumBotanicoId;

            bool cambio =
                resultado.CategoriaAlbumBotanicoIdSugerida != categoriaId ||
                resultado.AlbumBotanicoCafeIdSugerido.HasValue ||
                resultado.CoincideCatalogoAlbum ||
                !resultado.RequiereDecisionClasificacion ||
                !string.Equals(
                    resultado.EstadoClasificacionAlbum,
                    DiagnosticoIAFlujo.ClasificacionAlbum.PendienteAnalizador,
                    StringComparison.OrdinalIgnoreCase);

            resultado.CategoriaAlbumBotanicoIdSugerida = categoriaId;
            resultado.AlbumBotanicoCafeIdSugerido = null;
            resultado.CategoriaAlbumSugerida =
                Limitar(nombreCategoria, 150);
            resultado.ClasificacionAlbumSugerida =
                FichaPredeterminada;
            resultado.CoincideCatalogoAlbum = false;
            resultado.RequiereDecisionClasificacion = true;
            resultado.EstadoClasificacionAlbum =
                DiagnosticoIAFlujo.ClasificacionAlbum.PendienteAnalizador;
            resultado.MotivoClasificacionAlbum = categoria == null
                ? "La fotografía corresponde a una planta de café aparentemente sana, pero no existe un capítulo activo llamado Plantas sanas. El analizador debe proponer la clasificación y el aprobador decidirá su incorporación."
                : "La fotografía corresponde a una planta de café aparentemente sana, pero no existe una ficha compatible dentro del capítulo Plantas sanas.";

            return cambio;
        }

        private static bool EsNombreCategoriaPlantasSanas(
            string? nombre)
        {
            string normalizado = NormalizarComparacion(nombre);

            return normalizado is
                "PLANTASSANAS" or
                "PLANTASANA" or
                "CAFETOSSANOS" or
                "CAFETOSANO";
        }

        private static bool EsFichaGeneralDePlantaSana(
            string? titulo)
        {
            string normalizado = NormalizarComparacion(titulo);

            return normalizado is
                "PLANTADECAFEAPARENTEMENTESANA" or
                "PLANTADECAFESANA" or
                "PLANTAAPARENTEMENTESANA" or
                "PLANTASANA" or
                "CAFETOAPARENTEMENTESANO" or
                "CAFETOSANO";
        }

        private static string NormalizarComparacion(string? valor)
        {
            string descompuesto = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(descompuesto.Length);

            foreach (char caracter in descompuesto)
            {
                UnicodeCategory categoria =
                    CharUnicodeInfo.GetUnicodeCategory(caracter);

                if (categoria != UnicodeCategory.NonSpacingMark &&
                    char.IsLetterOrDigit(caracter))
                {
                    builder.Append(caracter);
                }
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC);
        }

        private static string Limitar(
            string? valor,
            int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();

            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }
    }
}
