using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.CentroGeoespacialDto;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Portal privado del propietario.
///
/// Nunca recibe propietarioId desde la interfaz. La identidad se obtiene
/// del JWT y se resuelve mediante usuarioPropietario y propietarioTerreno.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portal-propietario")]
public sealed class PortalPropietarioController : ControllerBase
{
    private const string NivelCritico = "CRITICO";
    private const string NivelAtencion = "ATENCION";
    private const string NivelEstable = "ESTABLE";
    private const string NivelSinAnalisis = "SIN_ANALISIS";

    private readonly DBContext db;
    private readonly PermisoApiService permisos;
    private readonly ClimaMapaService climaService;
    private readonly UmbralesAlertasService umbralesService;

    public PortalPropietarioController(
        DBContext db,
        PermisoApiService permisos,
        ClimaMapaService climaService,
        UmbralesAlertasService umbralesService)
    {
        this.db = db;
        this.permisos = permisos;
        this.climaService = climaService;
        this.umbralesService = umbralesService;
    }

    [HttpGet("mi-resumen")]
    public async Task<IActionResult> ObtenerMiResumen(
        CancellationToken cancellationToken = default)
    {
        int? usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
            return RespuestaNoAutorizada();

        IActionResult? acceso =
            await ValidarAccesoPortalPropietarioAsync(
                usuarioId.Value,
                cancellationToken);

        if (acceso is not null)
            return acceso;

        PortalPropietarioDatosDto? propietario =
            await ObtenerPropietarioAsync(
                usuarioId.Value,
                cancellationToken);

        if (propietario is null)
        {
            return Ok(new PortalPropietarioResumenDto
            {
                Vinculado = false,
                Mensaje =
                    "Su cuenta tiene acceso al portal, pero todavía no " +
                    "se encuentra vinculada con un propietario."
            });
        }

        List<PortalPropietarioTerrenoDto> terrenos =
            await ObtenerTerrenosResumenAsync(
                propietario.PropietarioId,
                cancellationToken);

        return Ok(new PortalPropietarioResumenDto
        {
            Vinculado = true,
            Mensaje =
                terrenos.Count == 0
                    ? "El propietario está vinculado, pero todavía no " +
                      "tiene terrenos activos asociados."
                    : "Información cargada correctamente.",
            Propietario = propietario,
            Resumen = new PortalPropietarioTotalesDto
            {
                TotalTerrenos =
                    terrenos.Count,
                TotalManzanas =
                    terrenos.Sum(x =>
                        x.ExtensionManzanas),
                TotalPlantas =
                    terrenos.Sum(x =>
                        x.CantidadPlantas),
                ProduccionEstimadaQuintales =
                    terrenos.Sum(x =>
                        x.CantidadQuintalesOro),
                TotalAnalisis =
                    terrenos.Sum(x =>
                        x.TotalAnalisis)
            },
            Terrenos = terrenos
        });
    }

