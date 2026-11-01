using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SIVCR.Api.Services;

public sealed class CargaDatosService
{
    private readonly CargaDatosOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CargaDatosService> _logger;

    public CargaDatosService(
        IOptions<CargaDatosOptions> options,
        IWebHostEnvironment environment,
        ILogger<CargaDatosService> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<string> ProbarScriptAsync()
    {
        var script = Path.Combine(
            _environment.ContentRootPath,
            "Spark",
            "cargar_accidentes.py"
        );

        if (!File.Exists(_options.PythonExecutable))
            throw new FileNotFoundException(
                "No se encontró el ejecutable de Python configurado."
            );

        if (!File.Exists(script))
            throw new FileNotFoundException(
                "No se encontró cargar_accidentes.py."
            );

        var inicio = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            WorkingDirectory = _environment.ContentRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        inicio.ArgumentList.Add(script);
        inicio.ArgumentList.Add("--help");

        using var proceso = Process.Start(inicio)
            ?? throw new InvalidOperationException(
                "No se pudo iniciar Python."
            );

        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();

        using var limite = new CancellationTokenSource(
            TimeSpan.FromSeconds(30)
        );

        try
        {
            await proceso.WaitForExitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);

            await proceso.WaitForExitAsync();
            await Task.WhenAll(salida, errores);

            throw new TimeoutException(
                "Python no respondió dentro de 30 segundos."
            );
        }

        await Task.WhenAll(salida, errores);

        if (proceso.ExitCode != 0)
        {
            _logger.LogError(
                "Falló la prueba del script: {Error}",
                await errores
            );

            throw new InvalidOperationException(
                "Falló la prueba de Python. Revisa el registro de la API."
            );
        }

        _logger.LogInformation("Prueba del script completada.");
        return await salida;
    }
    public async Task<string> GuardarArchivoAsync(
        IFormFile archivo,
        CancellationToken cancellationToken)
    {
        const long limiteBytes = 50L * 1024 * 1024;

        if (archivo.Length == 0 || archivo.Length > limiteBytes)
            throw new ArgumentException(
                "El archivo debe tener contenido y no superar 50 MB."
            );

        var extension = Path.GetExtension(archivo.FileName)
            .ToLowerInvariant();

        if (extension is not ".csv" and not ".json" and not ".xlsx")
        {
            throw new ArgumentException(
                "Se permiten archivos CSV, JSON y Excel (.xlsx).");
        }

        var cargaId = Guid.NewGuid().ToString("N");

        var carpeta = Path.Combine(
            _options.DataDirectory,
            "cargas",
            cargaId
        );

        Directory.CreateDirectory(carpeta);

        var destino = Path.Combine(carpeta, "entrada" + extension);

        try
        {
            await using var salida = new FileStream(
                destino,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );

            await archivo.CopyToAsync(salida, cancellationToken);
        }
        catch
        {
            if (File.Exists(destino))
                File.Delete(destino);

            throw;
        }

        _logger.LogInformation(
            "Archivo recibido para la carga {CargaId}.",
            cargaId
        );

        return cargaId;
    }
    public async Task<string> ProcesarArchivoAsync(
        string cargaId,
        CancellationToken cancellationToken)
    {
        // Aceptar únicamente los identificadores generados al subir archivos.
        if (!Guid.TryParseExact(cargaId, "N", out var identificador))
            throw new ArgumentException("Identificador de carga inválido.");

        var carpeta = Path.Combine(
            _options.DataDirectory,
            "cargas",
            identificador.ToString("N")
        );

        var candidatos = new[]
        {
            Path.Combine(carpeta, "entrada.csv"),
            Path.Combine(carpeta, "entrada.json"),
            Path.Combine(carpeta, "entrada.xlsx")
        };

        var encontrados = candidatos.Where(File.Exists).ToArray();

        if (encontrados.Length == 0)
        {
            throw new FileNotFoundException(
                "No se encontró el archivo de esta carga.");
        }

        if (encontrados.Length != 1)
        {
            throw new InvalidOperationException(
                "La carga contiene más de un archivo de entrada.");
        }

        var entrada = encontrados[0];

        // Cada intento tendrá su propia carpeta de resultados.
        var salida = Path.Combine(
            carpeta,
            "resultado-" + Guid.NewGuid().ToString("N")
        );

        var script = Path.Combine(
            _environment.ContentRootPath,
            "Spark",
            "cargar_accidentes.py"
        );

        var inicio = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            WorkingDirectory = _environment.ContentRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        inicio.ArgumentList.Add(script);
        inicio.ArgumentList.Add("--entrada");
        inicio.ArgumentList.Add(entrada);
        inicio.ArgumentList.Add("--salida");
        inicio.ArgumentList.Add(salida);

        inicio.Environment["SPARK_LOCAL_IP"] = "127.0.0.1";
        inicio.Environment["PYSPARK_PYTHON"] = _options.PythonExecutable;

        using var proceso = Process.Start(inicio)
            ?? throw new InvalidOperationException(
                "No se pudo iniciar el procesamiento."
            );

        var textoSalida = proceso.StandardOutput.ReadToEndAsync();
        var textoErrores = proceso.StandardError.ReadToEndAsync();

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        limite.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            await proceso.WaitForExitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);

