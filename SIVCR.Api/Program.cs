using SIVCR.Api.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<CargaDatosOptions>(
    builder.Configuration.GetSection("CargaDatos")
);
// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<CargaDatosService>();

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
