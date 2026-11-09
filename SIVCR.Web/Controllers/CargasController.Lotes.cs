using Microsoft.AspNetCore.Mvc;
using SIVCR.Web.Models;

namespace SIVCR.Web.Controllers;

public sealed partial class CargasController
{
    [HttpGet]
    public IActionResult Lote() => View();

    // Cada petición contiene un archivo; se conservan los límites existentes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(55_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 55_000_000)]
    public async Task<IActionResult> SubirLoteArchivo(
        IFormFile? archivo, CancellationToken cancellationToken)
        => RespuestaLote(await Subir(archivo, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcesarLoteArchivo(
        string cargaId, string? nombreArchivo, CancellationToken cancellationToken)
        => RespuestaLote(await Procesar(cargaId, nombreArchivo, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarLoteArchivo(
        string cargaId, string carpetaResultado, string? nombreArchivo,
        CancellationToken cancellationToken)
        => RespuestaLote(await Importar(
            cargaId, carpetaResultado, nombreArchivo, cancellationToken));

    private IActionResult RespuestaLote(IActionResult resultado)
    {
        if (resultado is not ViewResult { Model: CargaDatosViewModel modelo })
            return StatusCode(502, new { mensaje = "Respuesta de carga inesperada." });

        if (!string.IsNullOrWhiteSpace(modelo.Error))
            return BadRequest(new { mensaje = modelo.Error });

        return Json(new
        {
            cargaId = modelo.CargaId,
            carpetaResultado = modelo.CarpetaResultado,
            nombreArchivo = modelo.NombreArchivo,
            reporte = modelo.Reporte,
            importada = modelo.Importada
        });
    }
}