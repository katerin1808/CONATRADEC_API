using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CONATRADEC_API.Models {



    [Table("CategoriaAlbumBotanico")] 
    public class CategoriaAlbumBotanico {
        [Key] 
        public int categoriaAlbumBotanicoId {get;set;}
        [Required,MaxLength(100)]
        public string nombreCategoria {get;set;}=string.Empty;
        [MaxLength(500)] 
        public string? descripcion {get;set;}
        public string? rutaImagenPortada { get; set; }
        public bool activo {get;set;}=true;
        public ICollection<AlbumBotanicoCafe> Registros {get;set;}=new List<AlbumBotanicoCafe>();
       

    } 

}