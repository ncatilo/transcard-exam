using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TransCard.Configuration;
using TransCard.Handlers;
using TransCard.Handlers.Commands;
using TransCard.Handlers.Queries;
using TransCard.Data;
using TransCard.Middleware;
using TransCard.Models.Responses;
using TransCard.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are not configured.");

// Database
builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                var error = new
                {
                    type = "AuthenticationError",
                    title = "Authentication required.",
                    status = 401,
                    detail = string.IsNullOrEmpty(context.ErrorDescription)
                        ? "A valid JWT token is required to access this resource."
                        : context.ErrorDescription,
                    traceId = context.HttpContext.TraceIdentifier
                };
                return context.Response.WriteAsJsonAsync(error);
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                var error = new
                {
                    type = "AuthorizationError",
                    title = "Access denied.",
                    status = 403,
                    detail = "You do not have permission to access this resource.",
                    traceId = context.HttpContext.TraceIdentifier
                };
                return context.Response.WriteAsJsonAsync(error);
            }
        };
    });

builder.Services.AddAuthorization();

// Services (DI)
builder.Services.AddScoped<ProcessPaymentCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>>(sp =>
    new EventSourcingDecorator<ProcessPaymentCommand, ProcessPaymentResult>(
        sp.GetRequiredService<ProcessPaymentCommandHandler>(),
        sp.GetRequiredService<PaymentsDbContext>()));
builder.Services.AddScoped<IQueryHandler<GetPaymentByIdQuery, PaymentResponse?>, GetPaymentByIdQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPaymentEventsQuery, IReadOnlyList<PaymentEventResponse>>, GetPaymentEventsQueryHandler>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<PaymentProcessor>();

// Controllers + Swagger
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var response = new
            {
                type = "ValidationError",
                title = "One or more validation errors occurred.",
                status = 400,
                errors,
                traceId = context.HttpContext.TraceIdentifier
            };

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(response);
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Build
var app = builder.Build();

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    db.Database.EnsureCreated();
}

// Middleware pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
