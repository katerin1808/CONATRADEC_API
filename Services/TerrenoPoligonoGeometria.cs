using System.Text.Json;
using static CONATRADEC_API.DTOs.TerrenoPoligonoDto;

namespace CONATRADEC_API.Services;

/// <summary>
/// Valida y calcula la delimitación opcional de un terreno.
/// No modifica la latitud ni la longitud principal guardada en dbo.terreno.
/// </summary>
public static class TerrenoPoligonoGeometria
{
    private const double RadioTierraMetros = 6_378_137d;

    // Aproximación de una manzana nicaragüense en metros cuadrados.
    public const decimal MetrosCuadradosPorManzana = 7_042.25m;

    public sealed class Resultado
    {
        public List<VerticeDto> Vertices { get; init; } = [];
        public string GeoJson { get; init; } = string.Empty;
        public decimal AreaMetrosCuadrados { get; init; }
        public decimal AreaHectareas { get; init; }
        public decimal AreaManzanas { get; init; }
    }

    public static Resultado ValidarYCalcular(
        IReadOnlyCollection<VerticeDto>? vertices)
    {
        if (vertices is null)
            throw new ArgumentException("No se recibieron vértices.");

        List<VerticeDto> normalizados = Normalizar(vertices);

        if (normalizados.Count < 3)
        {
            throw new ArgumentException(
                "El polígono debe contener al menos tres vértices diferentes.");
        }

        if (normalizados.Count > 500)
        {
            throw new ArgumentException(
                "El polígono no puede superar 500 vértices.");
        }

        ValidarCoordenadas(normalizados);
        ValidarIntersecciones(normalizados);

        decimal areaMetros = decimal.Round(
            CalcularAreaMetrosCuadrados(normalizados),
            2);

        if (areaMetros <= 0)
        {
            throw new ArgumentException(
                "El polígono debe tener un área mayor que cero.");
        }

        decimal areaHectareas = decimal.Round(areaMetros / 10_000m, 4);
        decimal areaManzanas = decimal.Round(
            areaMetros / MetrosCuadradosPorManzana,
            4);

        return new Resultado
        {
            Vertices = normalizados,
            GeoJson = CrearGeoJson(normalizados),
            AreaMetrosCuadrados = areaMetros,
            AreaHectareas = areaHectareas,
            AreaManzanas = areaManzanas
        };
    }

