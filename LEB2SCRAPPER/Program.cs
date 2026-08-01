using System.Data.Common;
using LEB2SCRAPPER.Authentication;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Alerting;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.Alerting;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Infrastructure.HttpService;
using LEB2SCRAPPER.Infrastructure.Outbound;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Service.Core;
using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Service.Master;
using LEB2SCRAPPER.Repository.Core;
using LEB2SCRAPPER.Repository.Caching;
using LEB2SCRAPPER.Repository.Master;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Middleware;
using LEB2SCRAPPER.Extensions;
using LEB2SCRAPPER.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionStringName = builder.Environment.IsProduction()
    ? "Production"
    : "Supabase";
var databaseConnectionString = builder.Configuration.GetConnectionString(
    connectionStringName);
var connectionStringConfigurationKey = $"ConnectionStrings:{connectionStringName}";

if (builder.Environment.IsProduction()
    && string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException(
        $"{connectionStringConfigurationKey} is required in Production.");
}

if (!string.IsNullOrWhiteSpace(databaseConnectionString))
{
    try
    {
        _ = new DbConnectionStringBuilder
        {
            ConnectionString = databaseConnectionString
        };
    }
    catch (ArgumentException)
    {
        throw new InvalidOperationException(
            $"{connectionStringConfigurationKey} is not a valid connection string.");
    }
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(LEB2SCRAPPER.Presentation.AssemblyReference).Assembly)
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var response = new ValidationErrorResponse
            {
                Message = "Validation failed.",
                ResponseCode = ApiErrorCodes.InvalidRequest,
                TraceId = context.HttpContext.TraceIdentifier,
                ValidationErrors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value!.Errors
                            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "The supplied value is invalid."
                                : error.ErrorMessage)
                            .ToArray())
            };

            return new BadRequestObjectResult(response);
        };
    });

builder.Services.AddScoped<ICoreAdapterManager, CoreAdapterManager>();
builder.Services.AddScoped<IServiceManager, ServiceManager>();
builder.Services.AddScoped<IRepositoryManager, RepositoryManager>();
builder.Services.AddScoped<IAccessKeyRepository>(
    _ => new AccessKeyRepository(databaseConnectionString));
builder.Services.AddScoped<IAccessKeyService, AccessKeyService>();
builder.Services.AddScoped<AccessKeyRequestContext>();

var outboundRequestGateOptions = new OutboundRequestGateOptions();
builder.Configuration
    .GetSection("OutboundRequestGate")
    .Bind(outboundRequestGateOptions);

var emailFailureAlertOptions = new EmailFailureAlertOptions();
builder.Configuration
    .GetSection("FailureAlerts:Email")
    .Bind(emailFailureAlertOptions);
emailFailureAlertOptions.Validate();

var structuralScrapeCacheOptions = new StructuralScrapeCacheOptions();
builder.Configuration
    .GetSection("StructuralScrapeCache")
    .Bind(structuralScrapeCacheOptions);

var activityResultCacheOptions = new ActivityResultCacheOptions();
builder.Configuration
    .GetSection("ActivityResultCache")
    .Bind(activityResultCacheOptions);

builder.Services.AddSingleton(outboundRequestGateOptions);
builder.Services.AddSingleton(emailFailureAlertOptions);
builder.Services.AddSingleton(structuralScrapeCacheOptions);
builder.Services.AddSingleton(activityResultCacheOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IFailureAlerter, EmailFailureAlerter>();
builder.Services.AddSingleton<IClientFingerprintProvider, HmacClientFingerprintProvider>();
builder.Services.AddSingleton<IStructuralScrapeCache, StructuralScrapeCache>();
builder.Services.AddSingleton<IActivityResultCache, ActivityResultCache>();
builder.Services.AddSingleton<OutboundRequestGate>();
builder.Services.AddSingleton<IOutboundRequestGate>(
    serviceProvider => serviceProvider.GetRequiredService<OutboundRequestGate>());
builder.Services.AddSingleton<IOutboundRequestStatusReader>(
    serviceProvider => serviceProvider.GetRequiredService<OutboundRequestGate>());
builder.Services
    .AddHttpClient<IHttpService, HttpService>()
    .ConfigurePrimaryHttpMessageHandler(Leb2HttpClientHandlerFactory.Create);

builder.Services.AddScoped<Leb2SessionCredential>();
builder.Services.AddScoped<ILeb2SessionCredential>(
    serviceProvider => serviceProvider.GetRequiredService<Leb2SessionCredential>());
builder.Services.AddScoped<ILeb2SessionCredentialStore>(
    serviceProvider => serviceProvider.GetRequiredService<Leb2SessionCredential>());

builder.Services
    .AddAuthentication(Leb2BearerDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, Leb2BearerAuthenticationHandler>(
        Leb2BearerDefaults.AuthenticationScheme,
        _ => { });
builder.Services.AddAuthorization();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        Leb2BearerDefaults.AuthenticationScheme,
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Opaque LEB2 session cookie",
            Description = "Use Authorization: Bearer <session-cookie-value>. "
                + "Legacy raw Authorization values remain accepted during migration."
        });
    options.AddSecurityDefinition(
        "AccessKey",
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = AccessKeyAuthorizationFilter.HeaderName,
            In = ParameterLocation.Header,
            Description = "Use access-key: <provisioned UUID>."
        });
    options.OperationFilter<AuthorizeOperationFilter>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseGlobalExceptionMiddleware();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

public partial class Program
{
}
