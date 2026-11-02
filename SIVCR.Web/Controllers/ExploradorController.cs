using Microsoft.AspNetCore.Mvc;
using SIVCR.Web.Models;
using System.Text.Json;

namespace SIVCR.Web.Controllers;

// nota: por ahora solo carga los datos de prueba

public sealed class ExploradorController : Controller
{

    private readonly IConfiguration configuracion; 
    private readonly IWebHostEnvironment entorno;

    public ExploradorController(
        IConfiguration configuracion,
        IWebHostEnvironment entorno
    ) {
        this.configuracion = configuracion;
        this.entorno = entorno;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var usarDatosPrueba = configuracion.GetValue<bool>(
            "Explorador:UsarDatosPrueba"
        );
        if (!usarDatosPrueba) {
            return View(new List<RegistroVialViewModel>());
        }
        var rutaArchivo = configuracion["Explorador:DatosPruebaArchivo"];

        if (string.IsNullOrWhiteSpace(rutaArchivo)) {
            return View(new List<RegistroVialViewModel>());
        }

        var rutaCompleta = Path.Combine(
            entorno.ContentRootPath,
            rutaArchivo.Replace('/', Path.DirectorySeparatorChar)
        );

        if (!System.IO.File.Exists(rutaCompleta)) {
            return View(new List<RegistroVialViewModel>());
        }

        await using var archivo = System.IO.File.OpenRead(rutaCompleta);
        var registros = await JsonSerializer.DeserializeAsync<List<RegistroVialViewModel>>(
            archivo,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            }
        );

        return View(registros ?? new List<RegistroVialViewModel>());
    }
}
