using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "ProductionLinePlannerCors",
        policy =>
        {
            policy
                .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

// Authentication placeholder: implementation will be added in a dedicated auth slice later.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// SignalR placeholder registration, no hub implementation details yet.
builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("ProductionLinePlannerCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow
}))
    .WithTags("System")
    .WithName("Health");

app.MapGet("/", () => Results.Ok("ProductionLinePlanner API is running."));

app.MapGet("/api/identity/placeholder", () => Results.Ok(new
{
    message = "Authentication is currently a placeholder.",
    note = "JWT authentication handlers will be implemented in a future sprint."
}));

app.MapHub<ProductionHub>("/hubs/production");

app.Run();

public sealed class ProductionHub : Hub
{
}
