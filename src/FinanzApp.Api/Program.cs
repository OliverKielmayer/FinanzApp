using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Endpoints;
using FinanzApp.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
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
    // Vollständig qualifiziert: Microsoft.AspNetCore.Authentication bringt einen gleichnamigen Typ mit.
    builder.Services.AddSingleton<IClock, FinanzApp.Api.Infrastructure.SystemClock>();
}

// ── Anmeldung ──────────────────────────────────────────────────────────────────────────────
//
// Cookie statt Token: Client und API teilen sich einen Ursprung, damit ist das Cookie die
// einfachere und sicherere Wahl — es liegt httpOnly im Browser und ist für Skripte unerreichbar.
builder.Services.AddHttpContextAccessor();

// Echte Uhr für alles, was Gültigkeit bestimmt — der Demo-Stichtag gilt nur fachlich.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "finanzapp.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;

        // Diese Anwendung ist eine API: ein nicht angemeldeter Aufruf bekommt 401 und nicht
        // eine Weiterleitung auf eine Anmeldeseite, die es serverseitig gar nicht gibt.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // Das Cookie allein reicht nicht: bei jeder Anfrage wird geprüft, ob die Sitzung noch
        // gilt. Nur so lässt sich „Angemeldet bleiben“ überhaupt widerrufen.
        options.Events.OnValidatePrincipal = async context =>
        {
            // Die Sitzungs-Id kommt aus dem gerade entschlüsselten Cookie. HttpContext.User ist
            // an dieser Stelle noch nicht gesetzt — CurrentUser läse hier ins Leere.
            var claim = context.Principal?.FindFirst(AppClaims.SessionId)?.Value;
            var auth = context.HttpContext.RequestServices.GetRequiredService<AuthService>();

            if (!Guid.TryParse(claim, out var sessionId) || await auth.TouchSessionAsync(sessionId) is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAppAuthorization();

// Bremst das Durchprobieren von Zugangsdaten, bevor die Kontosperre überhaupt greift.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
        }));
});

// ── Mailversand ────────────────────────────────────────────────────────────────────────────
var mailOptions = builder.Configuration.GetSection(MailOptions.SectionName).Get<MailOptions>() ?? new MailOptions();
builder.Services.AddSingleton(mailOptions);
if (mailOptions.IsConfigured)
{
    builder.Services.AddSingleton<IMailSender, SmtpMailSender>();
}
else
{
    // Ohne diese Zeile ist „es kommt keine Mail an“ beim Start nicht zu sehen, sondern erst,
    // wenn jemand vergeblich auf eine wartet.
    Console.WriteLine(string.IsNullOrWhiteSpace(mailOptions.Host)
        ? "Mail: kein Postausgang (Mail:Host leer). Reset-Links stehen im Protokoll."
        : $"Mail: {mailOptions.Host} vorbereitet, aber Mail:Password fehlt. "
          + "Reset-Links stehen im Protokoll.");

    builder.Services.AddSingleton<IMailSender, LoggingMailSender>();
}

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<OverviewService>();
builder.Services.AddScoped<DashboardService>();

// ── Erweiterung: Dokumente, Vorgänge, Gesundheit, Versicherungen, Wohnen, Liquidität ───────
var documentOptions = builder.Configuration.GetSection(DocumentStorageOptions.SectionName)
                          .Get<DocumentStorageOptions>() ?? new DocumentStorageOptions();
builder.Services.AddSingleton(documentOptions);
builder.Services.AddSingleton<DocumentPathService>();

// Ohne angebundene Texterkennung bleibt die Erfassungsmaske leer — der Flow läuft trotzdem.
builder.Services.AddSingleton<IBillTextExtractor, NoBillTextExtractor>();

// Dasselbe Prinzip für Policen: eine Schnittstelle, austauschbar, und ohne sie läuft der
// Anlege-Flow trotzdem — nur mit leerer Maske.
builder.Services.AddSingleton<IPolicyDocumentAnalyzer, NoPolicyDocumentAnalyzer>();

builder.Services.AddScoped<ObjectLabelService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<MedicalBillService>();
builder.Services.AddScoped<PolicyService>();
builder.Services.AddScoped<VehicleService>();
builder.Services.AddScoped<ScanInboxService>();
builder.Services.AddScoped<CreateFormService>();
builder.Services.AddScoped<PropertyService>();
builder.Services.AddScoped<LiquidityService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinanzAppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Schema");

    // Migrationen statt EnsureCreated: das Schema wächst mit den Erweiterungen, und ein
    // bestehender Datenbestand überlebt eine neue Fassung.
    await SchemaStartup.MigrateAsync(db, logger);
    await SeedData.EnsureSeededAsync(
        db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        scope.ServiceProvider.GetRequiredService<DocumentPathService>());
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();

// Der Haushalt der Anfrage kommt aus dem Anmelde-Cookie und nirgendwo sonst. Ohne Anmeldung
// bleibt er 0 — dann findet der Abfragefilter im DbContext nichts.
app.Use(async (context, next) =>
{
    var current = context.RequestServices.GetRequiredService<CurrentUser>();
    if (current.HouseholdId is { } householdId)
    {
        context.RequestServices.GetRequiredService<FinanzAppDbContext>().CurrentHouseholdId = householdId;
    }

    await next();
});

app.UseAuthorization();

app.MapAuth();
app.MapApi();
app.MapExtensions();
app.MapFallbackToFile("index.html");

app.Run();
