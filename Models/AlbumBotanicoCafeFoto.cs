using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CONATRADEC_API.Models { 
   
    
    
    [Table("AlbumBotanicoCafeFoto")]
    public class AlbumBotanicoCafeFoto { 
        [Key] 
        public int albumBotanicoCafeFotoId {get;set;}
        public int albumBotanicoCafeId {get;set;}
        [Required,MaxLength(500)] 
        public string rutaFoto {get;set;}=string.Empty; 
        [MaxLength(500)]
        public string? descripcionFoto {get;set;} 
        public bool esPortada {get;set;} 
        public int orden {get;set;} 
        public bool activo {get;set;}=true;
        [ForeignKey(nameof(albumBotanicoCafeId))]
        public AlbumBotanicoCafe AlbumBotanicoCafe {get;set;}=null!; } 
}