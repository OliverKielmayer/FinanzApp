using FinanzApp.Client;
using FinanzApp.Client.Navigation;
using FinanzApp.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Fünf Minuten statt der üblichen hundert Sekunden.
//
// Der Umsatzabruf ist der längste Weg in dieser Anwendung, und er wartet nicht auf uns: Verlangt
// die Bank eine Freigabe in ihrer App, steht der Server bis zu drei Minuten still und fragt dort
// nach — und ein weit gefasster Zeitraum bringt danach noch Dutzende camt-Dokumente über die
// Leitung. Mit der Voreinstellung bräche der Browser den Abruf mitten im Bankdialog ab: Die
// Umsätze wären geholt, der Server legte sie ordentlich in die Vorschau, und die Seite zeigte
// einen Verbindungsfehler. Schlimmer noch, wer dann erneut drückt, startet einen zweiten Dialog.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(5),
});
builder.Services.AddScoped<FinanzAppApi>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<NavigationHistory>();
builder.Services.AddScoped<DeviceProfileStore>();

// Verbindungszustand und Alter des Gezeigten. Scoped, weil das Band über allen Screens steht
// und den Zeitpunkt über einen Bereichswechsel hinweg behalten muss.
builder.Services.AddScoped<ConnectionState>();

builder.Services.AddSingleton(TimeProvider.System);

// Ein begonnener Import überdauert den Bereichswechsel: im Browser lebt Scoped so lange wie
// die Anwendung, während die Seite bei jeder Navigation neu entsteht.
builder.Services.AddScoped<ImportDraft>();

// Der Anmeldezustand kommt aus dem Cookie und wird über /api/auth/me gelesen.
builder.Services.AddScoped<FinanzAppAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<FinanzAppAuthStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

await builder.Build().RunAsync();
