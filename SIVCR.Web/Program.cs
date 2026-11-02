using MongoDB.Driver;


var builder = WebApplication.CreateBuilder(args);

var cargarDatos = builder.Configuration.GetValue<bool>(
    "Explorador:CargarDatos"
);

// coneccion a Mongo
// crea el cliente solo cuando la carga real está activa
if (cargarDatos) {
    var mongoConfiguration = builder.Configuration.GetSection("MongoDB");
    var mongoPassword = Environment.GetEnvironmentVariable(
        "SIVCR_MONGO_PASSWORD"
    );

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
            mongoConfiguration["Host"]!,
            int.Parse(mongoConfiguration["Port"]!)
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