    public static List<VerticeDto> LeerVertices(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            return [];

        try
        {
            using JsonDocument documento = JsonDocument.Parse(geoJson);
            JsonElement raiz = documento.RootElement;

            if (!raiz.TryGetProperty("coordinates", out JsonElement coordenadas) ||
                coordenadas.ValueKind != JsonValueKind.Array ||
                coordenadas.GetArrayLength() == 0)
            {
                return [];
            }

            var resultado = new List<VerticeDto>();

            foreach (JsonElement punto in coordenadas[0].EnumerateArray())
            {
                if (punto.ValueKind != JsonValueKind.Array ||
                    punto.GetArrayLength() < 2)
                {
                    continue;
                }

                resultado.Add(new VerticeDto
                {
                    Longitud = punto[0].GetDecimal(),
                    Latitud = punto[1].GetDecimal()
                });
            }

            return Normalizar(resultado);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool ContienePunto(
        IReadOnlyList<VerticeDto> vertices,
        decimal latitud,
        decimal longitud)
    {
        if (vertices.Count < 3)
            return true;

        double x = (double)longitud;
        double y = (double)latitud;
        bool dentro = false;

        for (int i = 0, j = vertices.Count - 1;
             i < vertices.Count;
             j = i++)
        {
            double xi = (double)vertices[i].Longitud;
            double yi = (double)vertices[i].Latitud;
            double xj = (double)vertices[j].Longitud;
            double yj = (double)vertices[j].Latitud;

            bool intersecta =
                ((yi > y) != (yj > y)) &&
                (x < (xj - xi) * (y - yi) /
                    ((yj - yi) == 0 ? double.Epsilon : yj - yi) + xi);

            if (intersecta)
                dentro = !dentro;
        }

        return dentro;
    }

    private static List<VerticeDto> Normalizar(
        IEnumerable<VerticeDto> vertices)
    {
        var resultado = new List<VerticeDto>();

        foreach (VerticeDto item in vertices)
        {
            var actual = new VerticeDto
            {
                Latitud = decimal.Round(item.Latitud, 8),
                Longitud = decimal.Round(item.Longitud, 8)
            };

            VerticeDto? anterior = resultado.LastOrDefault();

            if (anterior is not null && SonIguales(anterior, actual))
                continue;

            resultado.Add(actual);
        }

        if (resultado.Count > 1 &&
            SonIguales(resultado[0], resultado[^1]))
        {
            resultado.RemoveAt(resultado.Count - 1);
        }

        return resultado;
    }

    private static void ValidarCoordenadas(
        IEnumerable<VerticeDto> vertices)
    {
        foreach (VerticeDto vertice in vertices)
        {
            if (vertice.Latitud is < -90 or > 90)
                throw new ArgumentException("Una latitud está fuera del rango permitido.");

            if (vertice.Longitud is < -180 or > 180)
                throw new ArgumentException("Una longitud está fuera del rango permitido.");
        }
    }

    private static void ValidarIntersecciones(
        IReadOnlyList<VerticeDto> vertices)
    {
        int total = vertices.Count;

        for (int i = 0; i < total; i++)
        {
            VerticeDto a1 = vertices[i];
            VerticeDto a2 = vertices[(i + 1) % total];

            for (int j = i + 1; j < total; j++)
            {
                // Los segmentos vecinos comparten un vértice.
                if (j == i ||
                    j == (i + 1) % total ||
                    i == (j + 1) % total ||
                    (i == 0 && j == total - 1))
                {
                    continue;
                }

                VerticeDto b1 = vertices[j];
                VerticeDto b2 = vertices[(j + 1) % total];

                if (SegmentosIntersectan(a1, a2, b1, b2))
                {
                    throw new ArgumentException(
                        "El polígono contiene lados que se cruzan entre sí.");
                }
            }
        }
    }

    private static bool SegmentosIntersectan(
        VerticeDto p1,
        VerticeDto p2,
        VerticeDto q1,
        VerticeDto q2)
    {
        double o1 = Orientacion(p1, p2, q1);
        double o2 = Orientacion(p1, p2, q2);
        double o3 = Orientacion(q1, q2, p1);
        double o4 = Orientacion(q1, q2, p2);

        return Math.Sign(o1) != Math.Sign(o2) &&
               Math.Sign(o3) != Math.Sign(o4);
    }

    private static double Orientacion(
        VerticeDto a,
        VerticeDto b,
        VerticeDto c) =>
        ((double)b.Longitud - (double)a.Longitud) *
        ((double)c.Latitud - (double)a.Latitud) -
        ((double)b.Latitud - (double)a.Latitud) *
        ((double)c.Longitud - (double)a.Longitud);

    private static decimal CalcularAreaMetrosCuadrados(
        IReadOnlyList<VerticeDto> vertices)
    {
        double suma = 0;

        for (int i = 0; i < vertices.Count; i++)
        {
            VerticeDto actual = vertices[i];
            VerticeDto siguiente = vertices[(i + 1) % vertices.Count];

            double lon1 = AGradosRadianes((double)actual.Longitud);
            double lon2 = AGradosRadianes((double)siguiente.Longitud);
            double lat1 = AGradosRadianes((double)actual.Latitud);
            double lat2 = AGradosRadianes((double)siguiente.Latitud);

            suma += (lon2 - lon1) *
                    (2 + Math.Sin(lat1) + Math.Sin(lat2));
        }

        double area = Math.Abs(
            suma * RadioTierraMetros * RadioTierraMetros / 2d);

        return Convert.ToDecimal(area);
    }

    private static string CrearGeoJson(
        IReadOnlyList<VerticeDto> vertices)
    {
        var anillo = vertices
            .Select(x => new[] { x.Longitud, x.Latitud })
            .ToList();

        anillo.Add(new[]
        {
            vertices[0].Longitud,
            vertices[0].Latitud
        });

        return JsonSerializer.Serialize(new
        {
            type = "Polygon",
            coordinates = new[] { anillo }
        });
    }

    private static bool SonIguales(VerticeDto a, VerticeDto b) =>
        a.Latitud == b.Latitud &&
        a.Longitud == b.Longitud;

    private static double AGradosRadianes(double grados) =>
        grados * Math.PI / 180d;
}
