using SpotifyAPI.Web;
using System.Text;
using BeatBallot.Data;
using BeatBallot.Services;
using BeatBallot.Web.Middleware;
using BeatBallot.Web.Services;
using Blazored.LocalStorage;
using ISpotifyAuthService = BeatBallot.Web.Services.ISpotifyAuthService;
using SpotifyAuthService = BeatBallot.Web.Services.SpotifyAuthService;


var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddAzureWebAppDiagnostics();

// Add services to the container.
builder.Services.AddSingleton<DapperContext>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.Configuration["BaseUrl"]) });


builder.Services.AddScoped<ITokenProvider, TokenProvider>();
builder.Services.AddScoped<SpotifyService>();
builder.Services.AddSingleton<StringCompressorService>();
builder.Services.AddSingleton<SessionCacheService>();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<ISpotifyAuthService, SpotifyAuthService>();

var app = builder.Build();

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



app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseEndpoints(endpoints =>
{
    endpoints.MapBlazorHub();
    endpoints.MapHub<NotificationHub>("/notificationHub");
    endpoints.MapFallbackToPage("/_Host");
});




app.Run();
