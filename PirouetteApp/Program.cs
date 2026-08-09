using Microsoft.EntityFrameworkCore;
using Pirouette.Infrastructure;
using Pirouette.Infrastructure.Configurations;
using PirouetteApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// There is no signed-in user to derive a tenant from until authentication lands in M7, so the
// current studio comes from configuration. Fail fast if it is missing: an unset value would be
// Guid.Empty, and because every query is filtered by it, the app would start cleanly and then
// return no rows at all — a much harder problem to diagnose than a startup error.
var studioId = builder.Configuration.GetValue<Guid>("Pirouette:DevStudioId");
if (studioId == Guid.Empty)
{
    throw new InvalidOperationException(
        "Pirouette:DevStudioId is not configured. Every query is scoped by it, so an unset " +
        $"value would silently return nothing. Expected {StudioConfiguration.SeedStudioId}.");
}

builder.Services.AddSingleton<ITenantProvider>(new FixedTenantProvider(studioId));

// Scoped rather than singleton so that M7 can swap in a claims-based ITenantProvider without
// restructuring: a scoped provider resolved by a singleton would be a captive dependency.
builder.Services.AddScoped<TenantStampingInterceptor>();

builder.Services.AddDbContextFactory<PirouetteDbContext>(
    (sp, options) => options
        .UseNpgsql(builder.Configuration.GetConnectionString("Pirouette"))
        .AddInterceptors(sp.GetRequiredService<TenantStampingInterceptor>()),
    lifetime: ServiceLifetime.Scoped);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
