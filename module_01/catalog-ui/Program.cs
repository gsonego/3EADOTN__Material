using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Auto-instruments ASP.NET Core (incoming requests) and HttpClient (the
// outbound calls CatalogProxyController makes to catalog-api) -- reads
// APPLICATIONINSIGHTS_CONNECTION_STRING automatically, no code-level secret.
// Because both apps use this same package, a single browser action shows up
// as one correlated Operation Id spanning catalog-ui's Request AND its
// HttpClient call to catalog-api as a Dependency (Module 4, Topic 2).
builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient("catalog-api", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
