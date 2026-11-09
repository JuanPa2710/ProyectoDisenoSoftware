using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using System.Text.Json;
using System.Globalization;
using SIVCR.Web.Services;
using ClosedXML.Excel;

namespace SIVCR.Web.Controllers;

public sealed class ExploradorController : Controller {

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
    public async Task<IActionResult> Index(string? collection = null, [FromQuery] DashboardFiltrosViewModel? filtros = null) {
        if (filtros is null)
        {
            filtros = new DashboardFiltrosViewModel();
        }
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

                var filtroMongo = MongoFilterBuilder.CrearFiltro(filtros);
                var documentos = await coleccion
                    .Find(filtroMongo)
                    .ToListAsync();

                var incidencias = configuracion["MongoDB:Collection"];
                if (string.IsNullOrWhiteSpace(incidencias)) {
                    incidencias = "Incidencias";
                }
                if (nombreColeccion.Equals(incidencias, StringComparison.OrdinalIgnoreCase)) {
                    await HistorialConsultas.RegistrarAsync(
                        mongoClient, configuracion, filtros, documentos.Count);
                }

                return View(CrearConsultaDinamica(
                    nombreColeccion,
                    documentos
                ));
            } catch (MongoException) {
                ViewData["ErrorMongoDB"] = "No fue posible conectar o consultar MongoDB.";
                return View(new ExploradorConsultaViewModel());
            }
        }
        return await CargarDatosPruebaAsync(filtros);
    }

    private async Task<IActionResult> CargarDatosPruebaAsync(DashboardFiltrosViewModel? filtros = null){
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

        if (filtros is null)
        {
            filtros = new DashboardFiltrosViewModel();
        }

        var filas = registrosDisponibles
            .Where(registro => CoincideConFiltros(registro, filtros))
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

    private static bool CoincideConFiltros( RegistroVialViewModel registro, DashboardFiltrosViewModel filtros) {
        if (!CoincideTexto(registro.Provincia, filtros.Provincia)) return false;
        if (!CoincideTexto(registro.Canton, filtros.Canton)) return false;
        if (!CoincideTexto(registro.Ruta, filtros.Ruta)) return false;
        if (!CoincideTexto(registro.Franja, filtros.FranjaHoraria)) return false;
        if (!CoincideTexto(registro.Vehiculo, filtros.TipoAccidente)) return false;
        if (!CoincideTexto(registro.Clima, filtros.Clima)) return false;
        if (!CoincideTexto(registro.Calzada, filtros.EstadoCalzada)) return false;
        if (!CoincideTexto(registro.Severidad, filtros.Severidad)) return false;

        if (filtros.Anio.HasValue || filtros.Mes.HasValue || filtros.DiaSemana.HasValue)
        {
            if (!DateTime.TryParseExact(
                registro.Fecha,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var fecha))
            {
                return false;
            }
            if (filtros.Anio.HasValue && fecha.Year != filtros.Anio.Value) return false;
            if (filtros.Mes.HasValue && fecha.Month != filtros.Mes.Value) return false;
        }

        return true;
    }

    private static bool CoincideTexto(string? valor, string? filtro)
    {
        if (string.IsNullOrWhiteSpace(filtro))
        {
            return true;
        }
        return string.Equals(valor, filtro, StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> DatosExportacion(string? collection = null, [FromQuery] DashboardFiltrosViewModel? filtros = null){
        if (filtros is null) {
            filtros = new DashboardFiltrosViewModel();
        }
        var cargarDatos = configuracion.GetValue<bool>(
            "Explorador:CargarDatos"
        );

        // coneccion a Mongo
        // exporta los registros
        if (!cargarDatos) {
            var resultado = await CargarDatosPruebaAsync(filtros);
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
            .Find(MongoFilterBuilder.CrearFiltro(filtros))
            .ToListAsync();

        return Json(CrearConsultaDinamica(
            ObtenerColeccionPermitida(collection),
            documentos
        ));
    }

    [HttpPost]
    public async Task<IActionResult> ExportarXlsx(
        string? collection = null,
        [FromQuery] DashboardFiltrosViewModel? filtros = null,
        [FromBody] List<ExploradorColumnaViewModel>? columnas = null
    ) {
        if (filtros is null) {
            filtros = new DashboardFiltrosViewModel();
        }
        if (columnas is null) {
            columnas = [];
        }

        ExploradorConsultaViewModel consulta;
        var cargarDatos = configuracion.GetValue<bool>(
            "Explorador:CargarDatos"
        );

        if (!cargarDatos) {
            var resultado = await CargarDatosPruebaAsync(filtros);
            if (resultado is ViewResult vista) {
                consulta = vista.Model as ExploradorConsultaViewModel
                    ?? new ExploradorConsultaViewModel();
            } else {
                consulta = new ExploradorConsultaViewModel();
            }
        } else {
            if (mongoClient is null) {
                return BadRequest("MongoDB no está configurado.");
            }
                try {
                    var database = mongoClient.GetDatabase(
                        configuracion["MongoDB:Database"]
                    );
                    var nombreColeccion = ObtenerColeccionPermitida(collection);
                    var coleccion = database.GetCollection<BsonDocument>(
                        nombreColeccion
                    );
                    var documentos = await coleccion
                        .Find(MongoFilterBuilder.CrearFiltro(filtros))
                        .ToListAsync();
                    consulta = CrearConsultaDinamica(nombreColeccion, documentos);
                } catch (MongoException) {
                    return StatusCode(
                        503,
                        "No fue posible consultar MongoDB para generar el archivo Excel."
                    );
                }
        }

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Consulta");

        for (var indice = 0; indice < columnas.Count; indice++) {
            hoja.Cell(1, indice + 1).Value = columnas[indice].Etiqueta;
        }

        var encabezado = hoja.Row(1);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
        encabezado.Style.Font.FontColor = XLColor.White;

        for (var filaIndice = 0; filaIndice < consulta.Filas.Count; filaIndice++) {
            var fila = consulta.Filas[filaIndice];
            for (var columnaIndice = 0; columnaIndice < columnas.Count; columnaIndice++) {
                var campo = columnas[columnaIndice].Campo;
                if (fila.TryGetValue(campo, out var valor) && valor is not null) {
                    hoja.Cell(filaIndice + 2, columnaIndice + 1).Value = valor;
                }
            }
        }

        if (columnas.Count > 0) {
            hoja.Range(1, 1, Math.Max(1, consulta.Filas.Count + 1), columnas.Count)
                .SetAutoFilter();
        }
        hoja.Columns().AdjustToContents();

        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return File(
            salida.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "consulta-incidentes.xlsx"
        );
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

        return string.Join(" - ", partes);
    }

}
