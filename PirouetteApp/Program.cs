using Microsoft.EntityFrameworkCore;
using Pirouette.Infrastructure;
using Pirouette.Infrastructure.Configurations;
using PirouetteApp.Components;
using PirouetteApp.Services;

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

// Scoped, though they hold nothing per-request: they depend only on the context factory, and
// each method opens and disposes its own short-lived context. Scoped rather than singleton so
// that adding a per-user dependency later — the claims-based tenant provider in M7 — does not
// turn into a captive-dependency bug discovered at runtime.
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<DashboardService>();

var app = builder.Build();

// Migrate and seed on startup, in development only.
//
// Calling Migrate() at startup is a habit worth being wary of — against a shared database it
// races between instances, and it grants the application's own connection permission to alter
// the schema. Here it is guarded by the environment check and exists so a fresh clone can go
// from `docker compose up` to a populated app without a separate `dotnet ef database update`.
// The deployment step in M8 will apply migrations from CI instead.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();

    var contextFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<PirouetteDbContext>>();

    await using var db = await contextFactory.CreateDbContextAsync();

    await db.Database.MigrateAsync();
    await DevelopmentSeeder.SeedAsync(db);
}

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
