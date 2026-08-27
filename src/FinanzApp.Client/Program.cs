using FinanzApp.Client;
using FinanzApp.Client.Navigation;
using FinanzApp.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<FinanzAppApi>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<NavigationHistory>();
builder.Services.AddScoped<DeviceProfileStore>();

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