    [HttpGet("mi-centro-geoespacial")]
    public async Task<IActionResult> ObtenerMiCentroGeoespacial(
        [FromQuery] bool forzarClima = false,
        CancellationToken cancellationToken = default)
    {
        int? usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
            return RespuestaNoAutorizada();

        IActionResult? acceso =
            await ValidarAccesoPortalPropietarioAsync(
                usuarioId.Value,
                cancellationToken);

        if (acceso is not null)
            return acceso;

        PortalPropietarioDatosDto? propietario =
            await ObtenerPropietarioAsync(
                usuarioId.Value,
                cancellationToken);

        if (propietario is null)
        {
            return Ok(new PortalCentroGeoespacialDto
            {
                Vinculado = false,
                Mensaje =
                    "Su cuenta todavía no se encuentra vinculada " +
                    "con un propietario."
            });
        }

        List<TerrenoCentroBase> terrenosBase =
            await ObtenerTerrenosCentroAsync(
                propietario.PropietarioId,
                cancellationToken);

        List<int> calculoIds = terrenosBase
            .Where(x =>
                x.AnalisisSueloCalculoId.HasValue)
            .Select(x =>
                x.AnalisisSueloCalculoId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, List<PortalCentroElementoDto>>
            elementosPorCalculo =
                await ObtenerElementosPorCalculoAsync(
                    calculoIds,
                    cancellationToken);

        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(
                cancellationToken);

        var terrenos =
            new List<PortalCentroTerrenoDto>();

        foreach (TerrenoCentroBase item in terrenosBase)
        {
            string nivel =
                CalcularNivel(
                    item.Ph,
                    item.MateriaOrganica,
                    item.AcidezTotal,
                    umbrales);

            List<PortalCentroElementoDto> elementos = [];

            if (item.AnalisisSueloCalculoId.HasValue &&
                elementosPorCalculo.TryGetValue(
                    item.AnalisisSueloCalculoId.Value,
                    out List<PortalCentroElementoDto>? encontrados))
            {
                elementos = encontrados;
            }

            terrenos.Add(new PortalCentroTerrenoDto
            {
                TerrenoId =
                    item.TerrenoId,
                CodigoTerreno =
                    item.CodigoTerreno,
                Direccion =
                    item.Direccion,
                ExtensionManzanas =
                    item.ExtensionManzanas,
                FechaIngreso =
                    item.FechaIngreso,
                CantidadPlantas =
                    item.CantidadPlantas,
                CantidadQuintalesOro =
                    item.CantidadQuintalesOro,
                Latitud =
                    item.Latitud,
                Longitud =
                    item.Longitud,
                MunicipioId =
                    item.MunicipioId,
                Municipio =
                    item.Municipio,
                DepartamentoId =
                    item.DepartamentoId,
                Departamento =
                    item.Departamento,
                TotalAnalisis =
                    item.TotalAnalisis,
                AnalisisSueloCalculoId =
                    item.AnalisisSueloCalculoId,
                FechaUltimoAnalisis =
                    item.FechaUltimoAnalisis,
                Ph =
                    item.Ph,
                MateriaOrganica =
                    item.MateriaOrganica,
                AcidezTotal =
                    item.AcidezTotal,
                Cice =
                    item.Cice,
                SaturacionBases =
                    item.SaturacionBases,
                RecomendacionGeneral =
                    item.RecomendacionGeneral,
                Observacion =
                    item.Observacion,
                Nivel =
                    nivel,
                Estado =
                    EstadoTexto(nivel),
                ColorEstado =
                    ColorNivel(nivel),
                Elementos =
                    elementos,
                Alertas =
                    ConstruirAlertas(
                        item,
                        umbrales)
            });
        }

        ClimaMapaRespuestaDto clima =
            await climaService.ObtenerAsync(
                forzarClima,
                cancellationToken);

        List<ClimaPuntoMapaDto> climaLocal =
            SeleccionarClimaLocal(
                terrenos,
                clima.Puntos);

        var nutrientes =
            terrenos
                .SelectMany(x =>
                    x.Elementos)
                .GroupBy(x =>
                    x.ElementoQuimicosId)
                .Select(grupo =>
                {
                    PortalCentroElementoDto primero =
                        grupo.First();

                    return new PortalCentroNutrienteDto
                    {
                        ElementoQuimicosId =
                            grupo.Key,
                        Simbolo =
                            primero.Simbolo,
                        Nombre =
                            primero.Nombre
                    };
                })
                .OrderBy(x =>
                    x.Nombre)
                .ToList();

        return Ok(new PortalCentroGeoespacialDto
        {
            Vinculado = true,
            Mensaje =
                terrenos.Count == 0
                    ? "El propietario no tiene terrenos activos vinculados."
                    : "Centro geoespacial cargado correctamente.",
            ActualizadoUtc =
                DateTime.UtcNow,
            Propietario =
                propietario,
            Terrenos =
                terrenos,
            NutrientesDisponibles =
                nutrientes,
            Clima =
                clima,
            Resumen =
                ConstruirResumen(
                    terrenos,
                    climaLocal)
        });
    }

    [HttpGet("terrenos/{terrenoId:int}/historial")]
    public async Task<IActionResult> ObtenerHistorialTerreno(
        int terrenoId,
        [FromQuery] int limite = 20,
        CancellationToken cancellationToken = default)
    {
        int? usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
            return RespuestaNoAutorizada();

        IActionResult? acceso =
            await ValidarAccesoPortalPropietarioAsync(
                usuarioId.Value,
                cancellationToken);

        if (acceso is not null)
            return acceso;

        limite = Math.Clamp(
            limite,
            1,
            50);

        TerrenoHistorialBase? terreno =
            await ObtenerTerrenoAutorizadoAsync(
                usuarioId.Value,
                terrenoId,
                cancellationToken);

        if (terreno is null)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "El terreno no pertenece al propietario vinculado " +
                    "con la cuenta autenticada."
            });
        }

        List<AnalisisHistorialBase> registros =
            await ObtenerAnalisisHistorialAsync(
                usuarioId.Value,
                terrenoId,
                limite,
                cancellationToken);

        List<int> calculoIds = registros
            .Select(x =>
                x.AnalisisSueloCalculoId)
            .Distinct()
            .ToList();

        Dictionary<int, List<PortalCentroElementoDto>>
            elementosPorCalculo =
                await ObtenerElementosPorCalculoAsync(
                    calculoIds,
                    cancellationToken);

        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(
                cancellationToken);

        var respuesta =
            new PortalHistorialTerrenoDto
            {
                TerrenoId =
                    terreno.TerrenoId,
                CodigoTerreno =
                    terreno.CodigoTerreno,
                Direccion =
                    terreno.Direccion,
                Municipio =
                    terreno.Municipio,
                Departamento =
                    terreno.Departamento,
                ExtensionManzanas =
                    terreno.ExtensionManzanas,
                ProduccionQuintalesOro =
                    terreno.ProduccionQuintalesOro,
                Analisis =
                    registros.Select(item =>
                    {
                        string nivel =
                            CalcularNivel(
                                item.Ph,
                                item.MateriaOrganica,
                                item.AcidezTotal,
                                umbrales);

                        elementosPorCalculo.TryGetValue(
                            item.AnalisisSueloCalculoId,
                            out List<PortalCentroElementoDto>?
                                elementos);

                        return new PortalHistorialAnalisisDto
                        {
                            AnalisisSueloCalculoId =
                                item.AnalisisSueloCalculoId,
                            AnalisisSueloId =
                                item.AnalisisSueloId,
                            Identificador =
                                item.Identificador,
                            FechaLaboratorio =
                                item.FechaLaboratorio,
                            FechaRegistro =
                                item.FechaRegistro,
                            Ph =
                                item.Ph,
                            MateriaOrganica =
                                item.MateriaOrganica,
                            AcidezTotal =
                                item.AcidezTotal,
                            Cice =
                                item.Cice,
                            SaturacionBases =
                                item.SaturacionBases,
                            Nivel =
                                nivel,
                            Estado =
                                EstadoTexto(nivel),
                            RecomendacionGeneral =
                                item.RecomendacionGeneral,
                            Observacion =
                                item.Observacion,
                            Elementos =
                                elementos ?? []
                        };
                    })
                    .ToList()
            };

        return Ok(respuesta);
    }

    private async Task<PortalPropietarioDatosDto?>
        ObtenerPropietarioAsync(
            int usuarioId,
            CancellationToken cancellationToken)
    {
        return await ConsultarUnoAsync(
            """
            SELECT TOP (1)
                p.propietarioId,
                p.identificacion,
                p.nombreCompleto,
                p.telefono,
                p.correo,
                p.direccion
            FROM dbo.usuarioPropietario up
            INNER JOIN dbo.propietario p
                ON p.propietarioId = up.propietarioId
            WHERE up.usuarioId = @usuarioId
              AND up.activo = 1
              AND p.activo = 1
            ORDER BY up.fechaAsignacionUtc DESC;
            """,
            command =>
                AgregarParametro(
                    command,
                    "@usuarioId",
                    usuarioId),
            reader => new PortalPropietarioDatosDto
            {
                PropietarioId =
                    reader.GetInt32(0),
                Identificacion =
                    Texto(reader, 1),
                NombreCompleto =
                    Texto(reader, 2),
                Telefono =
                    TextoNullable(reader, 3),
                Correo =
                    TextoNullable(reader, 4),
                Direccion =
                    TextoNullable(reader, 5)
            },
            cancellationToken);
    }

    private async Task<List<PortalPropietarioTerrenoDto>>
        ObtenerTerrenosResumenAsync(
            int propietarioId,
            CancellationToken cancellationToken)
    {
        return await ConsultarListaAsync(
            """
            SELECT
                t.terrenoId,
                t.codigoTerreno,
                t.direccionTerreno,
                t.extensionManzanaTerreno,
                t.fechaIngresoTerreno,
                t.cantidadPlantasTerreno,
                t.cantidadQuintalesOro,
                t.latitud,
                t.longitud,
                ISNULL(m.nombreMunicipio, N''),
                ISNULL(d.nombreDepartamento, N''),
                (
                    SELECT COUNT(1)
                    FROM dbo.analisisSueloCalculo calculo
                    WHERE calculo.terrenoId = t.terrenoId
                      AND calculo.activo = 1
                ) AS totalAnalisis,
                (
                    SELECT MAX(calculo.fechaCalculo)
                    FROM dbo.analisisSueloCalculo calculo
                    WHERE calculo.terrenoId = t.terrenoId
                      AND calculo.activo = 1
                ) AS fechaUltimoAnalisis
            FROM dbo.propietarioTerreno pt
            INNER JOIN dbo.terreno t
                ON t.terrenoId = pt.terrenoId
            LEFT JOIN dbo.municipio m
                ON m.municipioId = t.municipioId
            LEFT JOIN dbo.departamento d
                ON d.departamentoId = m.departamentoId
            WHERE pt.propietarioId = @propietarioId
              AND pt.activo = 1
              AND t.activo = 1
            ORDER BY t.codigoTerreno;
            """,
            command =>
                AgregarParametro(
                    command,
                    "@propietarioId",
                    propietarioId),
            reader => new PortalPropietarioTerrenoDto
            {
                TerrenoId =
                    reader.GetInt32(0),
                CodigoTerreno =
                    Texto(reader, 1),
                Direccion =
                    Texto(reader, 2),
                ExtensionManzanas =
                    Decimal(reader, 3),
                FechaIngreso =
                    Fecha(reader, 4),
                CantidadPlantas =
                    Entero(reader, 5),
                CantidadQuintalesOro =
                    Decimal(reader, 6),
                Latitud =
                    Decimal(reader, 7),
                Longitud =
                    Decimal(reader, 8),
                Municipio =
                    Texto(reader, 9),
                Departamento =
                    Texto(reader, 10),
                TotalAnalisis =
                    Entero(reader, 11),
                FechaUltimoAnalisis =
                    FechaNullable(reader, 12)
            },
            cancellationToken);
    }

    private async Task<List<TerrenoCentroBase>>
        ObtenerTerrenosCentroAsync(
            int propietarioId,
            CancellationToken cancellationToken)
    {
        return await ConsultarListaAsync(
            """
            SELECT
                t.terrenoId,
                t.codigoTerreno,
                t.direccionTerreno,
                t.extensionManzanaTerreno,
                t.fechaIngresoTerreno,
                t.cantidadPlantasTerreno,
                t.cantidadQuintalesOro,
                t.latitud,
                t.longitud,
                m.municipioId,
                ISNULL(m.nombreMunicipio, N''),
                d.departamentoId,
                ISNULL(d.nombreDepartamento, N''),
                (
                    SELECT COUNT(1)
                    FROM dbo.analisisSueloCalculo total
                    WHERE total.terrenoId = t.terrenoId
                      AND total.activo = 1
                ) AS totalAnalisis,
                ultimo.analisisSueloCalculoId,
                ultimo.fechaCalculo,
                ultimo.phAnalisisSuelo,
                ultimo.materiaOrganica,
                ultimo.acidezTotal,
                ultimo.recomendacionGeneral,
                ultimo.observacion,
                enmienda.cice,
                enmienda.saturacionActual
            FROM dbo.propietarioTerreno pt
            INNER JOIN dbo.terreno t
                ON t.terrenoId = pt.terrenoId
            INNER JOIN dbo.municipio m
                ON m.municipioId = t.municipioId
            INNER JOIN dbo.departamento d
                ON d.departamentoId = m.departamentoId
            OUTER APPLY
            (
                SELECT TOP (1)
                    calculo.analisisSueloCalculoId,
                    calculo.fechaCalculo,
                    calculo.phAnalisisSuelo,
                    calculo.materiaOrganica,
                    calculo.acidezTotal,
                    calculo.recomendacionGeneral,
                    calculo.observacion
                FROM dbo.analisisSueloCalculo calculo
                INNER JOIN dbo.analisisSuelo analisis
                    ON analisis.analisisSueloId =
                       calculo.analisisSueloId
                WHERE calculo.terrenoId = t.terrenoId
                  AND calculo.activo = 1
                  AND analisis.activo = 1
                ORDER BY
                    calculo.fechaCalculo DESC,
                    calculo.analisisSueloCalculoId DESC
            ) ultimo
            OUTER APPLY
            (
                SELECT TOP (1)
                    resultado.cice,
                    resultado.saturacionActual
                FROM dbo.enmiendaCalcarea resultado
                WHERE resultado.activo = 1
                  AND resultado.analisisSueloCalculoId =
                      ultimo.analisisSueloCalculoId
                ORDER BY
                    resultado.fechaCreacion DESC,
                    resultado.enmiendaCalcareaId DESC
            ) enmienda
            WHERE pt.propietarioId = @propietarioId
              AND pt.activo = 1
              AND t.activo = 1
            ORDER BY t.codigoTerreno;
            """,
            command =>
                AgregarParametro(
                    command,
                    "@propietarioId",
                    propietarioId),
            reader => new TerrenoCentroBase
            {
                TerrenoId =
                    reader.GetInt32(0),
                CodigoTerreno =
                    Texto(reader, 1),
                Direccion =
                    Texto(reader, 2),
                ExtensionManzanas =
                    Decimal(reader, 3),
                FechaIngreso =
                    Fecha(reader, 4),
                CantidadPlantas =
                    Entero(reader, 5),
                CantidadQuintalesOro =
                    Decimal(reader, 6),
                Latitud =
                    Decimal(reader, 7),
                Longitud =
                    Decimal(reader, 8),
                MunicipioId =
                    Entero(reader, 9),
                Municipio =
                    Texto(reader, 10),
                DepartamentoId =
                    Entero(reader, 11),
                Departamento =
                    Texto(reader, 12),
                TotalAnalisis =
                    Entero(reader, 13),
                AnalisisSueloCalculoId =
                    EnteroNullable(reader, 14),
                FechaUltimoAnalisis =
                    FechaNullable(reader, 15),
                Ph =
                    DecimalNullable(reader, 16),
                MateriaOrganica =
                    DecimalNullable(reader, 17),
                AcidezTotal =
                    DecimalNullable(reader, 18),
                RecomendacionGeneral =
                    Texto(reader, 19),
                Observacion =
                    Texto(reader, 20),
                Cice =
                    DecimalNullable(reader, 21),
                SaturacionBases =
                    DecimalNullable(reader, 22)
            },
            cancellationToken);
    }

    private async Task<Dictionary<int, List<PortalCentroElementoDto>>>
        ObtenerElementosPorCalculoAsync(
            IReadOnlyCollection<int> calculoIds,
            CancellationToken cancellationToken)
    {
        if (calculoIds.Count == 0)
        {
            return new Dictionary<
                int,
                List<PortalCentroElementoDto>>();
        }

        int indice = 0;

        string parametros = string.Join(
            ", ",
            calculoIds.Select(_ =>
                $"@calculo{indice++}"));

        List<ElementoCalculoBase> elementos =
            await ConsultarListaAsync(
                $"""
                SELECT
                    detalle.analisisSueloCalculoId,
                    detalle.elementoQuimicosId,
                    ISNULL(elemento.simboloElementoQuimico, N''),
                    ISNULL(elemento.nombreElementoQuimico, N''),
                    COALESCE(
                        detalle.cantidadConvertidaLbMz,
                        detalle.cantidadIngresada),
                    CASE
                        WHEN detalle.cantidadConvertidaLbMz IS NOT NULL
                            THEN N'lb/Mz'
                        ELSE ISNULL(unidad.nombreUnidadMedida, N'')
                    END,
                    ISNULL(detalle.clasificacion, N'')
                FROM dbo.analisisSueloCalculoElementoQuimico detalle
                INNER JOIN dbo.elementoQuimico elemento
                    ON elemento.elementoQuimicosId =
                       detalle.elementoQuimicosId
                LEFT JOIN dbo.unidadMedida unidad
                    ON unidad.unidadMedidaId =
                       detalle.unidadMedidaId
                WHERE detalle.activo = 1
                  AND detalle.analisisSueloCalculoId IN ({parametros})
                ORDER BY
                    detalle.analisisSueloCalculoId,
                    elemento.nombreElementoQuimico;
                """,
                command =>
                {
                    int posicion = 0;

                    foreach (int calculoId in calculoIds)
                    {
                        AgregarParametro(
                            command,
                            $"@calculo{posicion++}",
                            calculoId);
                    }
                },
                reader => new ElementoCalculoBase
                {
                    AnalisisSueloCalculoId =
                        Entero(reader, 0),
                    Elemento = new PortalCentroElementoDto
                    {
                        ElementoQuimicosId =
                            Entero(reader, 1),
                        Simbolo =
                            Texto(reader, 2),
                        Nombre =
                            Texto(reader, 3),
                        Valor =
                            Decimal(reader, 4),
                        Unidad =
                            Texto(reader, 5),
                        Clasificacion =
                            Texto(reader, 6)
                    }
                },
                cancellationToken);

        return elementos
            .GroupBy(x =>
                x.AnalisisSueloCalculoId)
            .ToDictionary(
                grupo =>
                    grupo.Key,
                grupo =>
                    grupo
                        .Select(x =>
                            x.Elemento)
                        .ToList());
    }

    private async Task<TerrenoHistorialBase?>
        ObtenerTerrenoAutorizadoAsync(
            int usuarioId,
            int terrenoId,
            CancellationToken cancellationToken)
    {
        return await ConsultarUnoAsync(
            """
            SELECT TOP (1)
                t.terrenoId,
                t.codigoTerreno,
                t.direccionTerreno,
                ISNULL(m.nombreMunicipio, N''),
                ISNULL(d.nombreDepartamento, N''),
                t.extensionManzanaTerreno,
                t.cantidadQuintalesOro
            FROM dbo.usuarioPropietario up
            INNER JOIN dbo.propietarioTerreno pt
                ON pt.propietarioId = up.propietarioId
               AND pt.activo = 1
            INNER JOIN dbo.terreno t
                ON t.terrenoId = pt.terrenoId
               AND t.activo = 1
            LEFT JOIN dbo.municipio m
                ON m.municipioId = t.municipioId
            LEFT JOIN dbo.departamento d
                ON d.departamentoId = m.departamentoId
            WHERE up.usuarioId = @usuarioId
              AND up.activo = 1
              AND t.terrenoId = @terrenoId;
            """,
            command =>
            {
                AgregarParametro(
                    command,
                    "@usuarioId",
                    usuarioId);

                AgregarParametro(
                    command,
                    "@terrenoId",
                    terrenoId);
            },
            reader => new TerrenoHistorialBase
            {
                TerrenoId =
                    Entero(reader, 0),
                CodigoTerreno =
                    Texto(reader, 1),
                Direccion =
                    Texto(reader, 2),
                Municipio =
                    Texto(reader, 3),
                Departamento =
                    Texto(reader, 4),
                ExtensionManzanas =
                    Decimal(reader, 5),
                ProduccionQuintalesOro =
                    Decimal(reader, 6)
            },
            cancellationToken);
    }

    private async Task<List<AnalisisHistorialBase>>
        ObtenerAnalisisHistorialAsync(
            int usuarioId,
            int terrenoId,
            int limite,
            CancellationToken cancellationToken)
    {
        return await ConsultarListaAsync(
            """
            SELECT TOP (@limite)
                calculo.analisisSueloCalculoId,
                calculo.analisisSueloId,
                analisis.identificadorAnalisisSuelo,
                analisis.fechaAnalisisSuelo,
                analisis.fechaCreacionAnalisisSuelo,
                calculo.phAnalisisSuelo,
                calculo.materiaOrganica,
                calculo.acidezTotal,
                calculo.recomendacionGeneral,
                calculo.observacion,
                enmienda.cice,
                enmienda.saturacionActual
            FROM dbo.usuarioPropietario up
            INNER JOIN dbo.propietarioTerreno pt
                ON pt.propietarioId = up.propietarioId
               AND pt.activo = 1
            INNER JOIN dbo.analisisSueloCalculo calculo
                ON calculo.terrenoId = pt.terrenoId
               AND calculo.activo = 1
            INNER JOIN dbo.analisisSuelo analisis
                ON analisis.analisisSueloId =
                   calculo.analisisSueloId
               AND analisis.activo = 1
            OUTER APPLY
            (
                SELECT TOP (1)
                    resultado.cice,
                    resultado.saturacionActual
                FROM dbo.enmiendaCalcarea resultado
                WHERE resultado.activo = 1
                  AND resultado.analisisSueloCalculoId =
                      calculo.analisisSueloCalculoId
                ORDER BY
                    resultado.fechaCreacion DESC,
                    resultado.enmiendaCalcareaId DESC
            ) enmienda
            WHERE up.usuarioId = @usuarioId
              AND up.activo = 1
              AND pt.terrenoId = @terrenoId
            ORDER BY
                calculo.fechaCalculo DESC,
                calculo.analisisSueloCalculoId DESC;
            """,
            command =>
            {
                AgregarParametro(
                    command,
                    "@limite",
                    limite);

                AgregarParametro(
                    command,
                    "@usuarioId",
                    usuarioId);

                AgregarParametro(
                    command,
                    "@terrenoId",
                    terrenoId);
            },
            reader => new AnalisisHistorialBase
            {
                AnalisisSueloCalculoId =
                    Entero(reader, 0),
                AnalisisSueloId =
                    Entero(reader, 1),
                Identificador =
                    Texto(reader, 2),
                FechaLaboratorio =
                    DateOnly.FromDateTime(
                        Fecha(reader, 3)),
                FechaRegistro =
                    Fecha(reader, 4),
                Ph =
                    Decimal(reader, 5),
                MateriaOrganica =
                    DecimalNullable(reader, 6),
                AcidezTotal =
                    DecimalNullable(reader, 7),
                RecomendacionGeneral =
                    Texto(reader, 8),
                Observacion =
                    Texto(reader, 9),
                Cice =
                    DecimalNullable(reader, 10),
                SaturacionBases =
                    DecimalNullable(reader, 11)
            },
            cancellationToken);
    }

    private async Task<IActionResult?>
        ValidarAccesoPortalPropietarioAsync(
            int usuarioId,
            CancellationToken cancellationToken)
    {
        ResultadoPermisoApi accesoWeb =
            await permisos.ValidarAsync(
                usuarioId,
                PortalWebDatabaseInitializer.AccesoPortal,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoWeb.Permitido)
        {
            return StatusCode(
                accesoWeb.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene habilitado el permiso " +
                        "\"Acceso al portal web\"."
                });
        }

        ResultadoPermisoApi accesoPropietario =
            await permisos.ValidarAsync(
                usuarioId,
                ParametrizacionAccesoDatabaseInitializer
                    .PortalPropietario,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoPropietario.Permitido)
        {
            return StatusCode(
                accesoPropietario.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene habilitado el permiso " +
                        "\"Portal del propietario\"."
                });
        }

        return null;
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

    private static PortalCentroGeoespacialResumenDto
        ConstruirResumen(
            IReadOnlyCollection<PortalCentroTerrenoDto> terrenos,
            IReadOnlyCollection<ClimaPuntoMapaDto> climaLocal)
    {
        decimal[] temperaturas =
            climaLocal
                .Where(x =>
                    x.Temperatura.HasValue)
                .Select(x =>
                    x.Temperatura!.Value)
                .ToArray();

        decimal[] humedades =
            climaLocal
                .Where(x =>
                    x.HumedadRelativa.HasValue)
                .Select(x =>
                    x.HumedadRelativa!.Value)
                .ToArray();

        decimal[] lluvias =
            climaLocal
                .Where(x =>
                    x.Precipitacion.HasValue)
                .Select(x =>
                    x.Precipitacion!.Value)
                .ToArray();

        decimal[] vientos =
            climaLocal
                .Where(x =>
                    x.VelocidadViento.HasValue)
                .Select(x =>
                    x.VelocidadViento!.Value)
                .ToArray();

        return new PortalCentroGeoespacialResumenDto
        {
            TotalTerrenos =
                terrenos.Count,
            ExtensionTotalManzanas =
                terrenos.Sum(x =>
                    x.ExtensionManzanas),
            EstadoCritico =
                terrenos.Count(x =>
                    x.Nivel == NivelCritico),
            RequierenAtencion =
                terrenos.Count(x =>
                    x.Nivel == NivelAtencion),
            EstadoEstable =
                terrenos.Count(x =>
                    x.Nivel == NivelEstable),
            SinAnalisis =
                terrenos.Count(x =>
                    x.Nivel == NivelSinAnalisis),
            AlertasActivas =
                terrenos.Sum(x =>
                    x.Alertas.Count),
            TemperaturaPromedioLocal =
                temperaturas.Length == 0
                    ? null
                    : decimal.Round(
                        temperaturas.Average(),
                        1),
            HumedadPromedioLocal =
                humedades.Length == 0
                    ? null
                    : decimal.Round(
                        humedades.Average(),
                        1),
            PrecipitacionMaximaLocal =
                lluvias.Length == 0
                    ? null
                    : lluvias.Max(),
            VientoMaximoLocal =
                vientos.Length == 0
                    ? null
                    : vientos.Max()
        };
    }

    private static List<ClimaPuntoMapaDto>
        SeleccionarClimaLocal(
            IReadOnlyCollection<PortalCentroTerrenoDto> terrenos,
            IReadOnlyCollection<ClimaPuntoMapaDto> puntos)
    {
        PortalCentroTerrenoDto[] ubicados =
            terrenos
                .Where(x =>
                    x.Latitud != 0 &&
                    x.Longitud != 0)
                .ToArray();

        if (ubicados.Length == 0 ||
            puntos.Count == 0)
        {
            return [];
        }

        var seleccionados =
            new Dictionary<string, ClimaPuntoMapaDto>();

        foreach (PortalCentroTerrenoDto terreno in ubicados)
        {
            ClimaPuntoMapaDto? cercano =
                puntos
                    .OrderBy(punto =>
                        DistanciaCuadrada(
                            terreno.Latitud,
                            terreno.Longitud,
                            punto.Latitud,
                            punto.Longitud))
                    .FirstOrDefault();

            if (cercano is null)
                continue;

            string clave =
                $"{cercano.Latitud:0.####}|" +
                $"{cercano.Longitud:0.####}";

            seleccionados[clave] =
                cercano;
        }

        return seleccionados
            .Values
            .ToList();
    }

    private static decimal DistanciaCuadrada(
        decimal latitudA,
        decimal longitudA,
        decimal latitudB,
        decimal longitudB)
    {
        decimal diferenciaLatitud =
            latitudA - latitudB;

        decimal diferenciaLongitud =
            longitudA - longitudB;

        return
            diferenciaLatitud * diferenciaLatitud +
            diferenciaLongitud * diferenciaLongitud;
    }

    private static string CalcularNivel(
        decimal? ph,
        decimal? materiaOrganica,
        decimal? acidezTotal,
        UmbralesAlertas umbrales)
    {
        if (!ph.HasValue)
            return NivelSinAnalisis;

        if (ph.Value <=
                umbrales.PhBajoCriticoMaximo ||
            ph.Value >=
                umbrales.PhAltoCriticoMinimo)
        {
            return NivelCritico;
        }

        if (ph.Value <=
                umbrales.PhBajoAtencionMaximo ||
            ph.Value >=
                umbrales.PhAltoAtencionMinimo ||
            materiaOrganica.HasValue &&
                materiaOrganica.Value <=
                umbrales.MateriaOrganicaBajaMaxima ||
            acidezTotal.HasValue &&
                acidezTotal.Value >=
                umbrales.AcidezAltaMinima)
        {
            return NivelAtencion;
        }

        return NivelEstable;
    }

    private static List<PortalCentroAlertaDto>
        ConstruirAlertas(
            TerrenoCentroBase terreno,
            UmbralesAlertas umbrales)
    {
        var alertas =
            new List<PortalCentroAlertaDto>();

        if (!terreno.Ph.HasValue)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "SIN_ANALISIS",
                Nivel =
                    NivelAtencion,
                Titulo =
                    "Terreno sin análisis",
                Mensaje =
                    "No existe un análisis de suelo activo " +
                    "para este terreno."
            });

            return alertas;
        }

        if (terreno.Ph.Value <=
            umbrales.PhBajoCriticoMaximo)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "PH_BAJO_CRITICO",
                Nivel =
                    NivelCritico,
                Titulo =
                    "pH críticamente bajo",
                Mensaje =
                    "El pH se encuentra por debajo del " +
                    "límite crítico configurado.",
                Valor =
                    terreno.Ph,
                Umbral =
                    umbrales.PhBajoCriticoMaximo
            });
        }
        else if (terreno.Ph.Value >=
                 umbrales.PhAltoCriticoMinimo)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "PH_ALTO_CRITICO",
                Nivel =
                    NivelCritico,
                Titulo =
                    "pH críticamente alto",
                Mensaje =
                    "El pH supera el límite crítico configurado.",
                Valor =
                    terreno.Ph,
                Umbral =
                    umbrales.PhAltoCriticoMinimo
            });
        }
        else if (terreno.Ph.Value <=
                 umbrales.PhBajoAtencionMaximo)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "PH_BAJO_ATENCION",
                Nivel =
                    NivelAtencion,
                Titulo =
                    "pH bajo",
                Mensaje =
                    "El pH requiere seguimiento.",
                Valor =
                    terreno.Ph,
                Umbral =
                    umbrales.PhBajoAtencionMaximo
            });
        }
        else if (terreno.Ph.Value >=
                 umbrales.PhAltoAtencionMinimo)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "PH_ALTO_ATENCION",
                Nivel =
                    NivelAtencion,
                Titulo =
                    "pH alto",
                Mensaje =
                    "El pH requiere seguimiento.",
                Valor =
                    terreno.Ph,
                Umbral =
                    umbrales.PhAltoAtencionMinimo
            });
        }

        if (terreno.MateriaOrganica.HasValue &&
            terreno.MateriaOrganica.Value <=
            umbrales.MateriaOrganicaBajaMaxima)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "MATERIA_ORGANICA_BAJA",
                Nivel =
                    NivelAtencion,
                Titulo =
                    "Materia orgánica baja",
                Mensaje =
                    "La materia orgánica está en o por debajo " +
                    "del umbral de atención.",
                Valor =
                    terreno.MateriaOrganica,
                Umbral =
                    umbrales.MateriaOrganicaBajaMaxima
            });
        }

        if (terreno.AcidezTotal.HasValue &&
            terreno.AcidezTotal.Value >=
            umbrales.AcidezAltaMinima)
        {
            alertas.Add(new PortalCentroAlertaDto
            {
                Clave =
                    "ACIDEZ_TOTAL_ALTA",
                Nivel =
                    NivelAtencion,
                Titulo =
                    "Acidez total alta",
                Mensaje =
                    "La acidez total alcanzó el umbral " +
                    "configurado de atención.",
                Valor =
                    terreno.AcidezTotal,
                Umbral =
                    umbrales.AcidezAltaMinima
            });
        }

        return alertas;
    }

    private static string EstadoTexto(
        string nivel) =>
        nivel switch
        {
            NivelCritico =>
                "Estado crítico",
            NivelAtencion =>
                "Requiere atención",
            NivelEstable =>
                "Estado estable",
            _ =>
                "Sin análisis"
        };

    private static string ColorNivel(
        string nivel) =>
        nivel switch
        {
            NivelCritico =>
                "#EF4444",
            NivelAtencion =>
                "#F2C94C",
            NivelEstable =>
                "#3B655B",
            _ =>
                "#94A3B8"
        };

    private IActionResult RespuestaNoAutorizada() =>
        Unauthorized(new
        {
            success = false,
            message =
                "No fue posible identificar al usuario autenticado."
        });

    private async Task<T?> ConsultarUnoAsync<T>(
        string sql,
        Action<DbCommand>? configurar,
        Func<DbDataReader, T> mapear,
        CancellationToken cancellationToken)
    {
        List<T> items =
            await ConsultarListaAsync(
                sql,
                configurar,
                mapear,
                cancellationToken);

        return items.FirstOrDefault();
    }

    private async Task<List<T>>
        ConsultarListaAsync<T>(
            string sql,
            Action<DbCommand>? configurar,
            Func<DbDataReader, T> mapear,
            CancellationToken cancellationToken)
    {
        var resultado =
            new List<T>();

        DbConnection connection =
            db.Database.GetDbConnection();

        bool cerrar =
            connection.State !=
            ConnectionState.Open;

        try
        {
            if (cerrar)
            {
                await connection.OpenAsync(
                    cancellationToken);
            }

            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText =
                sql;

            configurar?.Invoke(command);

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                resultado.Add(
                    mapear(reader));
            }
        }
        finally
        {
            if (cerrar &&
                connection.State ==
                ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return resultado;
    }

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object? valor)
    {
        DbParameter parametro =
            command.CreateParameter();

        parametro.ParameterName =
            nombre;

        parametro.Value =
            valor ?? DBNull.Value;

        command.Parameters.Add(
            parametro);
    }

    private static string Texto(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? string.Empty
            : Convert.ToString(
                reader.GetValue(indice)) ??
              string.Empty;

    private static string? TextoNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToString(
                reader.GetValue(indice));

    private static int Entero(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(indice));

    private static int? EnteroNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToInt32(
                reader.GetValue(indice));

    private static decimal Decimal(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? 0m
            : Convert.ToDecimal(
                reader.GetValue(indice));

    private static decimal? DecimalNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToDecimal(
                reader.GetValue(indice));

    private static DateTime Fecha(
        DbDataReader reader,
        int indice) =>
        Convert.ToDateTime(
            reader.GetValue(indice));

    private static DateTime? FechaNullable(
        DbDataReader reader,
        int indice) =>
        reader.IsDBNull(indice)
            ? null
            : Convert.ToDateTime(
                reader.GetValue(indice));

    private sealed class TerrenoCentroBase
    {
        public int TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public decimal ExtensionManzanas { get; set; }
        public DateTime FechaIngreso { get; set; }
        public int CantidadPlantas { get; set; }
        public decimal CantidadQuintalesOro { get; set; }
        public decimal Latitud { get; set; }
        public decimal Longitud { get; set; }
        public int MunicipioId { get; set; }
        public string Municipio { get; set; } = string.Empty;
        public int DepartamentoId { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int TotalAnalisis { get; set; }
        public int? AnalisisSueloCalculoId { get; set; }
        public DateTime? FechaUltimoAnalisis { get; set; }
        public decimal? Ph { get; set; }
        public decimal? MateriaOrganica { get; set; }
        public decimal? AcidezTotal { get; set; }
        public decimal? Cice { get; set; }
        public decimal? SaturacionBases { get; set; }
        public string RecomendacionGeneral { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
    }

    private sealed class ElementoCalculoBase
    {
        public int AnalisisSueloCalculoId { get; set; }
        public PortalCentroElementoDto Elemento { get; set; } = new();
    }

    private sealed class TerrenoHistorialBase
    {
        public int TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public string Municipio { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public decimal ExtensionManzanas { get; set; }
        public decimal ProduccionQuintalesOro { get; set; }
    }

    private sealed class AnalisisHistorialBase
    {
        public int AnalisisSueloCalculoId { get; set; }
        public int AnalisisSueloId { get; set; }
        public string Identificador { get; set; } = string.Empty;
        public DateOnly FechaLaboratorio { get; set; }
        public DateTime FechaRegistro { get; set; }
        public decimal Ph { get; set; }
        public decimal? MateriaOrganica { get; set; }
        public decimal? AcidezTotal { get; set; }
        public decimal? Cice { get; set; }
        public decimal? SaturacionBases { get; set; }
        public string RecomendacionGeneral { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
    }
}
