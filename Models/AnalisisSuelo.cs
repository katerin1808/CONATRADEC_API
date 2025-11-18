using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("analisisSuelo", Schema = "dbo")]
public class AnalisisSuelo
{
    [Key]
    public int analisisSueloId { get; set; }

    [Required]
    public DateOnly fechaAnalisisSuelo { get; set; }

    [Required, MaxLength(80)]
    public string laboratorioAnalasisSuelo { get; set; } = null!;

    [Required, MaxLength(50)]
    public string identificadorAnalisisSuelo { get; set; } = null!;

    public bool activo { get; set; } = true;

    // 🔗 Relación con elementos químicos
    public ICollection<AnalisisSueloElementoQuimico> Elementos { get; set; } = new List<AnalisisSueloElementoQuimico>();
}
