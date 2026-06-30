using PoultryFarm.Blazor.Components;
using PoultryFarm.Blazor.Services;
using MudBlazor;
using MudBlazor.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 1000;
    config.SnackbarConfiguration.HideTransitionDuration = 150;
    config.SnackbarConfiguration.ShowTransitionDuration = 150;
});
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddHttpClient("PoultryFarm.Api", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5100");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
