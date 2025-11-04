using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/usuarios")]

    public class UsuarioController : Controller
    {


        private readonly DBContext _db;
        public UsuarioController(DBContext db) => _db = db;

       

       
    }
    }


