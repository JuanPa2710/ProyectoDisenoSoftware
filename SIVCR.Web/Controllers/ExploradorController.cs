using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using System.Text.Json;
using System.Globalization;

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
    public async Task<IActionResult> Index(string? collection = null)
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
                var nombreColeccion = ObtenerColeccionPermitida(collection);
                var coleccion = database.GetCollection<BsonDocument>(
                    nombreColeccion
                );

                var documentos = await coleccion
                    .Find(FilterDefinition<BsonDocument>.Empty)
                    .Limit(configuracion.GetValue<int>(
                        "MongoDB:ExploradorCargaInicial",
                        1000
                    ))
                    .ToListAsync();

                return View(CrearConsultaDinamica(
                    nombreColeccion,
                    documentos
                ));
            } catch (MongoException) {
                ViewData["ErrorMongoDB"] =
                    "No fue posible conectar o consultar MongoDB.";
                return View(new ExploradorConsultaViewModel());
            }
        }
        return await CargarDatosPruebaAsync();
    }

    private async Task<IActionResult> CargarDatosPruebaAsync()
    {
        var rutaArchivo = configuracion["Explorador:DatosPruebaArchivo"];

        if (string.IsNullOrWhiteSpace(rutaArchivo)) {
            return View(new ExploradorConsultaViewModel());
        }

        var rutaCompleta = Path.Combine(
            entorno.ContentRootPath,
            rutaArchivo.Replace('/', Path.DirectorySeparatorChar)
        );

        if (!System.IO.File.Exists(rutaCompleta)) {
            return View(new ExploradorConsultaViewModel());
        }

        await using var archivo = System.IO.File.OpenRead(rutaCompleta);
        var registros = await JsonSerializer.DeserializeAsync<List<RegistroVialViewModel>>(
            archivo,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            }
        );

        var registrosDisponibles = registros;
        if (registrosDisponibles is null) {
            registrosDisponibles = [];
        }

        var filas = registrosDisponibles
            .Select(registro => new Dictionary<string, string?> {
                ["Id"] = registro.Id,
                ["Fecha"] = registro.Fecha,
                ["Hora"] = registro.Hora,
                ["Provincia"] = registro.Provincia,
                ["Canton"] = registro.Canton,
                ["Ruta"] = registro.Ruta,
                ["Vehiculo"] = registro.Vehiculo,
                ["Franja"] = registro.Franja,
                ["Clima"] = registro.Clima,
                ["Calzada"] = registro.Calzada,
                ["Tpr"] = registro.Tpr,
                ["Severidad"] = registro.Severidad
            })
            .ToList();

        return View(CrearConsultaDinamica("Incidencias", filas));
    }

    [HttpGet]
    public async Task<IActionResult> DatosExportacion(string? collection = null)
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
            return Json(new ExploradorConsultaViewModel());
        }
        if (mongoClient is null) {
            return Json(new ExploradorConsultaViewModel());
        }
        var database = mongoClient.GetDatabase(
            configuracion["MongoDB:Database"]
        );
        var coleccion = database.GetCollection<BsonDocument>(
            ObtenerColeccionPermitida(collection)
        );

        var documentos = await coleccion
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync();

        return Json(CrearConsultaDinamica(
            ObtenerColeccionPermitida(collection),
            documentos
        ));
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

        if (valor.IsString) {
            return valor.AsString;
        }
        if (valor.ToString() is not null) {
            return valor.ToString()!;
        }
        return "";
    }

    private string ObtenerColeccionPermitida(string? collection) {
        var colecciones = new[] {
            "Incidencias",
            "PersonasAccidentes",
            "RedVial"
        };
        var predeterminada = configuracion["MongoDB:Collection"];
        if (string.IsNullOrWhiteSpace(predeterminada)) {
            predeterminada = "Incidencias";
        }

        if (!string.IsNullOrWhiteSpace(collection)
            && colecciones.Contains(collection, StringComparer.OrdinalIgnoreCase)) {
            return colecciones.First(valor =>
                valor.Equals(collection, StringComparison.OrdinalIgnoreCase));
        }
        return predeterminada;
    }

    private static ExploradorConsultaViewModel CrearConsultaDinamica(
        string coleccion,
        IReadOnlyCollection<BsonDocument> documentos
    ) {
        var filas = documentos.Select(AplanarDocumento).ToList();
        return CrearConsultaDinamica(coleccion, filas);
    }

    private static ExploradorConsultaViewModel CrearConsultaDinamica(
        string coleccion,
        IReadOnlyCollection<Dictionary<string, string?>> filas
    ) {
        var columnas = new List<ExploradorColumnaViewModel>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filas) {
            foreach (var campo in fila.Keys) {
                if (!vistos.Add(campo)) continue;
                columnas.Add(new ExploradorColumnaViewModel {
                    Campo = campo,
                    Etiqueta = EtiquetaCampo(campo)
                });
            }
        }

        return new ExploradorConsultaViewModel {
            Coleccion = coleccion,
            Columnas = columnas,
            Filas = filas.ToList()
        };
    }

    private static Dictionary<string, string?> AplanarDocumento(BsonDocument documento) {
        var fila = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AplanarValores(documento, "", fila);
        return fila;
    }

    private static void AplanarValores(
        BsonDocument documento,
        string prefijo,
        IDictionary<string, string?> destino
    ) {
        foreach (var elemento in documento) {
            var campo = elemento.Name;
            if (!string.IsNullOrEmpty(prefijo)) {
                campo = $"{prefijo}.{elemento.Name}";
            }

            if (elemento.Value.IsBsonDocument) {
                AplanarValores(elemento.Value.AsBsonDocument, campo, destino);
            } else {
                destino[campo] = ConvertirValor(elemento.Value);
            }
        }
    }

    private static string? ConvertirValor(BsonValue valor) {
        if (valor == BsonNull.Value) return null;
        if (valor.IsString) return valor.AsString;
        if (valor.IsBoolean) {
            if (valor.AsBoolean) {
                return "Sí";
            } else {
                return "No";
            }
        }
        if (valor.IsBsonDateTime) {
            return valor.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
        return valor.ToString();
    }

    private static string EtiquetaCampo(string campo) {
        var nombres = campo.Split('.');
        var reemplazos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["Id"] = "ID",
            ["_id"] = "ID",
            ["anio"] = "Año",
            ["canton"] = "Cantón",
            ["provincia"] = "Provincia",
            ["kilometro"] = "Kilómetro",
            ["tipo_superficie"] = "Tipo de superficie",
            ["estado_superficie"] = "Estado de superficie",
            ["fecha_inventario_original"] = "Fecha de inventario"
        };

        var partes = new List<string>();
        foreach (var nombre in nombres) {
            if (reemplazos.TryGetValue(nombre, out var etiqueta)) {
                partes.Add(etiqueta);
            } else {
                partes.Add(new CultureInfo("es").TextInfo.ToTitleCase(
                    nombre.Replace("_", " ")
                ));
            }
        }

        return string.Join(" · ", partes);
    }

}
