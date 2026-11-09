namespace SIVCR.Web.Models;

public sealed class EstadisticasCoseviViewModel
{
    public int Anio { get; set; } = 2024;
    public int? Mes { get; set; }
    public string? Provincia { get; set; }
    public string? Canton { get; set; }
    public string? Distrito { get; set; }
    public int Pagina { get; set; } = 1;
    public int TotalPaginas { get; set; }
    public long TotalAccidentes { get; set; }
    public long TotalPersonas { get; set; }
    public string? Error { get; set; }
    public List<EstadisticaCoseviFila> Filas { get; set; } = [];
}

public sealed class EstadisticaCoseviFila
{
    public int Mes { get; set; }
    public string Provincia { get; set; } = "";
    public string Canton { get; set; } = "";
    public string Distrito { get; set; } = "";
    public long? Accidentes { get; set; }
    public long? Personas { get; set; }
}