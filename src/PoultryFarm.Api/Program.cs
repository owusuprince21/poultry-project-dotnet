using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PoultryFarm.Api.Hubs;
using PoultryFarm.Api.Services;
using PoultryFarm.Application;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Infrastructure;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using PoultryFarm.Api.Authorization;
using Microsoft.AspNetCore.Http.Connections;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IActivityNotifier, SignalRActivityNotifier>();
builder.Services.AddHostedService<MedicationReminderService>();
builder.Services.AddHostedService<ChatMessageEncryptionBackfillService>();
builder.Services.AddHttpClient<AiProviderClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("AI:TimeoutSeconds", 18));
});
builder.Services.AddScoped<IFarmAssistantAgent, OpenAiFarmAssistantAgent>();
builder.Services.AddSingleton<IChatMessageProtector, ChatMessageProtector>();
builder.Services.AddHttpClient<OpenAiService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, UserIdProvider>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.SystemAdminOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            PermissionHelpers.IsSystemAdmin(context.User));
    });

    options.AddPolicy(AppPolicies.CompanyAdminOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            PermissionHelpers.IsCompanyAdmin(context.User));
    });

    options.AddPolicy(AppPolicies.AdminOrWorkerLimited, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new WorkerWriteRequirement());
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, WorkerWriteRequirementHandler>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:5002", "http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var jwtKey = builder.Configuration["Jwt:SigningKey"] ?? "development-signing-key-change-before-production-12345";
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PoultryFarm.Api",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PoultryFarm.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/activity"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddSqlServer(connectionString, name: "sql-server");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedDevelopmentDataAsync();
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options =>
    {
        options.Title = "Poultry Farm API";
        options.Theme = ScalarTheme.Kepler;
    });
}

app.UseCors("BlazorClient");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<ActivityHub>("/hubs/activity");

app.Run();

public partial class Program;
