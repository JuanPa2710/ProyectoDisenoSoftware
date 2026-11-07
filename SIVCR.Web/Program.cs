using MongoDB.Driver;


var builder = WebApplication.CreateBuilder(args);

var cargarDatos = builder.Configuration.GetValue<bool>(
    "Explorador:CargarDatos"
);

// coneccion a Mongo
// crea el cliente
if (cargarDatos) {
    var mongoConfiguration = builder.Configuration.GetSection("MongoDB");
    var mongoPassword = Environment.GetEnvironmentVariable(
        "SIVCR_MONGO_PASSWORD"
    );

    var modoConexion = Environment.GetEnvironmentVariable(
        "SIVCR_MONGO_MODE"
    ) ?? mongoConfiguration["ModoConexion"] ?? "TUNNEL";
    modoConexion = modoConexion.Trim().ToUpperInvariant();

    var hostMongoDB = modoConexion switch {
        "VM" => mongoConfiguration["HostVM"],
        "TUNNEL" => mongoConfiguration["HostTunnel"],
        _ => throw new InvalidOperationException(
            "SIVCR_MONGO_MODE debe ser VM o TUNNEL."
        )
    };
    var puertoMongoDB = modoConexion switch {
        "VM" => mongoConfiguration.GetValue<int>("PortVM"),
        "TUNNEL" => mongoConfiguration.GetValue<int>("PortTunnel"),
        _ => throw new InvalidOperationException(
            "SIVCR_MONGO_MODE debe ser VM o TUNNEL."
        )
    };

    if (string.IsNullOrWhiteSpace(mongoPassword)) {
        throw new InvalidOperationException(
            "Falta configurar SIVCR_MONGO_PASSWORD."
        );
    }

    var mongoCredentials = MongoCredential.CreateCredential(
        mongoConfiguration["AuthSource"]!,
        mongoConfiguration["Username"]!,
        mongoPassword
    );

    var mongoSettings = new MongoClientSettings {
        Server = new MongoServerAddress(
            hostMongoDB!,
            puertoMongoDB
        ),
        Credential = mongoCredentials,
        ServerSelectionTimeout = TimeSpan.FromSeconds(5)
    };

    builder.Services.AddSingleton(
        new MongoClient(mongoSettings)
    );
}

builder.Services.AddHttpClient("CargaDatosApi", client =>
{
    var direccion = builder.Configuration["CargaDatosApi:BaseUrl"]
        ?? throw new InvalidOperationException(
            "Falta configurar la dirección de la API de cargas."
        );

    client.BaseAddress = new Uri(direccion);
    client.Timeout = TimeSpan.FromMinutes(11);
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// Conserva los filtros del dashboard al cambiar de pantalla.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opciones =>
{
    opciones.IdleTimeout = TimeSpan.FromHours(2);
    opciones.Cookie.HttpOnly = true;
    opciones.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();