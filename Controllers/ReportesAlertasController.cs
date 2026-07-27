using ClosedXML.Excel;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/reportes-alertas")]
    public sealed class ReportesAlertasController : ControllerBase
    {
        private readonly AlertasAgricolasDbContext db;

        public ReportesAlertasController(AlertasAgricolasDbContext db)
        {
            this.db = db;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult> Resumen(
            CancellationToken cancellationToken = default)
        {
            var query = db.Seguimientos.AsNoTracking().Where(x => x.Activo);

            return Ok(new
            {
                total = await query.CountAsync(cancellationToken),
                pendientes = await query.CountAsync(
                    x => x.Estado == "PENDIENTE", cancellationToken),
                enProceso = await query.CountAsync(
                    x => x.Estado == "EN_PROCESO", cancellationToken),
                atendidas = await query.CountAsync(
                    x => x.Estado == "ATENDIDA", cancellationToken),
                descartadas = await query.CountAsync(
                    x => x.Estado == "DESCARTADA", cancellationToken),
                criticas = await query.CountAsync(
                    x => x.Nivel == "CRITICA", cancellationToken)
            });
        }

        [HttpGet("excel")]
        public async Task<IActionResult> Excel(
            CancellationToken cancellationToken = default)
        {
            var datos = await db.Seguimientos.AsNoTracking()
                .Where(x => x.Activo)
                .OrderByDescending(x => x.FechaUltimaModificacionUtc)
                .ToListAsync(cancellationToken);

            using var libro = new XLWorkbook();
            var hoja = libro.Worksheets.Add("Seguimiento de alertas");

            string[] encabezados =
            [
                "ID", "Terreno", "Tipo", "Nivel", "Estado",
                "Responsable ID", "Observación", "Creación",
                "Última modificación", "Cierre"
            ];

            for (int i = 0; i < encabezados.Length; i++)
                hoja.Cell(1, i + 1).Value = encabezados[i];

            hoja.Range(1, 1, 1, encabezados.Length).Style.Font.Bold = true;
            hoja.Range(1, 1, 1, encabezados.Length).Style.Fill.BackgroundColor =
                XLColor.FromHtml("#3B655B");
            hoja.Range(1, 1, 1, encabezados.Length).Style.Font.FontColor =
                XLColor.White;

            int fila = 2;
            foreach (var item in datos)
            {
                hoja.Cell(fila, 1).Value = item.SeguimientoAlertaAgricolaId;
                hoja.Cell(fila, 2).Value = item.TerrenoId;
                hoja.Cell(fila, 3).Value = item.TipoAlerta;
                hoja.Cell(fila, 4).Value = item.Nivel;
                hoja.Cell(fila, 5).Value = item.Estado;
                hoja.Cell(fila, 6).Value = item.UsuarioAsignadoId;
                hoja.Cell(fila, 7).Value = item.Observacion;
                hoja.Cell(fila, 8).Value = item.FechaCreacionUtc;
                hoja.Cell(fila, 9).Value = item.FechaUltimaModificacionUtc;
                if (item.FechaCierreUtc.HasValue)
                    hoja.Cell(fila, 10).Value = item.FechaCierreUtc.Value;
                fila++;
            }

            hoja.Columns().AdjustToContents(10, 45);

            using var stream = new MemoryStream();
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
            var datos = await db.Seguimientos.AsNoTracking()
                .Where(x => x.Activo)
                .OrderByDescending(x => x.FechaUltimaModificacionUtc)
                .Take(250)
                .ToListAsync(cancellationToken);

            byte[] archivo = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(column =>
                    {
                        column.Item().Text("CONATRADEC")
                            .FontSize(18).Bold().FontColor("#3B655B");
                        column.Item().Text("Reporte de seguimiento de alertas agrícolas")
                            .FontSize(12).Bold();
                        column.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy hh:mm tt}");
                    });

                    page.Content().PaddingVertical(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
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
                                "ID", "Terreno", "Tipo", "Nivel", "Estado", "Observación"
                            })
                            {
                                header.Cell().Background("#3B655B")
                                    .Padding(5).Text(texto).FontColor(Colors.White).Bold();
                            }
                        });

                        foreach (var item in datos)
                        {
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.SeguimientoAlertaAgricolaId.ToString());
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.TerrenoId.ToString());
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.TipoAlerta);
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.Nivel);
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.Estado);
                            table.Cell().BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(4).Text(item.Observacion ?? string.Empty);
                        }
                    });

                    page.Footer().AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Página ");
                            x.CurrentPageNumber();
                            x.Span(" de ");
                            x.TotalPages();
                        });
                });
            }).GeneratePdf();

            return File(
                archivo,
                "application/pdf",
                $"reporte-alertas-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
        }
    }
}
