using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using System.Text.Json;

namespace SIVCR.Web.Controllers;

public sealed class ExploradorController : Controller
{

    private readonly IConfiguration configuracion; 
    private readonly IWebHostEnvironment entorno;
    private readonly MongoClient? mongoClient;

    public ExploradorController(
        IConfiguration configuracion,
        IWebHostEnvironment entorno,
        MongoClient? mongoClient = null
    ) {
        this.configuracion = configuracion;
        this.entorno = entorno;
        this.mongoClient = mongoClient;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var cargarDatos = configuracion.GetValue<bool>(
            "Explorador:CargarDatos"
        );
        // coneccion a Mongo
        // selecciona MongoDB o el archivo de prueba 
        if (cargarDatos) {
            try {
                if (mongoClient is null) {
                    throw new InvalidOperationException(
                        "MongoDB no está configurado."
                    );
                }
                var database = mongoClient.GetDatabase(
                    configuracion["MongoDB:Database"]
                );
                var coleccion = database.GetCollection<BsonDocument>(
                    configuracion["MongoDB:Collection"]
                );

                var documentos = await coleccion
                    .Find(FilterDefinition<BsonDocument>.Empty)
                    .Limit(configuracion.GetValue<int>(
                        "MongoDB:ExploradorCargaInicial",
                        1000
                    ))
                    .ToListAsync();

                return View(documentos.Select(MapearRegistro).ToList());
            } catch (MongoException) {
                ViewData["ErrorMongoDB"] =
                    "No fue posible conectar o consultar MongoDB.";
                return View(new List<RegistroVialViewModel>());
            }
        }
        return await CargarDatosPruebaAsync();
    }

    private async Task<IActionResult> CargarDatosPruebaAsync()
    {
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

    [HttpGet]
    public async Task<IActionResult> DatosExportacion()
    {
        var cargarDatos = configuracion.GetValue<bool>(
            "Explorador:CargarDatos"
        );

        // coneccion a Mongo
        // exporta los registros
        if (!cargarDatos) {
            var resultado = await CargarDatosPruebaAsync();
            if (resultado is ViewResult vista) {
                return Json(vista.Model);
            }
            return Json(Array.Empty<RegistroVialViewModel>());
        }
        if (mongoClient is null) {
            return Json(Array.Empty<RegistroVialViewModel>());
        }
        var database = mongoClient.GetDatabase(
            configuracion["MongoDB:Database"]
        );
        var coleccion = database.GetCollection<BsonDocument>(
            configuracion["MongoDB:Collection"]
        );

        var documentos = await coleccion
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync();

        return Json(documentos.Select(MapearRegistro).ToList());
    }

    private static RegistroVialViewModel MapearRegistro(BsonDocument documento) {
        var tiempo = documento.GetValue("tiempo", new BsonDocument())
            .AsBsonDocument;
        var ubicacion = documento.GetValue("ubicacion", new BsonDocument())
            .AsBsonDocument;
        var condiciones = documento.GetValue("condiciones", new BsonDocument())
            .AsBsonDocument;

        var anio = ObtenerTexto(tiempo, "anio");
        var mes = ObtenerTexto(tiempo, "mes");
        var diaSemana = ObtenerTexto(tiempo, "dia_semana");
        var fecha = string.Join("-", new[] { anio, mes, diaSemana }
            .Where(valor => !string.IsNullOrWhiteSpace(valor)));

        var claseAccidente = ObtenerTexto(documento, "clase_accidente");
        var tipoAccidente = ObtenerTexto(documento, "tipo_accidente");

        return new RegistroVialViewModel {
            Id = ObtenerTexto(documento, "_id"),
            Fecha = fecha,
            Hora = ObtenerTexto(tiempo, "intervalo_hora"),
            Provincia = ObtenerTexto(ubicacion, "provincia"),
            Canton = ObtenerTexto(ubicacion, "canton"),
            Ruta = ObtenerTexto(ubicacion, "ruta"),
            Vehiculo = tipoAccidente,
            Franja = ObtenerTexto(documento, "franja_horaria"),
            Clima = ObtenerTexto(condiciones, "clima"),
            Calzada = ObtenerTexto(condiciones, "estado_calzada"),
            Tpr = "",
            Severidad = claseAccidente
        };
    }

    private static string ObtenerTexto(BsonDocument documento, string campo) {
        if (!documento.TryGetValue(campo, out var valor)) {
            return "";
        }

        return valor.IsString ? valor.AsString : (valor.ToString() ?? "");
    }
}
