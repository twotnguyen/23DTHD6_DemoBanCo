using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace _23DTHD6_DemoBanCo.Controllers.api
{
    [Route("api/[controller]")]
    [ApiController]
    public class BanCoController : ControllerBase
    {
        private IWebHostEnvironment _environment;

        public BanCoController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        [HttpGet("getboard")]
        public IActionResult GetBoard() { 
             var filePath = Path.Combine(
                _environment.WebRootPath,
                "co_tuong_initial.json"
            );
            var json =  System.IO.File.ReadAllText(filePath);
            return Ok(json);
        }
    }
}
