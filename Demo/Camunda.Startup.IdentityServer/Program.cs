using Camunda.Startup.IdentityServer;
using Camunda.Startup.IdentityServer.Components;
using Duende.IdentityServer;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents();
builder.Services.AddAuthorization();

builder.Services.AddIdentityServer(options =>
    {
        // Camunda runs in a container and reaches this host via host.containers.internal.
        // Pin IssuerUri so tokens minted via the browser-facing localhost URL still
        // pass Camunda's issuer validation.
        options.IssuerUri = "http://host.containers.internal:8080";
        options.EmitStaticAudienceClaim = false;
        options.Authentication.CheckSessionCookieSameSiteMode = SameSiteMode.Lax;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryApiResources(Config.ApiResources)
    .AddInMemoryClients(Config.Clients)
    .AddTestUsers(Config.Users)
    .AddDeveloperSigningCredential();

// Dev runs on HTTP. ASP.NET Core silently drops Set-Cookie when SameSite=None
// is set without Secure, which kills the OIDC login because the auth cookie
// never reaches the browser. Pin the IS auth cookie to Lax so it survives HTTP.
builder.Services.PostConfigure<CookieAuthenticationOptions>(
    IdentityServerConstants.DefaultCookieAuthenticationScheme,
    o => o.Cookie.SameSite = SameSiteMode.Lax);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();
app.UseAntiforgery();
app.MapRazorComponents<App>();

app.Run();
