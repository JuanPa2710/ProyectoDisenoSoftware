namespace SIVCR.Web.Models;

public sealed class DashboardViewModel
{
    public DashboardFiltrosViewModel Filtros { get; set; } = new();
    public DashboardOpcionesFiltros Opciones { get; set; } = new();
    public string? ErrorMongoDB { get; set; }
    public int TotalIncidentes { get; set; }
    public IReadOnlyList<DashboardKpiViewModel> Indicadores { get; set; } = [];
    public IReadOnlyList<DashboardDatoViewModel> Provincias { get; set; } = [];
    public IReadOnlyList<string> FranjasMatriz { get; set; } = [];
    public IReadOnlyList<DashboardFilaTemporalViewModel> MatrizTemporal { get; set; } = [];
    public IReadOnlyList<DashboardDatoViewModel> TiposAccidente { get; set; } = [];
    public IReadOnlyList<DashboardDatoViewModel> Severidades { get; set; } = [];
    public IReadOnlyList<DashboardRutaViewModel> Rutas { get; set; } = [];
    public string TipoAccidenteDonutCss { get; set; } = "#3d3d3d";
    public string SeveridadPieCss { get; set; } = "#3d3d3d";
}

public sealed class DashboardFiltrosViewModel
{
    public string? Provincia { get; set; }
    public string? Canton { get; set; }
    public string? Distrito { get; set; }
    public string? Ruta { get; set; }
    public int? Anio { get; set; }
    public int? Mes { get; set; }
    public int? DiaSemana { get; set; }
    public string? FranjaHoraria { get; set; }
    public string? TipoAccidente { get; set; }
    public string? Clima { get; set; }
    public string? EstadoCalzada { get; set; }
    public string? Severidad { get; set; }
}

public sealed class DashboardOpcionesFiltros
{
    public IReadOnlyList<string> Provincias { get; set; } = [];
    public IReadOnlyList<string> Cantones { get; set; } = [];
    public IReadOnlyList<string> Distritos { get; set; } = [];
    public IReadOnlyList<string> Rutas { get; set; } = [];
    public IReadOnlyList<int> Anios { get; set; } = [];
    public IReadOnlyList<int> Meses { get; set; } = [];
    public IReadOnlyList<int> DiasSemana { get; set; } = [];
    public IReadOnlyList<string> FranjasHorarias { get; set; } = [];
    public IReadOnlyList<string> TiposAccidente { get; set; } = [];
    public IReadOnlyList<string> Climas { get; set; } = [];
    public IReadOnlyList<string> EstadosCalzada { get; set; } = [];
    public IReadOnlyList<string> Severidades { get; set; } = [];
}

public sealed class DashboardKpiViewModel
{
    public string Titulo { get; set; } = "";
    public string Valor { get; set; } = "";
    public string Detalle { get; set; } = "";
    public string Formula { get; set; } = "";
}

public sealed class DashboardDatoViewModel
{
    public string Etiqueta { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Porcentaje { get; set; }
}

public sealed class DashboardFilaTemporalViewModel
{
    public string Dia { get; set; } = "";
    public IReadOnlyList<int> Valores { get; set; } = [];
}

public sealed class DashboardRutaViewModel
{
    public string Ruta { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal PorcentajeAcumulado { get; set; }
}

public sealed class GuardarFiltrosRequest
{
    public string? Nombre { get; set; }
    public DashboardFiltrosViewModel? Filtros { get; set; }
}