using BeatBallot.Data;
using BeatBallot.Web.Admin;
using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.LiveSession.TakeRequests.Shared;
using BeatBallot.Web.LiveSession.VotingExperience;
using BeatBallot.Web.LiveSession.VotingExperience.Shared;
using BeatBallot.Web.Services;
using BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Middleware;
using BeatBallot.Web.Services.Spotify;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.HttpOverrides;


var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddAzureWebAppDiagnostics();

// Add services to the container.
builder.Services.AddSingleton<DapperContext>();
builder.Services.AddHttpContextAccessor();

//builder.Services.AddDistributedMemoryCache();


builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.Configuration["BaseUrl"]) });
builder.Services.AddHttpClient();

builder.Services.AddScoped<ITokenProvider, TokenProvider>();
builder.Services.AddScoped<SpotifyService>();
builder.Services.AddSingleton<StringCompressorService>();


builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPlaylistVoteCacheService, PlaylistVoteCacheService>();
builder.Services.AddScoped<ISongRequestCacheService, SongRequestCacheService>();
builder.Services.AddScoped<ISpotifyTokenService, SpotifyTokenService>();
builder.Services.AddScoped<SpotifySearchService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddBlazoredLocalStorage();


// Configure SignalR for better stability
builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.EnableDetailedErrors = true; // Only in development
});

// Configure Blazor Server options
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});
builder.Services.AddScoped<IConnectionMonitorService, ConnectionMonitorService>();



// Register admin services
builder.Services.AddSingleton<ISessionActivityLogger, SessionActivityLogger>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<SongRequestHub>();

builder.Services.AddControllers();

builder.Services.AddScoped<ITipService, TipService>();


var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapGet("/request/{sessionId}", async (string sessionId, HttpContext context) =>
{
    var htmlPath = Path.Combine(app.Environment.WebRootPath, "request.html");

    if (!File.Exists(htmlPath))
    {
        return Results.NotFound("Request page not found");
    }

    var htmlContent = await File.ReadAllTextAsync(htmlPath);
    return Results.Content(htmlContent, "text/html");
});

// Serve landing page at root URL
app.MapGet("/", async (HttpContext context) =>
{
    var htmlPath = Path.Combine(app.Environment.WebRootPath, "landing.html");
    if (!File.Exists(htmlPath))
    {
        return Results.NotFound("Landing page not found");
    }
    var htmlContent = await File.ReadAllTextAsync(htmlPath);
    return Results.Content(htmlContent, "text/html");
});

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseEndpoints(endpoints =>
{
    endpoints.MapBlazorHub();
    endpoints.MapHub<VotingHub>("/votingHub");
    

    endpoints.MapHub<SongRequestHub>("/songRequestHub");
    endpoints.MapFallbackToPage("/_Host");
});

app.MapControllers();


app.Run();
