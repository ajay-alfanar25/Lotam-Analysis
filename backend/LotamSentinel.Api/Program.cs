using System.Text;
using LotamSentinel.Application.Auth;
using LotamSentinel.Application.Classification;
using LotamSentinel.Application.Comparison;
using LotamSentinel.Application.Email;
using LotamSentinel.Application.Ingestion;
using LotamSentinel.Application.Normalization;
using LotamSentinel.Application.Notifications;
using LotamSentinel.Application.PressReleases;
using LotamSentinel.Application.Reporting;
using LotamSentinel.Application.SecMonitoring;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default in appsettings.json.");

builder.Services.AddDbContext<LotamSentinelDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<PdfLotamParser>();
builder.Services.AddScoped<SupplierNormalizationService>();
builder.Services.AddScoped<LotamIngestionService>();
builder.Services.AddScoped<StatusClassifier>();
builder.Services.AddScoped<TechnologyClassifier>();
builder.Services.AddScoped<ComparisonEngine>();
builder.Services.AddScoped<SeverityCalculator>();
builder.Services.AddScoped<ComparisonService>();

// Reporting: IReportRenderer is the extension point for export formats -
// both Pdf and Html are registered here as the same interface, and
// IReportRendererFactory (also registered below) is what picks the right
// one for a given request's ?format= parameter. The two builders below
// (which gather the actual data) don't need to know or care which formats
// exist.
builder.Services.AddScoped<IReportRenderer, PdfReportRenderer>();
builder.Services.AddScoped<IReportRenderer, HtmlReportRenderer>();
builder.Services.AddScoped<IReportRendererFactory, ReportRendererFactory>();
builder.Services.AddScoped<ChangeReportBuilder>();
builder.Services.AddScoped<MaterialReportBuilder>();

// ---- Auth / Profile / Email ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection("Brevo"));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// Email:Provider picks which IEmailSender implementation is active -
// "Smtp" (default, MailKit talking raw SMTP to Host:Port) or "Brevo" (the
// Brevo HTTP API over plain HTTPS). Both are registered so the choice is a
// config change, not a redeploy-with-different-code change.
//
// Why this switch exists: on a network that blocks or throttles outbound
// SMTP (very common for a corporate firewall - see BrevoEmailSender's doc
// comment), a raw TCP test to the SMTP host can succeed while the app's
// own SMTP connection still times out. Switching Email:Provider to "Brevo"
// sends over HTTPS instead, which corporate networks essentially never
// block (every browser depends on it), so it works from a network where
// SMTP does not - no code change needed, just Email__Provider=Brevo plus
// the Brevo:* settings (see DEPLOYMENT.md "Email delivery").
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddHttpClient<BrevoEmailSender>();
var emailProvider = builder.Configuration["Email:Provider"] ?? "Smtp";
builder.Services.AddScoped<IEmailSender>(sp => string.Equals(emailProvider, "Brevo", StringComparison.OrdinalIgnoreCase)
    ? sp.GetRequiredService<BrevoEmailSender>()
    : sp.GetRequiredService<SmtpEmailSender>());

// Subscription digest ("Send digest now" on the Notification Settings tab) -
// singleton queue + a long-running hosted worker that drains it one job at
// a time. See SubscriptionDigestQueue's doc comment for why this is
// sequential rather than fire-and-forget per subscription.
builder.Services.AddSingleton<SubscriptionDigestQueue>();
builder.Services.AddHostedService<SubscriptionDigestWorker>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Tolerant of a missing/short secret at startup (so the app still
        // boots before Jwt:Secret is filled in) - JwtTokenService.GenerateToken
        // is what actually throws a clear error the first time someone tries
        // to log in with it unset.
        var keyBytes = Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(jwtOptions.Secret) ? new string('0', 32) : jwtOptions.Secret);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddMemoryCache();
builder.Services.Configure<PressReleaseSearchOptions>(builder.Configuration.GetSection("PressReleaseSearch"));
builder.Services.AddHttpClient<IPressReleaseSearchService, GooglePressReleaseSearchService>();

