using System.Text.Json;
using SIVCR.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace SIVCR.Web.Controllers;

public sealed class CargasController : Controller
{
    private readonly IHttpClientFactory _clientes;

    public CargasController(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new CargaDatosViewModel());
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(55_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 55_000_000)]
    public async Task<IActionResult> Subir(
        IFormFile? archivo,
        CancellationToken cancellationToken)
    {
        var modelo = new CargaDatosViewModel();

        if (archivo == null || archivo.Length == 0)
        {
            modelo.Error = "Seleccioná un archivo con contenido.";
            return View("Index", modelo);
        }

        if (archivo.Length > 50L * 1024 * 1024)
        {
            modelo.Error = "El archivo no puede superar 50 MB.";
            return View("Index", modelo);
        }

        var extension = Path.GetExtension(archivo.FileName)
            .ToLowerInvariant();

        if (extension is not ".csv" and not ".json" and not ".xlsx")
        {
            modelo.Error = "Se permiten archivos CSV, JSON y Excel (.xlsx).";
            return View("Index", modelo);
        }

        modelo.NombreArchivo = Path.GetFileName(
            archivo.FileName.Replace('\\', '/')
        );

        try
        {
            var cliente = _clientes.CreateClient("CargaDatosApi");

            using var formulario = new MultipartFormDataContent();
            using var contenido = new StreamContent(archivo.OpenReadStream());

            formulario.Add(
                contenido,
                "archivo",
                modelo.NombreArchivo
            );

            using var respuesta = await cliente.PostAsync(
                "api/cargas/archivo",
                formulario,
                cancellationToken
            );

            if (!respuesta.IsSuccessStatusCode)
            {
                modelo.Error = "La API no pudo recibir el archivo.";
                return View("Index", modelo);
            }

            var texto = await respuesta.Content.ReadAsStringAsync(
                cancellationToken
            );

            using var documento = JsonDocument.Parse(texto);

            if (!documento.RootElement.TryGetProperty(
                    "cargaId", out var propiedad)
                || propiedad.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(
                    propiedad.GetString(), "N", out var id))
            {
                modelo.Error = "La API devolvió una respuesta inesperada.";
                return View("Index", modelo);
            }

            modelo.CargaId = id.ToString("N");
        }
        catch (HttpRequestException)
        {
            modelo.Error = "No se pudo contactar con la API de cargas.";
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            modelo.Error = "Se agotó el tiempo de espera al enviar el archivo.";
        }
        catch (JsonException)
        {
            modelo.Error = "La API devolvió una respuesta que no se pudo leer.";
        }

        return View("Index", modelo);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Procesar(
        string cargaId,
        string? nombreArchivo,
        CancellationToken cancellationToken)
    {
        var modelo = new CargaDatosViewModel
        {
            NombreArchivo = nombreArchivo
        };

        if (!Guid.TryParseExact(cargaId, "N", out var id))
        {
            modelo.Error = "El identificador de carga no es válido.";
            return View("Index", modelo);
        }

        modelo.CargaId = id.ToString("N");

        try
        {
            var cliente = _clientes.CreateClient("CargaDatosApi");

            using var respuesta = await cliente.PostAsync(
                $"api/cargas/{modelo.CargaId}/procesar",
                null,
                cancellationToken
            );

            if (!respuesta.IsSuccessStatusCode)
            {
                modelo.Error =
                    "No se pudo procesar el archivo. Intentá nuevamente "
                    + "o revisá el registro de la API.";

                if (respuesta.StatusCode ==
                    System.Net.HttpStatusCode.BadRequest)
                {
                    var detalle = await respuesta.Content
                        .ReadAsStringAsync(cancellationToken);

                    try
                    {
                        using var errorApi = JsonDocument.Parse(detalle);

                        if (errorApi.RootElement.TryGetProperty(
                                "mensaje", out var mensaje)
                            && mensaje.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(mensaje.GetString()))
                        {
                            modelo.Error = mensaje.GetString();
                        }
                    }
                    catch (JsonException)
                    {
                        // Conservar el mensaje general.
                    }
                }

                return View("Index", modelo);
            }

            var texto = await respuesta.Content.ReadAsStringAsync(
                cancellationToken
            );

            using var documento = JsonDocument.Parse(texto);

            if (!documento.RootElement.TryGetProperty(
                    "carpetaResultado", out var propiedad)
                || propiedad.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(propiedad.GetString()))
            {
                modelo.Error = "La API no devolvió la carpeta de resultados.";
                return View("Index", modelo);
            }

            modelo.CarpetaResultado = propiedad.GetString();

            var rutaReporte =
                $"api/cargas/{modelo.CargaId}/reporte"
                + "?carpetaResultado="
                + Uri.EscapeDataString(modelo.CarpetaResultado!);

            using var respuestaReporte = await cliente.GetAsync(
                rutaReporte,
                cancellationToken
            );

            if (!respuestaReporte.IsSuccessStatusCode)
            {
                modelo.Error =
                    "El procesamiento terminó, pero no se pudo consultar el reporte.";
                return View("Index", modelo);
            }

            var textoReporte = await respuestaReporte.Content.ReadAsStringAsync(
                cancellationToken
            );

            modelo.Reporte = JsonSerializer.Deserialize<ReporteCargaViewModel>(
                textoReporte
            );

            if (modelo.Reporte == null)
                modelo.Error = "La API devolvió un reporte vacío.";
        }
        catch (HttpRequestException)
        {
            modelo.Error = "No se pudo contactar con la API de cargas.";
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            modelo.Error = "Se agotó el tiempo de espera del procesamiento.";
        }
        catch (JsonException)
        {
            modelo.Error = "No se pudo interpretar la respuesta de la API.";
        }

        return View("Index", modelo);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Importar(
        string cargaId,
        string carpetaResultado,
        string? nombreArchivo,
        CancellationToken cancellationToken)
    {
        var modelo = new CargaDatosViewModel
        {
            NombreArchivo = nombreArchivo
        };

        if (!Guid.TryParseExact(cargaId, "N", out var id)
            || string.IsNullOrWhiteSpace(carpetaResultado)
            || !carpetaResultado.StartsWith(
                "resultado-", StringComparison.Ordinal)
            || !Guid.TryParseExact(
                carpetaResultado["resultado-".Length..], "N", out _))
        {
            modelo.Error = "La carga o la carpeta de resultados no es válida.";
            return View("Index", modelo);
        }

        modelo.CargaId = id.ToString("N");
        modelo.CarpetaResultado = carpetaResultado;

        try
        {
            var cliente = _clientes.CreateClient("CargaDatosApi");
            var consulta = "?carpetaResultado="
                + Uri.EscapeDataString(carpetaResultado);
            var ruta = $"api/cargas/{modelo.CargaId}";

            // Consultar el reporte real antes de importar.
            using var respuestaReporte = await cliente.GetAsync(
                ruta + "/reporte" + consulta, cancellationToken);

            if (!respuestaReporte.IsSuccessStatusCode)
            {
                modelo.Error = "No se pudo recuperar el reporte de esta carga.";
                return View("Index", modelo);
            }

            var textoReporte = await respuestaReporte.Content
                .ReadAsStringAsync(cancellationToken);

            modelo.Reporte = JsonSerializer.Deserialize<ReporteCargaViewModel>(
                textoReporte);

            if (modelo.Reporte == null)
            {
                modelo.Error = "El reporte de esta carga está vacío.";
                return View("Index", modelo);
            }

            if (!modelo.Reporte.PuedeImportar
                || modelo.Reporte.RegistrosAceptados <= 0)
            {
                modelo.Error =
                    "No hay registros válidos para importar. "
                    + "Revisá el reporte y corregí el archivo.";

                return View("Index", modelo);
            }

            if (modelo.Reporte.DocumentosGenerados
                != modelo.Reporte.RegistrosAceptados)
            {
                modelo.Error =
                    "El reporte presenta conteos inconsistentes. "
                    + "Volvé a procesar el archivo.";

                return View("Index", modelo);
            }

            using var respuesta = await cliente.PostAsync(
                ruta + "/importar" + consulta, null, cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                modelo.Error =
                    "La API no confirmó la importación. Revisá su consola antes de reintentar.";
                return View("Index", modelo);
            }

            var texto = await respuesta.Content.ReadAsStringAsync(
                cancellationToken);

            using var documento = JsonDocument.Parse(texto);

            if (!documento.RootElement.TryGetProperty("estado", out var estado)
                || estado.ValueKind != JsonValueKind.String
                || estado.GetString() != "Importada")
            {
                modelo.Error =
                    "La respuesta no permite confirmar si terminó la importación.";
                return View("Index", modelo);
            }

            modelo.Importada = true;
        }
        catch (HttpRequestException)
        {
            modelo.Error =
                "Se perdió la comunicación con la API. Verificá la carga antes de reintentar.";
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            modelo.Error =
                "Se agotó la espera. La importación podría haber comenzado; revisá la API antes de reintentar.";
        }
        catch (JsonException)
        {
            modelo.Error = "No se pudo interpretar la respuesta de la API.";
        }

        return View("Index", modelo);
    }
    [HttpGet]
    public async Task<IActionResult> DescargarReporte(
        string cargaId,
        string carpetaResultado,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(cargaId, "N", out var id)
            || string.IsNullOrWhiteSpace(carpetaResultado)
            || !carpetaResultado.StartsWith(
                "resultado-", StringComparison.Ordinal)
            || !Guid.TryParseExact(
                carpetaResultado["resultado-".Length..], "N", out _))
        {
            return BadRequest("La carga o la carpeta no es válida.");
        }

        try
        {
            var cliente = _clientes.CreateClient("CargaDatosApi");

            var ruta = $"api/cargas/{id:N}/reporte"
                + "?carpetaResultado="
                + Uri.EscapeDataString(carpetaResultado);

            using var respuesta = await cliente.GetAsync(
                ruta, cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)respuesta.StatusCode,
                    "No se pudo recuperar el reporte.");
            }

            var texto = await respuesta.Content.ReadAsStringAsync(
                cancellationToken);

            // Comprobar que es JSON y darle formato legible.
            using var documento = JsonDocument.Parse(texto);

            var contenido = JsonSerializer.SerializeToUtf8Bytes(
                documento.RootElement,
                new JsonSerializerOptions { WriteIndented = true });

            return File(
                contenido,
                "application/json",
                $"reporte-carga-{id:N}.json");
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, "No se pudo contactar con la API.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, "Se agotó la espera del reporte.");
        }
        catch (JsonException)
        {
            return StatusCode(502, "La API devolvió un reporte inválido.");
        }
    }
}
