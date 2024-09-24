using Entertainer.Services;
using SpotifyAPI.Web;
using System.Text;
using Entertainer.Data;
using TheEntertainer.Services;
using TheEntertainer.Middleware;
using Blazored.LocalStorage;

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



builder.Services.AddScoped<SpotifyService>();
builder.Services.AddSingleton<NotificationService>(); // Register NotificationService
builder.Services.AddSingleton<StringCompressorService>();
builder.Services.AddSingleton<JamCacheService>();
builder.Services.AddBlazoredLocalStorage();






var app = builder.Build();
app.MapGet("/login", () =>
{
    string clientId = builder.Configuration["SpotifyClientId"];
    string redirectUri = builder.Configuration["BaseUrl"] + "/jam";
    Random random = new Random();
    string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    char[] stringChars = new char[16];

    for (int i = 0; i < stringChars.Length; i++)
    {
        stringChars[i] = chars[random.Next(chars.Length)];
    }

    string state = new string(stringChars);
    string scope = "user-read-private user-read-email user-modify-playback-state user-read-playback-state user-read-currently-playing";
    StringBuilder query = new StringBuilder();
    query.Append("https://accounts.spotify.com/authorize?");
    query.Append($"response_type=code&client_id={clientId}&scope={scope}&redirect_uri={redirectUri}&state={state}");
    dynamic data = new { Query = query.ToString() };
    return Results.Json(data);
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



app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseEndpoints(endpoints =>
{
    endpoints.MapBlazorHub();
    endpoints.MapHub<NotificationHub>("/notificationHub");
    endpoints.MapFallbackToPage("/_Host");
});




app.Run();
