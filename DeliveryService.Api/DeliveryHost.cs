using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using DeliveryService.Core;
using DeliveryService.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

namespace DeliveryService.Api;

public static class DeliveryHost
{
    public static WebApplication Build(string[] args, X509Certificate2? desktopCertificate = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(DeliveryHost).Assembly.GetName().Name
        });

        if (desktopCertificate is not null)
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(desktopCertificate)));
        else
        {
            string urls = builder.Configuration["urls"] ?? "https://localhost:7135";
            if (urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(url => !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Для API указывайте только HTTPS-адреса.");
            builder.WebHost.UseUrls(urls);
        }

        int loginLimit = builder.Configuration.GetValue("Security:LoginAttemptsPerMinute", 5);
        if (loginLimit is < 1 or > 1000)
            throw new InvalidOperationException("Security:LoginAttemptsPerMinute должен быть от 1 до 1000.");
        string? configuredKey = builder.Configuration["Jwt:Key"];
        if (configuredKey is not null && Encoding.UTF8.GetByteCount(configuredKey) < 32)
            throw new InvalidOperationException("Jwt:Key должен содержать не менее 32 байтов.");

        string path = Path.GetFullPath(builder.Configuration["Delivery:Database"] ?? DeliveryDatabase.DefaultPath);
        bool seedDemo = builder.Configuration.GetValue<bool?>("Delivery:SeedDemo") ??
            (desktopCertificate is not null && builder.Configuration["Delivery:Database"] is null);
        builder.Services.AddDbContext<DeliveryDb>(options => DeliveryDatabase.Configure(options, path));
        builder.Services.AddScoped<DeliveryStore>();
        builder.Services.AddScoped<IShipmentRepository>(services => services.GetRequiredService<DeliveryStore>());
        builder.Services.AddScoped<IRecipientRepository>(services => services.GetRequiredService<DeliveryStore>());
        builder.Services.AddScoped<IUserRepository>(services => services.GetRequiredService<DeliveryStore>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        builder.Services.AddMemoryCache();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            const string bearer = "Bearer";
            options.AddSecurityDefinition(bearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(bearer, document)] = []
            });
        });
        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            foreach (var converter in Exchange.JsonOptions.Converters)
                options.JsonSerializerOptions.Converters.Add(converter);
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            foreach (var converter in Exchange.JsonOptions.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });

        var key = new SymmetricSecurityKey(configuredKey is null
            ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(configuredKey));
        builder.Services.AddSingleton(new SessionTokens(key));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = SessionTokens.Issuer,
                ValidAudience = SessionTokens.Audience,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "login",
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    string? id = context.Principal?.FindFirst("jti")?.Value;
                    var revoked = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    if (id is null || revoked.TryGetValue(id, out _))
                        context.Fail("Сеанс завершён.");
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Staff", policy =>
            policy.RequireRole(Roles.Admin, Roles.Operator));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginLimit,
                    Window = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true
                }));
        });

        builder.Services.AddSerilog((_, logger) => logger.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(Path.Combine(Path.GetDirectoryName(path)!, "logs", "api-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        var app = builder.Build();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(handler => handler.Run(HandleError));
        app.UseStatusCodePages();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", async (DeliveryDb db, CancellationToken ct) =>
            Results.Ok(new HealthInfo("ok", await db.Shipments.CountAsync(ct))));
        return app;
    }

    public static async Task Initialize(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        bool seed = app.Configuration.GetValue<bool?>("Delivery:SeedDemo") ?? false;
        await DeliveryDatabase.Initialize(scope.ServiceProvider.GetRequiredService<DeliveryDb>(),
            Path.GetFullPath(app.Configuration["Delivery:Database"] ?? DeliveryDatabase.DefaultPath), seed);
    }

    private static Task HandleError(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        return (error switch
        {
            CsvImportException csv => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Ошибка импорта CSV", detail: csv.Message,
                extensions: new Dictionary<string, object?> { ["errors"] = csv.Errors }),
            RuleException => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Проверьте данные", detail: error.Message),
            ShipmentConflictException or DbUpdateConcurrencyException =>
                Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Отправление изменилось",
                    detail: "Обновите список, откройте отправление и повторите действие."),
            BadHttpRequestException bad => Results.Problem(statusCode: bad.StatusCode, title: "Некорректный запрос"),
            DbUpdateException => Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Ошибка сохранения данных"),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Внутренняя ошибка службы доставки")
        }).ExecuteAsync(context);
    }
}
