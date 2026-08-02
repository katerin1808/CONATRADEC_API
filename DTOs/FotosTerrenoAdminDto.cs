using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class FotoTerrenoAdminPaginaDto
    {
        public List<FotoTerrenoAdminItemDto> Items { get; set; } = [];
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
        public bool TienePaginaAnterior { get; set; }
        public bool TienePaginaSiguiente { get; set; }
        public FotoTerrenoAdminResumenDto Resumen { get; set; } = new();
    }

    public sealed class FotoTerrenoAdminItemDto
    {
        public int FotoTerrenoId { get; set; }
        public int TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string DireccionTerreno { get; set; } = string.Empty;
        public int? PropietarioId { get; set; }
        public string Propietario { get; set; } = string.Empty;
        public string IdentificacionPropietario { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string NombreArchivoOriginal { get; set; } = string.Empty;
        public DateTime FechaRegistroUtc { get; set; }
        public DateTime? FechaCaptura { get; set; }
        public bool EsPortada { get; set; }
        public bool Activo { get; set; }
        public bool TerrenoActivo { get; set; }
        public bool ArchivoExiste { get; set; }
        public bool EsHuerfana { get; set; }
    }

    public sealed class FotoTerrenoAdminResumenDto
    {
        public int Total { get; set; }
        public int Activas { get; set; }
        public int Inactivas { get; set; }
        public int Portadas { get; set; }
        public int ArchivosFaltantes { get; set; }
        public int Huerfanas { get; set; }
    }

    public sealed class FotoTerrenoAdminGuardarDto
    {
        [MaxLength(150)]
        public string? Titulo { get; set; }

        [MaxLength(600)]
        public string? Descripcion { get; set; }

        public DateTime? FechaCaptura { get; set; }
    }

    public sealed class FotoTerrenoAdminSubirDto
    {
        [Required]
        public IFormFile? Foto { get; set; }

        [Range(1, int.MaxValue)]
        public int TerrenoId { get; set; }

        [MaxLength(150)]
        public string? Titulo { get; set; }

        [MaxLength(600)]
        public string? Descripcion { get; set; }

        public DateTime? FechaCaptura { get; set; }
        public bool EstablecerComoPortada { get; set; }
    }

    public sealed class TerrenoFotoSelectorDto
    {
        public int TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public int? PropietarioId { get; set; }
        public string Propietario { get; set; } = string.Empty;
        public int CantidadFotosActivas { get; set; }
    }
}
