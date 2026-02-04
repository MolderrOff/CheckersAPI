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
builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true; 
});


builder.Services.AddScoped<LogActionFilter>();

builder.WebHost.UseUrls("http://0.0.0.0:5119", "https://0.0.0.0:7224");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStopping.Register(() =>
{
    var pool = app.Services.GetRequiredService<EnginePoolService>();
    pool.Dispose();
});

app.Run();
