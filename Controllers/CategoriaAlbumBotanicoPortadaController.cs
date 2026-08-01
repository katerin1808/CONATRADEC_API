using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Complementa la administración de categorías del álbum permitiendo quitar
/// una portada sin eliminar ni desactivar la categoría.
/// </summary>
[ApiController]
[Route("api/categoria-album-botanico")]
public sealed class CategoriaAlbumBotanicoPortadaController : ControllerBase
{
    private readonly DBContext context;
    private readonly ImageService imageService;
    private readonly ILogger<CategoriaAlbumBotanicoPortadaController> logger;

    public CategoriaAlbumBotanicoPortadaController(
        DBContext context,
        ImageService imageService,
        ILogger<CategoriaAlbumBotanicoPortadaController> logger)
    {
        this.context = context;
        this.imageService = imageService;
        this.logger = logger;
    }

    // DELETE: api/categoria-album-botanico/1/portada
    [HttpDelete("{id:int}/portada")]
    public async Task<ActionResult> EliminarPortada(int id)
    {
        var categoria = await context
            .CategoriasAlbumBotanico
            .FirstOrDefaultAsync(x =>
                x.categoriaAlbumBotanicoId == id);

        if (categoria is null)
        {
            return NotFound(new
            {
                success = false,
                message = "La categoría no fue encontrada."
            });
        }

        if (string.IsNullOrWhiteSpace(
                categoria.rutaImagenPortada))
        {
            return Ok(new
            {
                success = true,
                message = "La categoría ya se encuentra sin portada."
            });
        }

        string rutaAnterior =
            categoria.rutaImagenPortada;

        /*
         * Primero se elimina la referencia en la base de datos. De esta forma,
         * aunque falle la limpieza física del archivo, el portal y las
         * aplicaciones dejan de utilizar inmediatamente la portada.
         */
        categoria.rutaImagenPortada = null;
        await context.SaveChangesAsync();

        try
        {
            imageService.EliminarImagen(
                rutaAnterior);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "La portada de la categoría {CategoriaId} fue desvinculada, " +
                "pero el archivo físico {Ruta} no pudo eliminarse.",
                id,
                rutaAnterior);
        }

        return Ok(new
        {
            success = true,
            message = "Portada de la categoría eliminada correctamente."
        });
    }
}
