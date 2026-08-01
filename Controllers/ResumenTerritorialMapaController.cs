using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.ResumenTerritorialMapaDto;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Resumen territorial del Centro Geoespacial administrativo.
///
/// No se utiliza en el portal privado del propietario.
/// </summary>
[ApiController]
[Authorize]
[Route("api/centro-geoespacial/resumen-territorial")]
public sealed class ResumenTerritorialMapaController : ControllerBase
{
    private const string PermisoMapa =
        "MapaTerrenosWeb";

    private const string PermisoTerreno =
        "terrenoPage";

    private readonly DBContext db;
    private readonly UmbralesAlertasService umbralesService;
    private readonly PermisoApiService permisos;

    public ResumenTerritorialMapaController(
        DBContext db,
        UmbralesAlertasService umbralesService,
        PermisoApiService permisos)
    {
        this.db = db;
        this.umbralesService = umbralesService;
        this.permisos = permisos;
    }

    [HttpGet]
    public async Task<IActionResult> Obtener(
        [FromQuery] int? departamentoId = null,
        [FromQuery] int? municipioId = null,
        [FromQuery] string? nivel = null,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso =
            await ValidarLecturaAsync(
                cancellationToken);

        if (acceso is not null)
            return acceso;

        var servicio =
            new ResumenTerritorialMapaService(
                db,
                umbralesService);

        RespuestaDto respuesta =
            await servicio.ObtenerAsync(
                departamentoId,
                municipioId,
                nivel,
                cancellationToken);

        return Ok(respuesta);
    }

    private async Task<IActionResult?> ValidarLecturaAsync(
        CancellationToken cancellationToken)
    {
        int? usuarioId =
            ObtenerUsuarioId();

        ResultadoPermisoApi mapa =
            await permisos.ValidarAsync(
                usuarioId,
                PermisoMapa,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (mapa.Permitido)
            return null;

        ResultadoPermisoApi terreno =
            await permisos.ValidarAsync(
                usuarioId,
                PermisoTerreno,
                TipoPermisoApi.Leer,
                cancellationToken);

        return terreno.Permitido
            ? null
            : StatusCode(
                mapa.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene permiso para consultar el resumen territorial."
                });
    }

    private int? ObtenerUsuarioId()
    {
        string? valor =
            User.FindFirstValue("uid") ??
            User.FindFirstValue(
                ClaimTypes.NameIdentifier) ??
            Request.Headers["X-Usuario-Id"]
                .FirstOrDefault();

        return int.TryParse(
                valor,
                out int usuarioId) &&
               usuarioId > 0
            ? usuarioId
            : null;
    }
}