            await proceso.WaitForExitAsync();
            await Task.WhenAll(textoSalida, textoErrores);

            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                "El procesamiento superó los 10 minutos."
            );
        }

        await Task.WhenAll(textoSalida, textoErrores);

        if (proceso.ExitCode != 0)
        {
            var errores = await textoErrores;

            var marca = errores.Split('\n')
                .Select(linea => linea.Trim())
                .FirstOrDefault(linea => linea.StartsWith(
                    "SIVCR_VALIDACION:", StringComparison.Ordinal));

            var mensaje = marca switch
            {
                "SIVCR_VALIDACION:COLUMNAS_INCORRECTAS" =>
                    "El archivo no tiene los campos simples necesarios "
                    + "del perfil de accidentes COSEVI. Revisá los nombres "
                    + "de los campos. Si es CSV, usá punto y coma (;).",

                "SIVCR_VALIDACION:SIN_REGISTROS" =>
                    "El archivo contiene encabezados, pero no tiene "
                    + "registros para procesar.",

                "SIVCR_VALIDACION:EXCEL_INVALIDO" =>
                    "No se pudo leer el Excel con la estructura requerida. "
                    + "Usá un archivo .xlsx con una sola hoja, encabezados "
                    + "en la primera fila y valores sin fórmulas ni errores.",
                _ => null
            };

            if (mensaje != null)
            {
                _logger.LogWarning(
                    "Archivo rechazado en la carga {CargaId}: {Marca}",
                    cargaId, marca);

                throw new ArgumentException(mensaje);
            }

            _logger.LogError(
                "Falló Spark para la carga {CargaId}: {Error}",
                cargaId, errores);

            throw new InvalidOperationException(
                "Spark no pudo completar el procesamiento.");
        }

        if (!File.Exists(Path.Combine(salida, "_SUCCESS")))
            throw new InvalidOperationException(
                "Spark terminó sin confirmar la exportación."
            );

        _logger.LogInformation(
            "Procesamiento completado para la carga {CargaId}.",
            cargaId
        );

        return Path.GetFileName(salida);
    }
    private async Task<string> EjecutarPythonAsync(
        string nombreScript,
        string[] argumentos,
        TimeSpan tiempoMaximo,
        CancellationToken cancellationToken)
    {
        var script = Path.Combine(
            _environment.ContentRootPath,
            "Spark",
            nombreScript
        );

        if (!File.Exists(_options.PythonExecutable) || !File.Exists(script))
            throw new FileNotFoundException(
                "No se encontró Python o el script requerido."
            );

        var inicio = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            WorkingDirectory = _environment.ContentRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        inicio.ArgumentList.Add(script);

        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);

        inicio.Environment["SPARK_LOCAL_IP"] = "127.0.0.1";
        inicio.Environment["PYSPARK_PYTHON"] = _options.PythonExecutable;
        inicio.Environment["PYTHONUNBUFFERED"] = "1";

        using var proceso = Process.Start(inicio)
            ?? throw new InvalidOperationException(
                "No se pudo iniciar Python."
            );

        // Los scripts ejecutados por la API no reciben entrada por teclado.
        proceso.StandardInput.Close();

        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        limite.CancelAfter(tiempoMaximo);

        try
        {
            await proceso.WaitForExitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);

            await proceso.WaitForExitAsync();
            await Task.WhenAll(salida, errores);

            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                "El script superó el tiempo permitido."
            );
        }

        await Task.WhenAll(salida, errores);

        if (proceso.ExitCode != 0)
        {
            // Evitar registrar mensajes que puedan contener credenciales.
            _logger.LogError(
                "El script {Script} terminó con código {Codigo}.",
                nombreScript,
                proceso.ExitCode
            );

            throw new InvalidOperationException(
                "El script no pudo completar la operación."
            );
        }

        return await salida;
    }
    public async Task ImportarResultadoAsync(
        string cargaId,
        string carpetaResultado,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(cargaId, "N", out var id))
            throw new ArgumentException("Identificador de carga inválido.");

        const string prefijo = "resultado-";

        if (string.IsNullOrWhiteSpace(carpetaResultado)
            || !carpetaResultado.StartsWith(prefijo)
            || !Guid.TryParseExact(
                carpetaResultado[prefijo.Length..],
                "N",
                out var resultadoId))
        {
            throw new ArgumentException(
                "Nombre de carpeta de resultado inválido."
            );
        }

        var carpeta = Path.Combine(
            _options.DataDirectory,
            "cargas",
            id.ToString("N"),
            prefijo + resultadoId.ToString("N")
        );

        if (!File.Exists(Path.Combine(carpeta, "_SUCCESS"))
            || !Directory.EnumerateFiles(carpeta, "part-*.json").Any())
        {
            throw new FileNotFoundException(
                "No se encontró una exportación completa de esta carga."
            );
        }

        if (string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("SIVCR_MONGO_PASSWORD")))
        {
            throw new InvalidOperationException(
                "Falta configurar la contraseña de MongoDB en la API."
            );
        }

        await EjecutarPythonAsync(
            "importar_mongo.py",
            new[]
            {
                carpeta,
                "--carga-id",
                id.ToString("N")
            },
            TimeSpan.FromMinutes(10),
            cancellationToken
        );

        _logger.LogInformation(
            "Importación completada para la carga {CargaId}.",
            id.ToString("N")
        );
    }
    public async Task<JsonElement> LeerReporteAsync(
        string cargaId,
        string carpetaResultado,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(cargaId, "N", out var id))
            throw new ArgumentException("Identificador de carga inválido.");

        const string prefijo = "resultado-";

        if (string.IsNullOrWhiteSpace(carpetaResultado)
            || !carpetaResultado.StartsWith(prefijo)
            || !Guid.TryParseExact(
                carpetaResultado[prefijo.Length..],
                "N",
                out var resultadoId))
        {
            throw new ArgumentException(
                "Nombre de carpeta de resultado inválido."
            );
        }

        var carpeta = Path.Combine(
            _options.DataDirectory,
            "cargas",
            id.ToString("N"),
            prefijo + resultadoId.ToString("N")
        );

        var rutaReporte = Path.Combine(carpeta, "reporte.json");

        if (!File.Exists(Path.Combine(carpeta, "_SUCCESS"))
            || !File.Exists(rutaReporte))
        {
            throw new FileNotFoundException(
                "El reporte todavía no está disponible."
            );
        }

        var contenido = await File.ReadAllTextAsync(
            rutaReporte,
            cancellationToken
        );

        using var documento = JsonDocument.Parse(contenido);
        return documento.RootElement.Clone();
    }
}
