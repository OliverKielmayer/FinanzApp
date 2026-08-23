using System.Text.Json.Serialization;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Endpoints;
using FinanzApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FinanzAppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FinanzApp")
                      ?? "Data Source=finanzapp.db"));

// Enums gehen als Name über die Leitung. Das hält die JSON-Antworten lesbar und entkoppelt
// den Client von der numerischen Reihenfolge.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Die Beispieldaten hängen an einem festen Stichtag, damit Monatssummen und Budgets stimmig
// bleiben. Ohne "Demo:Today" läuft die Anwendung auf der echten Uhr.
var demoToday = builder.Configuration["Demo:Today"];
if (DateTime.TryParse(demoToday, out var fixedToday))
{
    builder.Services.AddSingleton<IClock>(new FixedClock(fixedToday));
}
else
{
    builder.Services.AddSingleton<IClock, SystemClock>();
}

builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<OverviewService>();
builder.Services.AddScoped<DashboardService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinanzAppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.EnsureSeededAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapApi();
app.MapFallbackToFile("index.html");

app.Run();
