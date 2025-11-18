using System.ComponentModel.DataAnnotations;

public class AnalisisSueloDto
{
    public int analisisSueloId { get; set; }
    public DateOnly fechaAnalisisSuelo { get; set; }
    public string laboratorioAnalasisSuelo { get; set; } = null!;
    public string identificadorAnalisisSuelo { get; set; } = null!;
    public bool activo { get; set; }

    public List<AnalisisSueloElementoDto> elementos { get; set; } = new();
}

public class AnalisisSueloCrearDto
{
    [Required]
    public DateOnly fechaAnalisisSuelo { get; set; }

    [Required, MaxLength(80)]
    public string laboratorioAnalasisSuelo { get; set; } = null!;

    [Required, MaxLength(50)]
    public string identificadorAnalisisSuelo { get; set; } = null!;
}

public class AnalisisSueloElementoCrearDto
{
    [Required]
    public decimal cantidadElemento { get; set; }

    [Required]
    public int elementoQuimicosId { get; set; }

    [Required]
    public int unidadMedidaId { get; set; }
}

public class AnalisisSueloElementoDto
{
    public int analisisSueloElementoQuimicoId { get; set; }
    public decimal cantidadElemento { get; set; }
    public int elementoQuimicosId { get; set; }
    public string nombreElementoQuimico { get; set; } = null!;
    public string simboloElementoQuimico { get; set; } = null!;
    public int unidadMedidaId { get; set; }
    public string nombreUnidadMedida { get; set; } = null!;
    public bool activo { get; set; }
}
public class AnalisisSueloConElementosDto
{
    public int analisisSueloId { get; set; }
    public DateOnly fechaAnalisisSuelo { get; set; }
    public string laboratorioAnalasisSuelo { get; set; } = null!;
    public string identificadorAnalisisSuelo { get; set; } = null!;
    public bool activo { get; set; }

    public List<AnalisisSueloElementoDto> elementos { get; set; } = new();
}