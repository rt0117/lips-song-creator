using LipsSongCreator.Web.Components;
using LipsSongCreator.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Song-Service als Scoped (pro Session)
builder.Services.AddScoped<SongService>();
builder.Services.AddScoped<DlcBuildService>();
// Medien-Cache als Singleton (HTTP-Streaming fuer Audio-Vorschau)
builder.Services.AddSingleton<MediaCacheService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.UseStaticFiles();

// Medien-Streaming fuer die Editor-Vorschau (Audio/Video mit Range-Support)
app.MapGet("/media/{key}", (string key, MediaCacheService cache) =>
{
    var entry = cache.Get(key);
    if (entry == null) return Results.NotFound();
    return Results.File(entry.Data, entry.ContentType, enableRangeProcessing: true);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