builder.Services.Configure<SecMonitorOptions>(builder.Configuration.GetSection("SecMonitor"));
builder.Services.AddHttpClient<ISecMonitorService, SecMonitorService>();
builder.Services.AddHostedService<SecMonitorBackgroundService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets Swagger UI's "Authorize" button attach a bearer token, so
    // [Authorize] endpoints (Profile, change-password, etc.) can still be
    // exercised from /swagger without a separate REST client.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by POST /api/auth/login (no \"Bearer \" prefix needed here).",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// One-time bootstrap: if there are no users yet at all, and an admin
// email/password are configured, create the very first login. There is no
// public sign-up endpoint by design, so without this there would be no way
// to log in the first time. Safe to leave the config keys filled in
// afterwards - this only ever runs when app_user is completely empty.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LotamSentinelDbContext>();
    var bootstrapLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var adminEmail = app.Configuration["Bootstrap:AdminEmail"];
    var adminPassword = app.Configuration["Bootstrap:AdminPassword"];
    var adminName = app.Configuration["Bootstrap:AdminFullName"] ?? "Administrator";

    if (!await db.AppUsers.AnyAsync())
    {
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var hasher = app.Services.GetRequiredService<IPasswordHasher<AppUser>>();
            var admin = new AppUser
            {
                Email = adminEmail.Trim(),
                FullName = adminName,
                IsActive = true,
                IsAdmin = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.AppUsers.Add(admin);
            await db.SaveChangesAsync();
            bootstrapLogger.LogInformation("Bootstrapped the first admin login for {Email}.", admin.Email);
        }
        else
        {
            bootstrapLogger.LogWarning(
                "No users exist yet and Bootstrap:AdminEmail/AdminPassword are not set in appsettings.json - " +
                "nobody will be able to log in until one of the two is done.");
        }
    }
}

// Diagnostic startup log - confirms which environment is actually active
// and whether SecMonitor/PressReleaseSearch config resolved as expected,
// without logging the actual secret values. Safe to remove once SEC
// monitoring is confirmed working - kept temporarily to debug a real case
// where appsettings.Development.json wasn't being loaded at all.
{
    using var scope = app.Services.CreateScope();
    var secOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<SecMonitorOptions>>().CurrentValue;
    var pressOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PressReleaseSearchOptions>>().CurrentValue;
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation(
        "Startup config check - Environment: {Env} | SecMonitor.Enabled: {SecEnabled}, ListingPageUrl set: {SecListingSet} (on hold - LOTAM is portal-gated, see SecMonitorService.cs) | PressReleaseSearch.Enabled: {PressEnabled}, GoogleApiKey set: {PressKeySet}, GoogleSearchEngineId set: {PressCxSet}",
        app.Environment.EnvironmentName,
        secOptions.Enabled, !string.IsNullOrWhiteSpace(secOptions.ListingPageUrl),
        pressOptions.Enabled, !string.IsNullOrWhiteSpace(pressOptions.GoogleApiKey), !string.IsNullOrWhiteSpace(pressOptions.GoogleSearchEngineId));
}

// Swagger enabled unconditionally (not just in Development) - this is an
// internal V1 tool with no production deployment in scope, so gating it
// behind ASPNETCORE_ENVIRONMENT is just friction (it silently defaults to
// Production, and hence no Swagger, whenever the DLL is run directly instead
// of via `dotnet run`/an IDE, since only those two read launchSettings.json).
app.UseSwagger();
app.UseSwaggerUI();

// Without this, an unhandled exception anywhere below just returns a bare
// 500 with an empty body - which is exactly what showed up as "500
// (Internal Server Error)" in the browser console with no way to tell what
// actually broke. UseDeveloperExceptionPage() puts the real exception type,
// message, and stack trace in the HTTP response itself (Development only -
// same reasoning as the unconditional Swagger above, this is an internal V1
// tool, not something exposing a public Production deployment).
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseCors("FrontendDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
