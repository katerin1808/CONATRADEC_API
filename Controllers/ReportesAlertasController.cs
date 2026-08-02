using ClosedXML.Excel;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reportes-alertas")]
    public sealed class ReportesAlertasController :
        ControllerBase
    {
        private const string PermisoAnteriorSeguimiento =
            "SeguimientoAlertasWeb";

        private readonly AlertasAgricolasDbContext db;
        private readonly PermisoApiService permisos;

        public ReportesAlertasController(
            AlertasAgricolasDbContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult> Resumen(
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<SeguimientoAlertaAgricola> query =
                db.Seguimientos
                    .AsNoTracking()
                    .Where(item => item.Activo);

            return Ok(new
            {
                total =
                    await query.CountAsync(
                        cancellationToken),
                pendientes =
                    await query.CountAsync(
                        item =>
                            item.Estado ==
                            "PENDIENTE",
                        cancellationToken),
                enProceso =
                    await query.CountAsync(
                        item =>
                            item.Estado ==
                            "EN_PROCESO",
                        cancellationToken),
                atendidas =
                    await query.CountAsync(
                        item =>
                            item.Estado ==
                            "ATENDIDA",
                        cancellationToken),
                descartadas =
                    await query.CountAsync(
                        item =>
                            item.Estado ==
                            "DESCARTADA",
                        cancellationToken),
                criticas =
                    await query.CountAsync(
                        item =>
                            item.Nivel ==
                            "CRITICA",
                        cancellationToken)
            });
        }

        [HttpGet("excel")]
        public async Task<IActionResult> Excel(
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<SeguimientoAlertaAgricola> datos =
                await db.Seguimientos
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderByDescending(item =>
                        item.FechaUltimaModificacionUtc)
                    .ToListAsync(cancellationToken);

            using var libro =
                new XLWorkbook();

            var hoja =
                libro.Worksheets.Add(
                    "Seguimiento de alertas");

            string[] encabezados =
            [
                "ID",
                "Terreno",
                "Tipo",
                "Nivel",
                "Estado",
                "Responsable ID",
                "Observación",
                "Creación",
                "Última modificación",
                "Cierre"
            ];

            for (int indice = 0;
                 indice < encabezados.Length;
                 indice++)
            {
                hoja.Cell(
                    1,
                    indice + 1).Value =
                    encabezados[indice];
            }

            hoja.Range(
                    1,
                    1,
                    1,
                    encabezados.Length)
                .Style.Font.Bold =
                true;

            hoja.Range(
                    1,
                    1,
                    1,
                    encabezados.Length)
                .Style.Fill.BackgroundColor =
                XLColor.FromHtml("#3B655B");

            hoja.Range(
                    1,
                    1,
                    1,
                    encabezados.Length)
                .Style.Font.FontColor =
                XLColor.White;

            int fila = 2;

            foreach (SeguimientoAlertaAgricola item in datos)
            {
                hoja.Cell(fila, 1).Value =
                    item.SeguimientoAlertaAgricolaId;
                hoja.Cell(fila, 2).Value =
                    item.TerrenoId;
                hoja.Cell(fila, 3).Value =
                    item.TipoAlerta;
                hoja.Cell(fila, 4).Value =
                    item.Nivel;
                hoja.Cell(fila, 5).Value =
                    item.Estado;
                hoja.Cell(fila, 6).Value =
                    item.UsuarioAsignadoId;
                hoja.Cell(fila, 7).Value =
                    item.Observacion;
                hoja.Cell(fila, 8).Value =
                    item.FechaCreacionUtc;
                hoja.Cell(fila, 9).Value =
                    item.FechaUltimaModificacionUtc;

                if (item.FechaCierreUtc.HasValue)
                {
                    hoja.Cell(fila, 10).Value =
                        item.FechaCierreUtc.Value;
                }

                fila++;
            }

            hoja.Columns()
                .AdjustToContents(
                    10,
                    45);

            using var stream =
                new MemoryStream();

            libro.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"reporte-alertas-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        [HttpGet("pdf")]
        public async Task<IActionResult> Pdf(
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<SeguimientoAlertaAgricola> datos =
                await db.Seguimientos
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderByDescending(item =>
                        item.FechaUltimaModificacionUtc)
                    .Take(250)
                    .ToListAsync(cancellationToken);

            byte[] archivo =
                Document.Create(document =>
                {
                    document.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(28);
                        page.DefaultTextStyle(
                            text =>
                                text.FontSize(9));

                        page.Header()
                            .Column(column =>
                            {
                                column.Item()
                                    .Text("CONATRADEC")
                                    .FontSize(18)
                                    .Bold()
                                    .FontColor("#3B655B");

                                column.Item()
                                    .Text(
                                        "Reporte de seguimiento de alertas agrícolas")
                                    .FontSize(12)
                                    .Bold();

                                column.Item()
                                    .Text(
                                        $"Generado: {DateTime.Now:dd/MM/yyyy hh:mm tt}");
                            });

                        page.Content()
                            .PaddingVertical(12)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(
                                    columns =>
                                    {
                                        columns.ConstantColumn(34);
                                        columns.ConstantColumn(44);
                                        columns.RelativeColumn(1.5f);
                                        columns.RelativeColumn();
                                        columns.RelativeColumn();
                                        columns.RelativeColumn(2);
                                    });

                                table.Header(header =>
                                {
                                    foreach (string texto in new[]
                                             {
                                                 "ID",
                                                 "Terreno",
                                                 "Tipo",
                                                 "Nivel",
                                                 "Estado",
                                                 "Observación"
                                             })
                                    {
                                        header.Cell()
                                            .Background("#3B655B")
                                            .Padding(5)
                                            .Text(texto)
                                            .FontColor(Colors.White)
                                            .Bold();
                                    }
                                });

                                foreach (
                                    SeguimientoAlertaAgricola item
                                    in datos)
                                {
                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(
                                            item
                                                .SeguimientoAlertaAgricolaId
                                                .ToString());

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(
                                            item.TerrenoId.ToString());

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(item.TipoAlerta);

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(item.Nivel);

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(item.Estado);

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor("#E5E7EB")
                                        .Padding(4)
                                        .Text(
                                            item.Observacion ??
                                            string.Empty);
                                }
                            });

                        page.Footer()
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.Span("Página ");
                                text.CurrentPageNumber();
                                text.Span(" de ");
                                text.TotalPages();
                            });
                    });
                })
                .GeneratePdf();

            return File(
                archivo,
                "application/pdf",
                $"reporte-alertas-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
        }

        private async Task<ActionResult?>
            ValidarAccesoAsync(
                CancellationToken cancellationToken)
        {
            int? usuarioId =
                ObtenerUsuarioId();

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    PortalWebDatabaseInitializer
                        .ReportesWeb,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            /*
             * Compatibilidad temporal:
             * los roles que ya administraban el seguimiento de alertas no
             * pierden acceso mientras se distribuye el permiso nuevo.
             */
            if (!resultado.Permitido &&
                resultado.CodigoEstado ==
                    StatusCodes.Status403Forbidden)
            {
                resultado =
                    await permisos.ValidarAsync(
                        usuarioId,
                        PermisoAnteriorSeguimiento,
                        TipoPermisoApi.Leer,
                        cancellationToken);
            }

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje
                });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue("uid") ??
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                       valor,
                       out int usuarioId) &&
                   usuarioId > 0
                ? usuarioId
                : null;
        }
    }
}
