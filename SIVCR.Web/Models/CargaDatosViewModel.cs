using System.Text.Json.Serialization;

namespace SIVCR.Web.Models;

public sealed class CargaDatosViewModel
{
    public string? CargaId { get; set; }
    public string? CarpetaResultado { get; set; }
    public string? NombreArchivo { get; set; }
    public string? Error { get; set; }
    public ReporteCargaViewModel? Reporte { get; set; }
    public bool Importada { get; set; }
}

public sealed class ReporteCargaViewModel
{
    [JsonPropertyName("perfil")]
    public string Perfil { get; set; } = "";

    [JsonPropertyName("registros_leidos")]
    public long RegistrosLeidos { get; set; }

    [JsonPropertyName("documentos_generados")]
    public long DocumentosGenerados { get; set; }

    [JsonPropertyName("registros_sin_anio_o_mes")]
    public long SinAnioOMes { get; set; }

    [JsonPropertyName("registros_sin_dia_semana")]
    public long SinDiaSemana { get; set; }

    [JsonPropertyName("registros_sin_kilometro_numerico")]
    public long SinKilometroNumerico { get; set; }

    [JsonPropertyName("notas")]
    public string[] Notas { get; set; } = [];
}
