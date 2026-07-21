using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CONATRADEC_API.Models {
    [
        Table("AlbumBotanicoCafe")]
    public class AlbumBotanicoCafe { 
        [Key] public int albumBotanicoCafeId {get;set;} 
        public int categoriaAlbumBotanicoId {get;set;}
        [Required,MaxLength(200)]
        public string titulo {get;set;}=string.Empty; 
        [MaxLength(200)] 
        public string? nombreCientifico {get;set;}
        [Required]
        public string descripcion {get;set;}=string.Empty;
        public string? caracteristicas {get;set;} 
        public string? sintomas {get;set;} 
        public string? causas {get;set;} 
        public string? recomendaciones {get;set;}
        public string? observaciones {get;set;}
        public bool activo {get;set;}=true;
        public DateTime fechaCreacion {get;set;}=DateTime.Now;
        [ForeignKey(nameof(categoriaAlbumBotanicoId))] 
        public CategoriaAlbumBotanico Categoria {get;set;}=null!;
        public ICollection<AlbumBotanicoCafeFoto> Fotos {get;set;}=new List<AlbumBotanicoCafeFoto>(); }
}