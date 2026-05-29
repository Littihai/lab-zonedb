using Microsoft.AspNetCore.Mvc;

namespace NexAuthDb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            success = true,
            message = "NexAuth API Working"
        });
    }
}