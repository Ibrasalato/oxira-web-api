using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Oxira.Web.Api.Infrastructure;
using Oxira.Web.Application;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure;
using Oxira.Web.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddApplication(cfg);
builder.Services.AddInfrastructure(cfg);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddSingleton<IRealtime, SignalRRealtime>();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddApiHosting(cfg);
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

builder.Services.AddControllers(o =>
    {
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        o.AllowEmptyInputInBodyModelBinding = true;
    })
    .AddJsonOptions(o => Json.Configure(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
        new BadRequestObjectResult(ErrorBody.Of("bad_request", "طلب غير صالح", null,
            ctx.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? string.Join("; ", ctx.ModelState.Where(m => m.Value?.Errors.Count > 0).Select(m => $"{m.Key}: {m.Value!.Errors[0].ErrorMessage}"))
                : null)));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<OpenApiInfoTransformer>());
builder.Services.AddSignalR(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment()).AddJsonProtocol(o => Json.Configure(o.PayloadSerializerOptions));

// ───── JWT: this API issues the Oxira account tokens and validates them like every other Oxira API ─────
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = jwt.SigningKey(),
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = OxiraClaims.Role, NameClaimType = OxiraClaims.Email,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], RequireExpirationTime = true, RequireSignedTokens = true,
    };
    o.Events = new JwtBearerEvents
    {
        // SignalR sends the token as ?access_token= (browsers cannot set headers on WebSockets)
        OnMessageReceived = ctx =>
        {
            var t = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(t) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = t;
            return Task.CompletedTask;
        },
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            await ctx.Response.WriteAsJsonAsync(ErrorBody.Of("session", "انتهت الجلسة، سجّل الدخول من جديد"));
        },
        OnForbidden = async ctx =>
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(ErrorBody.Of("forbidden", "هذه الصفحة لفريق أوكسيرا فقط"));
        },
    };
});
builder.Services.AddAuthorizationBuilder().AddPolicy("staff", p => p.RequireRole(Oxira.Web.Domain.Roles.Team));

// ───── CORS: every Oxira site and local development ─────
var origins = cfg.GetSection("Cors:Origins").Get<string[]>() is { Length: > 0 } list ? list :
    ["https://oxira.sa", "https://www.oxira.sa", "https://design.oxira.sa", "https://events.oxira.sa", "https://ibrasalato.github.io", "http://localhost:4321"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ───── rate limits per IP ─────
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(ErrorBody.Of("limit", "محاولات كثيرة، انتظر قليلًا ثم حاول", new { seconds = 60 }), ct);
    string Ip(HttpContext h) => h.RequestServices.GetRequiredService<ICurrentUser>().Ip;
    RateLimitPartition<string> Window(HttpContext h, string name, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue($"RateLimit:{name}", perMinute), Window = TimeSpan.FromMinutes(1) });
    o.AddPolicy("auth", h => Window(h, "AuthPerMinute", 20));
    o.AddPolicy("refresh", h => Window(h, "RefreshPerMinute", 60));
    o.AddPolicy("forms", h => Window(h, "FormsPerMinute", 10));
    o.AddPolicy("chat", h => Window(h, "ChatPerMinute", 20));
    o.AddPolicy("ai", h => Window(h, "AiPerMinute", 8));
    o.AddPolicy("upload", h => Window(h, "UploadPerMinute", 30));
});

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<BodyNormalizerMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<AccountHub>("/hubs/account");
app.MapHub<OrdersHub>("/hubs/orders");
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow })).ExcludeFromDescription();

// Swagger: /openapi/v1.json and the UI at /swagger (Swagger__Enabled=false turns both off)
if (cfg.GetValue("Swagger:Enabled", true))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Oxira API v1");
        o.DocumentTitle = "Oxira API";
        o.EnablePersistAuthorization();
        o.DisplayRequestDuration();
    });
}

if (cfg.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DbInitializer>().RunAsync();
}

app.Run();

public partial class Program;
