using CONATRADEC_API.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Genera una copia visual derivada de la evidencia original. Nunca modifica
    /// ni sustituye RutaRelativa de la fotografía oficial.
    /// </summary>
    public sealed class DiagnosticoIAImagenMarcadaService
    {
        private readonly ImageStoragePathService storage;
        private readonly ILogger logger;

        public DiagnosticoIAImagenMarcadaService(
            ImageStoragePathService storage,
            ILogger logger)
        {
            this.storage = storage;
            this.logger = logger;
        }

        public async Task<ResultadoImagenMarcadaGenerada?> GenerarAsync(
            int inspeccionId,
            DiagnosticoIAImagen imagen,
            int revision,
            IReadOnlyCollection<ProveedorIADiagnosticoFoto>? diagnosticos,
            CancellationToken cancellationToken = default)
        {
            if (inspeccionId <= 0 ||
                imagen.DiagnosticoIAImagenId <= 0 ||
                revision <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    "La inspección, fotografía y revisión deben ser válidas.");
            }

            List<ProveedorIADiagnosticoFoto> localizables =
                (diagnosticos ?? [])
                    .Where(item => item.Lesiones?.Any(EsLesionValida) == true)
                    .ToList();

            if (localizables.Count == 0)
                return null;

            string rutaOriginal = storage.ResolverRutaPublica(
                imagen.RutaRelativa);

            if (!File.Exists(rutaOriginal))
            {
                throw new FileNotFoundException(
                    "No se encontró la evidencia original para generar la imagen marcada.",
                    rutaOriginal);
            }

            string carpetaRelativa =
                $"diagnostico-ia/inspeccion-{inspeccionId}/" +
                $"foto-{imagen.DiagnosticoIAImagenId}";
            string carpetaFisica = storage.ObtenerCarpeta(carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);

            string nombreArchivo = $"revision-{revision}.webp";
            string destino = Path.Combine(carpetaFisica, nombreArchivo);
            string temporal = destino + $".{Guid.NewGuid():N}.tmp";

            try
            {
                using Image<Rgba32> copia = await Image.LoadAsync<Rgba32>(
                    rutaOriginal,
                    cancellationToken);

                int grosor = Math.Clamp(
                    Math.Min(copia.Width, copia.Height) / 220,
                    3,
                    9);

                /*
                 * La colección se normaliza contra las dimensiones REALES de la
                 * imagen antes de dibujar. El controlador serializa DiagnosticosJson
                 * después de GenerarAsync, por lo que el contador mostrado por MAUI
                 * queda alineado con las regiones que efectivamente se renderizaron.
                 */
                foreach (ProveedorIADiagnosticoFoto diagnostico in localizables)
                {
                    int originales = diagnostico.Lesiones?.Count ?? 0;
                    diagnostico.Lesiones = PrepararLesionesRenderizables(
                        diagnostico.Lesiones,
                        copia.Width,
                        copia.Height);

                    if (diagnostico.Lesiones.Count != originales)
                    {
                        logger.LogInformation(
                            "Se normalizaron regiones del diagnóstico {Diagnostico}: {Originales} recibidas, {Finales} renderizables.",
                            diagnostico.Diagnostico,
                            originales,
                            diagnostico.Lesiones.Count);
                    }

                    foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                             (diagnostico.DiferencialesLocalizados ?? []))
                    {
                        int originalesDiferencial = diferencial.Lesiones?.Count ?? 0;
                        diferencial.Lesiones = PrepararLesionesRenderizables(
                            diferencial.Lesiones,
                            copia.Width,
                            copia.Height);

                        if (diferencial.Lesiones.Count != originalesDiferencial)
                        {
                            logger.LogInformation(
                                "Se normalizaron regiones del diferencial {Diferencial}: {Originales} recibidas, {Finales} renderizables.",
                                diferencial.Diagnostico,
                                originalesDiferencial,
                                diferencial.Lesiones.Count);
                        }
                    }
                }

                var cajasDiagnosticos = new List<CajaPixel>();

                // Primero se dibujan todos los diagnósticos confirmados/propuestos.
                foreach (ProveedorIADiagnosticoFoto diagnostico in localizables)
                {
                    Rgba32 color = ParseColor(diagnostico.ColorMarcador);

                    foreach (ProveedorIALesionFoto lesion in diagnostico.Lesiones)
                    {
                        if (!TryCrearCajaPixel(
                                lesion.Box2d,
                                copia.Width,
                                copia.Height,
                                out CajaPixel caja))
                        {
                            continue;
                        }

                        DibujarRectangulo(
                            copia,
                            caja,
                            color,
                            grosor,
                            margenInterior: 0);

                        cajasDiagnosticos.Add(caja);
                    }
                }

                /*
                 * Los diferenciales se dibujan después. Si una caja azul coincide
                 * o se superpone fuertemente con una roja/otro diagnóstico, se
                 * desplaza hacia dentro unos píxeles. De esta forma ambos bordes
                 * permanecen visibles y una marca azul nunca oculta una región que
                 * sigue contabilizándose como diagnóstico principal/adicional.
                 */
                foreach (ProveedorIADiagnosticoFoto diagnostico in localizables)
                {
                    foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                             (diagnostico.DiferencialesLocalizados ?? []))
                    {
                        Rgba32 colorDiferencial = ParseColor(diferencial.ColorMarcador);

                        foreach (ProveedorIALesionFoto lesion in diferencial.Lesiones)
                        {
                            if (!TryCrearCajaPixel(
                                    lesion.Box2d,
                                    copia.Width,
                                    copia.Height,
                                    out CajaPixel caja))
                            {
                                continue;
                            }

                            int margenInterior = ObtenerMargenInteriorDiferencial(
                                caja,
                                cajasDiagnosticos,
                                grosor);

                            DibujarRectangulo(
                                copia,
                                caja,
                                colorDiferencial,
                                grosor,
                                margenInterior);
                        }
                    }
                }

                await copia.SaveAsWebpAsync(
                    temporal,
                    new WebpEncoder { Quality = 84 },
                    cancellationToken);

                File.Move(temporal, destino, overwrite: true);

                string rutaRelativa =
                    $"{ImageStoragePathService.PrefijoPublico}" +
                    $"{carpetaRelativa}/{nombreArchivo}";

                logger.LogInformation(
                    "Imagen IA derivada generada para inspección {InspeccionId}, fotografía {FotografiaId}, revisión {Revision}.",
                    inspeccionId,
                    imagen.DiagnosticoIAImagenId,
                    revision);

                return new ResultadoImagenMarcadaGenerada(
                    rutaRelativa,
                    revision);
            }
            finally
            {
                if (File.Exists(temporal))
                {
                    try
                    {
                        File.Delete(temporal);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool EsLesionValida(ProveedorIALesionFoto lesion) =>
            lesion.Box2d is { Count: 4 } &&
            lesion.Box2d.All(value => value is >= 0 and <= 1000) &&
            lesion.Box2d[0] < lesion.Box2d[2] &&
            lesion.Box2d[1] < lesion.Box2d[3];

        private static List<ProveedorIALesionFoto> PrepararLesionesRenderizables(
            IEnumerable<ProveedorIALesionFoto>? lesiones,
            int anchoImagen,
            int altoImagen)
        {
            var resultado = new List<ProveedorIALesionFoto>();
            var cajasAceptadas = new List<CajaPixel>();

            foreach (ProveedorIALesionFoto lesion in lesiones ?? [])
            {
                if (!EsLesionValida(lesion) ||
                    !TryCrearCajaPixel(
                        lesion.Box2d,
                        anchoImagen,
                        altoImagen,
                        out CajaPixel caja))
                {
                    continue;
                }

                // Evita guardar dos regiones que terminarían viéndose como una sola.
                bool duplicadaVisual = cajasAceptadas.Any(existente =>
                    CalcularIoU(caja, existente) >= 0.92d);

                if (duplicadaVisual)
                    continue;

                resultado.Add(lesion);
                cajasAceptadas.Add(caja);
            }

            return resultado;
        }

        private static bool TryCrearCajaPixel(
            IReadOnlyList<int>? box,
            int anchoImagen,
            int altoImagen,
            out CajaPixel caja)
        {
            caja = default;

            if (box == null || box.Count != 4 ||
                anchoImagen <= 1 || altoImagen <= 1)
            {
                return false;
            }

            int ymin = Math.Clamp(Escalar(box[0], altoImagen - 1), 0, altoImagen - 1);
            int xmin = Math.Clamp(Escalar(box[1], anchoImagen - 1), 0, anchoImagen - 1);
            int ymax = Math.Clamp(Escalar(box[2], altoImagen - 1), 0, altoImagen - 1);
            int xmax = Math.Clamp(Escalar(box[3], anchoImagen - 1), 0, anchoImagen - 1);

            // Una caja de menos de 2 px por lado no se distingue de forma confiable.
            if (ymax - ymin < 2 || xmax - xmin < 2)
                return false;

            caja = new CajaPixel(ymin, xmin, ymax, xmax);
            return true;
        }

        private static int ObtenerMargenInteriorDiferencial(
            CajaPixel diferencial,
            IReadOnlyCollection<CajaPixel> diagnosticos,
            int grosor)
        {
            bool solapamientoImportante = diagnosticos.Any(diagnostico =>
                CalcularCoberturaSobreMenor(diferencial, diagnostico) >= 0.65d);

            if (!solapamientoImportante)
                return 0;

            int ladoMenor = Math.Min(diferencial.Ancho, diferencial.Alto);
            int margenDeseado = Math.Max(grosor * 2 + 2, 5);
            int margenMaximo = Math.Max(0, (ladoMenor - 4) / 4);

            return Math.Min(margenDeseado, margenMaximo);
        }

        private static double CalcularIoU(CajaPixel a, CajaPixel b)
        {
            double interseccion = CalcularAreaInterseccion(a, b);
            if (interseccion <= 0d)
                return 0d;

            double union = a.Area + b.Area - interseccion;
            return union <= 0d ? 0d : interseccion / union;
        }

        private static double CalcularCoberturaSobreMenor(CajaPixel a, CajaPixel b)
        {
            double interseccion = CalcularAreaInterseccion(a, b);
            if (interseccion <= 0d)
                return 0d;

            double menor = Math.Min(a.Area, b.Area);
            return menor <= 0d ? 0d : interseccion / menor;
        }

        private static double CalcularAreaInterseccion(CajaPixel a, CajaPixel b)
        {
            int ymin = Math.Max(a.YMin, b.YMin);
            int xmin = Math.Max(a.XMin, b.XMin);
            int ymax = Math.Min(a.YMax, b.YMax);
            int xmax = Math.Min(a.XMax, b.XMax);

            if (ymin >= ymax || xmin >= xmax)
                return 0d;

            return (ymax - ymin) * (xmax - xmin);
        }

        private static void DibujarRectangulo(
            Image<Rgba32> imagen,
            CajaPixel caja,
            Rgba32 color,
            int grosor,
            int margenInterior)
        {
            int ymin = Math.Clamp(caja.YMin + margenInterior, 0, imagen.Height - 1);
            int xmin = Math.Clamp(caja.XMin + margenInterior, 0, imagen.Width - 1);
            int ymax = Math.Clamp(caja.YMax - margenInterior, 0, imagen.Height - 1);
            int xmax = Math.Clamp(caja.XMax - margenInterior, 0, imagen.Width - 1);

            if (ymin >= ymax || xmin >= xmax)
                return;

            imagen.ProcessPixelRows(accessor =>
            {
                int grosorReal = Math.Min(
                    grosor,
                    Math.Max(1, Math.Min(ymax - ymin, xmax - xmin) / 3));

                for (int offset = 0; offset < grosorReal; offset++)
                {
                    int ySuperior = Math.Clamp(ymin + offset, 0, imagen.Height - 1);
                    int yInferior = Math.Clamp(ymax - offset, 0, imagen.Height - 1);
                    Span<Rgba32> filaSuperior = accessor.GetRowSpan(ySuperior);
                    Span<Rgba32> filaInferior = accessor.GetRowSpan(yInferior);

                    for (int x = xmin; x <= xmax; x++)
                    {
                        filaSuperior[x] = color;
                        filaInferior[x] = color;
                    }

                    int xIzquierdo = Math.Clamp(xmin + offset, 0, imagen.Width - 1);
                    int xDerecho = Math.Clamp(xmax - offset, 0, imagen.Width - 1);

                    for (int y = ymin; y <= ymax; y++)
                    {
                        Span<Rgba32> fila = accessor.GetRowSpan(y);
                        fila[xIzquierdo] = color;
                        fila[xDerecho] = color;
                    }
                }
            });
        }

        private readonly record struct CajaPixel(
            int YMin,
            int XMin,
            int YMax,
            int XMax)
        {
            public int Ancho => XMax - XMin;
            public int Alto => YMax - YMin;
            public double Area => Math.Max(0, Ancho) * Math.Max(0, Alto);
        }

        private static int Escalar(int normalizado, int maximo) =>
            (int)Math.Round(
                Math.Clamp(normalizado, 0, 1000) / 1000d * maximo,
                MidpointRounding.AwayFromZero);

        private static Rgba32 ParseColor(string? hexadecimal)
        {
            string valor = (hexadecimal ?? string.Empty).Trim().TrimStart('#');

            if (valor.Length == 6 &&
                byte.TryParse(valor[..2],
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out byte r) &&
                byte.TryParse(valor.Substring(2, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out byte g) &&
                byte.TryParse(valor.Substring(4, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out byte b))
            {
                return new Rgba32(r, g, b, 255);
            }

            return new Rgba32(229, 57, 53, 255);
        }
    }

    public sealed record ResultadoImagenMarcadaGenerada(
        string RutaRelativa,
        int Revision);
}
