using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CONATRADEC_API.Reportes
{
    public static class AnalisisReportePdf
    {
        private const string Verde = "#3B655B";
        private const string Cafe = "#9B552C";
        private const string AmarilloSuave = "#FFF8D8";
        private const string VerdeSuave = "#EEF5F2";
        private const string GrisBorde = "#D1D5DB";
        private const string GrisFondo = "#F9FAFB";
        private const string GrisTexto = "#4B5563";

        public static byte[] Generar(AnalisisReporte reporte)
        {
            ArgumentNullException.ThrowIfNull(reporte);

            return Document.Create(documento =>
            {
                documento.Page(pagina =>
                {
                    pagina.Size(PageSizes.A4);
                    pagina.Margin(28);
                    pagina.PageColor(Colors.White);
                    pagina.DefaultTextStyle(x => x.FontSize(9));

                    pagina.Header()
                        .PaddingBottom(12)
                        .Element(contenedor => ComponerEncabezado(contenedor, reporte));

                    pagina.Content()
                        .Column(columna => ComponerContenido(columna, reporte));

                    pagina.Footer()
                        .PaddingTop(8)
                        .BorderTop(1)
                        .BorderColor(GrisBorde)
                        .AlignCenter()
                        .Text(texto =>
                        {
                            texto.DefaultTextStyle(x =>
                                x.FontSize(8).FontColor(GrisTexto));
                            texto.Span("CONATRACAFÉ SOIL · Página ");
                            texto.CurrentPageNumber();
                            texto.Span(" de ");
                            texto.TotalPages();
                        });
                });
            }).GeneratePdf();
        }

        private static void ComponerEncabezado(
            IContainer contenedor,
            AnalisisReporte reporte)
        {
            contenedor
                .Background(Verde)
                .Padding(14)
                .Row(fila =>
                {
                    fila.RelativeItem()
                        .Column(columna =>
                        {
                            columna.Item()
                                .Text("CONATRACAFÉ SOIL")
                                .Bold()
                                .FontSize(18)
                                .FontColor(Colors.White);

                            columna.Item()
                                .PaddingTop(2)
                                .Text("Reporte integral de análisis de suelo")
                                .FontSize(10)
                                .FontColor(Colors.White);
                        });

                    fila.ConstantItem(180)
                        .AlignRight()
                        .Column(columna =>
                        {
                            columna.Item()
                                .AlignRight()
                                .Text(ValorO(reporte.Identificador, "Análisis de suelo"))
                                .Bold()
                                .FontSize(11)
                                .FontColor(Colors.White);

                            columna.Item()
                                .PaddingTop(3)
                                .AlignRight()
                                .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(8)
                                .FontColor(Colors.White);
                        });
                });
        }

        private static void ComponerContenido(
            ColumnDescriptor columna,
            AnalisisReporte reporte)
        {
            columna.Spacing(12);

            ComponerDatosGenerales(columna, reporte);
            ComponerValoresLaboratorio(columna, reporte);
            ComponerRequerimiento(columna, reporte);

            if (reporte.Balance != null)
                ComponerBalance(columna, reporte.Balance);

            if (reporte.Enmienda != null)
                ComponerEnmienda(columna, reporte.Enmienda);

            if (reporte.FertilizacionMixta != null)
                ComponerFertilizacionMixta(columna, reporte.FertilizacionMixta);
        }

        private static void ComponerDatosGenerales(
            ColumnDescriptor columna,
            AnalisisReporte reporte)
        {
            columna.Item().Element(TituloSeccion).Text("Datos generales");

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.ConstantColumn(82);
                    columnas.RelativeColumn();
                    columnas.ConstantColumn(82);
                    columnas.RelativeColumn();
                });

                AgregarPar(tabla, "Cliente", reporte.Cliente, "Terreno", reporte.Terreno);
                AgregarPar(
                    tabla,
                    "Fecha del análisis",
                    reporte.FechaAnalisis.ToString("dd/MM/yyyy"),
                    "Laboratorio",
                    reporte.Laboratorio);
                AgregarPar(tabla, "Cultivo", reporte.TipoCultivo, "Tipo de análisis", reporte.TipoAnalisis);
                AgregarPar(tabla, "Producción", $"{reporte.ProduccionQqOro:N2} qq oro", "Tamaño", $"{reporte.TamanoFincaMz:N2} mz");
                AgregarPar(tabla, "pH", $"{reporte.Ph:N2}", "Acidez total", TextoNumero(reporte.AcidezTotal));
                AgregarPar(
                    tabla,
                    "Materia orgánica",
                    TextoNumero(reporte.MateriaOrganica, reporte.UnidadMateriaOrganica),
                    "Responsable",
                    reporte.Responsable);
            });
        }

        private static void ComponerValoresLaboratorio(
            ColumnDescriptor columna,
            AnalisisReporte reporte)
        {
            columna.Item().Element(TituloSeccion).Text("Valores originales del laboratorio");

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn(2);
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                });

                tabla.Header(encabezado =>
                {
                    Encabezado(encabezado, "Elemento");
                    Encabezado(encabezado, "Cantidad");
                    Encabezado(encabezado, "Unidad");
                });

                foreach (AnalisisReporteValorLaboratorio item in reporte.ValoresLaboratorio)
                {
                    Celda(tabla, item.Elemento);
                    CeldaNumero(tabla, item.Cantidad, "N4");
                    Celda(tabla, item.Unidad);
                }
            });
        }

        private static void ComponerRequerimiento(
            ColumnDescriptor columna,
            AnalisisReporte reporte)
        {
            columna.Item().Element(TituloSeccion).Text("Requerimiento anual");

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn(1.5f);
                    columnas.RelativeColumn(0.8f);
                    columnas.RelativeColumn(0.9f);
                    columnas.RelativeColumn();
                    columnas.RelativeColumn(2);
                });

                tabla.Header(encabezado =>
                {
                    Encabezado(encabezado, "Elemento");
                    Encabezado(encabezado, "Ingresado");
                    Encabezado(encabezado, "Requerimiento");
                    Encabezado(encabezado, "Clasificación");
                    Encabezado(encabezado, "Observación");
                });

                foreach (AnalisisReporteRequerimiento item in reporte.Requerimientos)
                {
                    Celda(tabla, item.Elemento);
                    CeldaNumero(tabla, item.CantidadIngresada, "N4");
                    Celda(
                        tabla,
                        item.RequerimientoLbMz.HasValue
                            ? $"{item.RequerimientoLbMz:N2} {item.UnidadResultado}"
                            : "-");
                    Celda(tabla, item.Clasificacion);
                    Celda(tabla, item.Observacion);
                }
            });

            if (!string.IsNullOrWhiteSpace(reporte.RecomendacionGeneral))
            {
                columna.Item()
                    .Background(VerdeSuave)
                    .Padding(10)
                    .Column(contenido =>
                    {
                        contenido.Item()
                            .Text("Recomendación general")
                            .Bold()
                            .FontColor(Verde);

                        contenido.Item()
                            .PaddingTop(3)
                            .Text(reporte.RecomendacionGeneral);
                    });
            }

            if (reporte.Observaciones.Count > 0)
            {
                columna.Item().Text(texto =>
                {
                    texto.Span("Observaciones: ").Bold();
                    texto.Span(string.Join(" · ", reporte.Observaciones));
                });
            }
        }

        private static void ComponerBalance(
            ColumnDescriptor columna,
            AnalisisReporteBalance balance)
        {
            columna.Item().Element(TituloSeccion).Text("Balance de fórmula");

            columna.Item()
                .Background(GrisFondo)
                .Padding(10)
                .Column(contenido =>
                {
                    contenido.Item()
                        .Text(ValorO(balance.NombreFormula, "Fórmula nutricional"))
                        .Bold()
                        .FontSize(12)
                        .FontColor(Verde);

                    if (balance.FormulaComercial.Count > 0)
                    {
                        contenido.Item()
                            .PaddingTop(3)
                            .Text("Aportes: " + string.Join(
                                " · ",
                                balance.FormulaComercial.Select(x =>
                                    $"{x.Key} {x.Value:N2}")));
                    }

                    contenido.Item()
                        .PaddingTop(5)
                        .Text(
                            $"Mezcla exacta: {balance.MezclaTotalQq:N3} qq  ·  " +
                            $"Aplicaciones: {balance.TotalAplicaciones}  ·  " +
                            $"Dosis/planta/aplicación: {balance.DosisPlantaPorAplicacionOz:N2} oz");

                    contenido.Item()
                        .PaddingTop(3)
                        .Text(texto =>
                        {
                            texto.Span("Costo real de compra: ").Bold().FontColor(Cafe);
                            texto.Span($"C$ {balance.CostoRealCompra:N2}").Bold().FontColor(Cafe);
                            texto.Span($"  ·  Precio exacto de referencia: C$ {balance.PrecioExactoReferencia:N2}");
                        });
                });

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn(1.7f);
                    columnas.RelativeColumn(0.75f);
                    columnas.RelativeColumn(0.7f);
                    columnas.RelativeColumn(0.85f);
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                });

                tabla.Header(encabezado =>
                {
                    Encabezado(encabezado, "Fuente / elemento");
                    Encabezado(encabezado, "QQ exactos");
                    Encabezado(encabezado, "QQ compra");
                    Encabezado(encabezado, "Precio/QQ");
                    Encabezado(encabezado, "Subtotal exacto");
                    Encabezado(encabezado, "Costo compra");
                });

                foreach (AnalisisReporteBalanceDetalle item in balance.Detalles)
                {
                    Celda(tabla, $"{item.Fuente}\n{item.Elemento}");
                    CeldaNumero(tabla, item.QuintalesExactos, "N3");
                    CeldaNumero(tabla, item.QuintalesComprar, "N0");
                    CeldaMoneda(tabla, item.PrecioPorQuintal);
                    CeldaMoneda(tabla, item.SubtotalExacto);
                    CeldaMoneda(tabla, item.CostoCompra);
                }
            });
        }

        private static void ComponerEnmienda(
            ColumnDescriptor columna,
            AnalisisReporteEnmienda enmienda)
        {
            columna.Item().Element(TituloSeccion).Text("Enmienda calcárea");

            columna.Item()
                .Background(GrisFondo)
                .Padding(10)
                .Text(texto =>
                {
                    texto.Span(ValorO(enmienda.Fuente, "Fuente no especificada"))
                        .Bold()
                        .FontColor(Cafe);
                    texto.Span(
                        $"  ·  {enmienda.TotalAplicaciones} aplicaciones  ·  " +
                        $"{enmienda.TotalPlantas:N0} plantas");
                });

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                });

                AgregarPar(tabla, "pH", $"{enmienda.Ph:N2}", "Acidez total", $"{enmienda.AcidezTotal:N2}");
                AgregarPar(tabla, "Calcio", $"{enmienda.Calcio:N2}", "Magnesio", $"{enmienda.Magnesio:N2}");
                AgregarPar(tabla, "Potasio", $"{enmienda.Potasio:N2}", "CICE", $"{enmienda.Cice:N2}");
                AgregarPar(tabla, "Saturación actual", $"{enmienda.SaturacionActual:N2}%", "Saturación deseada", $"{enmienda.SaturacionDeseada:N2}%");
                AgregarPar(tabla, "PRNT", $"{enmienda.Prnt:N2}%", "Necesidad", $"{enmienda.NecesidadEncaladoTonHa:N2} ton/ha");
                AgregarPar(tabla, "Equivalente", $"{enmienda.NecesidadEncaladoLbMz:N2} lb/mz", "Dosis anual", $"{enmienda.DosisPlantaAnualOz:N2} oz/planta");
                AgregarPar(tabla, "Por aplicación", $"{enmienda.DosisPlantaPorAplicacionOz:N2} oz/planta", "Análisis", enmienda.NombreAnalisis);
            });
        }

        private static void ComponerFertilizacionMixta(
            ColumnDescriptor columna,
            AnalisisReporteFertilizacionMixta mixta)
        {
            columna.Item().Element(TituloSeccion).Text("Fertilización mixta");

            if (!string.IsNullOrWhiteSpace(mixta.Observacion))
                columna.Item().Text(mixta.Observacion).FontColor(GrisTexto);

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn(2);
                    columnas.RelativeColumn();
                });

                tabla.Header(encabezado =>
                {
                    Encabezado(encabezado, "Fuente utilizada");
                    Encabezado(encabezado, "Cantidad (qq)");
                });

                foreach (AnalisisReporteMixtaFuente item in mixta.Fuentes)
                {
                    tabla.Cell().Element(CeldaMixta).Text(item.Fuente);
                    tabla.Cell().Element(CeldaMixta).AlignRight().Text($"{item.CantidadQq:N2}");
                }
            });

            columna.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(columnas =>
                {
                    columnas.RelativeColumn(1.5f);
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                    columnas.RelativeColumn();
                });

                tabla.Header(encabezado =>
                {
                    Encabezado(encabezado, "Elemento");
                    Encabezado(encabezado, "Requerimiento");
                    Encabezado(encabezado, "Aporte orgánico");
                    Encabezado(encabezado, "Déficit");
                    Encabezado(encabezado, "Sobrante");
                });

                foreach (AnalisisReporteMixtaDetalle item in mixta.Detalles)
                {
                    Celda(tabla, item.Elemento);
                    CeldaNumero(tabla, item.RequerimientoOriginal, "N2");
                    CeldaNumero(tabla, item.AporteOrganico, "N2");
                    CeldaNumero(tabla, item.Deficit, "N2");
                    CeldaNumero(tabla, item.Sobrante, "N2");
                }
            });
        }

        private static IContainer TituloSeccion(IContainer contenedor) =>
            contenedor
                .BorderBottom(2)
                .BorderColor(Verde)
                .PaddingBottom(4)
                .DefaultTextStyle(x =>
                    x.Bold().FontSize(12).FontColor(Verde));

        private static IContainer CeldaEncabezado(IContainer contenedor) =>
            contenedor
                .Background(Verde)
                .PaddingVertical(5)
                .PaddingHorizontal(5)
                .DefaultTextStyle(x =>
                    x.Bold().FontSize(8).FontColor(Colors.White));

        private static IContainer CeldaTabla(IContainer contenedor) =>
            contenedor
                .BorderBottom(0.5f)
                .BorderColor(GrisBorde)
                .PaddingVertical(4)
                .PaddingHorizontal(5);

        private static IContainer CeldaEtiqueta(IContainer contenedor) =>
            contenedor
                .Background(GrisFondo)
                .BorderBottom(0.5f)
                .BorderColor(GrisBorde)
                .Padding(5)
                .DefaultTextStyle(x => x.Bold().FontColor(GrisTexto));

        private static IContainer CeldaValor(IContainer contenedor) =>
            contenedor
                .BorderBottom(0.5f)
                .BorderColor(GrisBorde)
                .Padding(5);

        private static IContainer CeldaMixta(IContainer contenedor) =>
            contenedor
                .Background(AmarilloSuave)
                .BorderBottom(1)
                .BorderColor(Colors.White)
                .Padding(5);

        private static void AgregarPar(
            TableDescriptor tabla,
            string etiqueta1,
            string valor1,
            string etiqueta2,
            string valor2)
        {
            tabla.Cell().Element(CeldaEtiqueta).Text(etiqueta1);
            tabla.Cell().Element(CeldaValor).Text(ValorO(valor1, "-"));
            tabla.Cell().Element(CeldaEtiqueta).Text(etiqueta2);
            tabla.Cell().Element(CeldaValor).Text(ValorO(valor2, "-"));
        }

        private static void Encabezado(
            TableCellDescriptor encabezado,
            string texto) =>
            encabezado.Cell().Element(CeldaEncabezado).Text(texto);

        private static void Celda(TableDescriptor tabla, string texto) =>
            tabla.Cell().Element(CeldaTabla).Text(ValorO(texto, "-"));

        private static void CeldaNumero(
            TableDescriptor tabla,
            decimal numero,
            string formato) =>
            tabla.Cell().Element(CeldaTabla).AlignRight().Text(numero.ToString(formato));

        private static void CeldaMoneda(
            TableDescriptor tabla,
            decimal numero) =>
            tabla.Cell().Element(CeldaTabla).AlignRight().Text($"C$ {numero:N2}");

        private static string TextoNumero(decimal? valor, string? unidad = null)
        {
            if (!valor.HasValue)
                return "No disponible";

            return string.IsNullOrWhiteSpace(unidad)
                ? valor.Value.ToString("N2")
                : $"{valor.Value:N2} {unidad.Trim()}";
        }

        private static string ValorO(string? valor, string alternativo) =>
            string.IsNullOrWhiteSpace(valor) ? alternativo : valor.Trim();
    }
}
