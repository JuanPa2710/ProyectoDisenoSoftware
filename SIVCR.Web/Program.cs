var builder = WebApplication.CreateBuilder(args);

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
