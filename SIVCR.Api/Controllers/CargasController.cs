using Microsoft.AspNetCore.Mvc;
using SIVCR.Api.Services;

namespace SIVCR.Api.Controllers;

[ApiController]
[Route("api/cargas")]
public sealed class CargasController : ControllerBase
{
    private readonly CargaDatosService _servicio;

    public CargasController(CargaDatosService servicio)
    {
        _servicio = servicio;
    }

    [HttpGet("prueba-script")]
    public async Task<IActionResult> ProbarScript()
    {
        var resultado = await _servicio.ProbarScriptAsync();
        return Content(resultado, "text/plain; charset=utf-8");
    }
    [HttpPost("archivo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(55_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 55_000_000)]
    public async Task<IActionResult> SubirArchivo(
        [FromForm] IFormFile archivo,
        CancellationToken cancellationToken)
    {
        try
        {
            var cargaId = await _servicio.GuardarArchivoAsync(
                archivo,
                cancellationToken
            );

            return Ok(new
            {
                cargaId,
                estado = "Recibido",
                mensaje = "Archivo guardado. Pendiente de procesamiento."
            });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { mensaje = error.Message });
        }
    }

    [HttpPost("{cargaId}/procesar")]
    public async Task<IActionResult> ProcesarArchivo(
        string cargaId,
        CancellationToken cancellationToken)
    {
        try
        {
            var carpetaResultado = await _servicio.ProcesarArchivoAsync(
                cargaId,
                cancellationToken
            );

            return Ok(new
            {
                cargaId,
                estado = "Procesado",
                carpetaResultado,
                mensaje = "JSON generados. Pendiente de importar a MongoDB."
            });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { mensaje = error.Message });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new
            {
                mensaje = "No se encontró el archivo o un recurso necesario."
            });
        }
        catch (TimeoutException error)
        {
            return StatusCode(504, new { mensaje = error.Message });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(500, new
            {
                mensaje = "No se pudo procesar la carga. Revisa el registro de la API."
            });
        }
    }
    [HttpPost("{cargaId}/importar")]
    public async Task<IActionResult> ImportarResultado(
        string cargaId,
        [FromQuery] string carpetaResultado,
        CancellationToken cancellationToken)
    {
        try
        {
            await _servicio.ImportarResultadoAsync(
                cargaId,
                carpetaResultado,
                cancellationToken
            );

            return Ok(new
            {
                cargaId,
                estado = "Importada",
                mensaje = "Documentos e historial guardados en MongoDB."
            });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { mensaje = error.Message });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new
            {
                mensaje = "No se encontró el resultado o un recurso necesario."
            });
        }
        catch (TimeoutException error)
        {
            return StatusCode(504, new { mensaje = error.Message });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(500, new
            {
                mensaje = "No se pudo importar la carga. Revisa la configuración y el registro de la API."
            });
        }
    }
    [HttpGet("{cargaId}/reporte")]
    public async Task<IActionResult> ObtenerReporte(
        string cargaId,
        [FromQuery] string carpetaResultado,
        CancellationToken cancellationToken)
    {
        try
        {
            var reporte = await _servicio.LeerReporteAsync(
                cargaId,
                carpetaResultado,
                cancellationToken
            );

            return Ok(reporte);
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { mensaje = error.Message });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new
            {
                mensaje = "El reporte todavía no está disponible."
            });
        }
        catch (System.Text.Json.JsonException)
        {
            return StatusCode(500, new
            {
                mensaje = "El archivo de reporte tiene un formato inválido."
            });
        }
    }
}
