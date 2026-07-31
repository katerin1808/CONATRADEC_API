using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs;

public sealed class PropietarioGuardarDto
{
    [Required, MaxLength(50)]
    public string Identificacion { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string NombreCompleto { get; set; } = string.Empty;

    [MaxLength(25)]
    public string? Telefono { get; set; }

    [EmailAddress, MaxLength(150)]
    public string? Correo { get; set; }

    [MaxLength(300)]
    public string? Direccion { get; set; }

    public bool Activo { get; set; } = true;
}

public sealed class VincularTerrenoPropietarioDto
{
    [Range(1, int.MaxValue)]
    public int TerrenoId { get; set; }
}

public sealed class VincularUsuarioPropietarioDto
{
    [Range(1, int.MaxValue)]
    public int UsuarioId { get; set; }

    [Range(1, int.MaxValue)]
    public int PropietarioId { get; set; }
}

public sealed class AsignarUsuarioTerrenoDto
{
    [Range(1, int.MaxValue)]
    public int UsuarioId { get; set; }

    [Range(1, int.MaxValue)]
    public int TerrenoId { get; set; }

    [Required, MaxLength(50)]
    public string TipoAsignacion { get; set; } = "TECNICO";

    public bool EsResponsablePrincipal { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }
}

public sealed class GuardarCoberturaTerritorialDto
{
    [Range(1, int.MaxValue)]
    public int UsuarioId { get; set; }

    [Required, MaxLength(30)]
    public string TipoCobertura { get; set; } = "DEPARTAMENTO";

    public int? DepartamentoId { get; set; }
    public int? MunicipioId { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }
}
