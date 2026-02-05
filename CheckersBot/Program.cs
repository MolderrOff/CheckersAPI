using CheckersBot;
using Microsoft.AspNetCore.Mvc;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<EnginePoolService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EnginePoolService>());
builder.Services.AddMemoryCache(options => {
    options.SizeLimit = 10000;
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.AddServer(new Microsoft.OpenApi.Models.OpenApiServer
    {
        Url = "/" 
    });
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true; 
});


builder.Services.AddScoped<LogActionFilter>();

builder.WebHost.UseUrls("http://0.0.0.0:5119", "https://0.0.0.0:7224");

var app = builder.Build();

app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "V1");
       
    });
}

app.UseAuthorization();

app.UseDefaultFiles(); 
app.UseStaticFiles();

app.MapControllers();

app.Lifetime.ApplicationStopping.Register(() =>
{
    var pool = app.Services.GetRequiredService<EnginePoolService>();
    pool.Dispose();
});

app.Run();
