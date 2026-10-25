using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SigpaBackend.Data;
using SigpaBackend.Repositories;
using SigpaBackend.Services;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No se configuró la conexión a PostgreSQL. Define ConnectionStrings:DefaultConnection " +
        "con dotnet user-secrets o la variable de entorno DATABASE_URL antes de iniciar SigpaBackend.");
}

builder.Services.AddDbContext<SigpaContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

var supabaseUrl = builder.Configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");

if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    throw new InvalidOperationException("Configure Supabase:Url o SUPABASE_URL para validar los JWT.");
}
supabaseUrl = supabaseUrl.TrimEnd('/');

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabaseUrl}/auth/v1";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidAudience = "authenticated",
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Coordinador", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new CoordinadorRequirement());
    });
});

builder.Services.AddScoped<UserAccessService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<IAuthorizationHandler, CoordinadorAuthorizationHandler>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IWelcomeEmailService, SmtpWelcomeEmailService>();
builder.Services.AddHttpClient<ISupabaseAdminUserService, SupabaseAdminUserService>();
builder.Services.AddHttpClient<SupabaseStorageService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors("PermitirFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<PostgresSchemaErrorMiddleware>();
app.UseMiddleware<ActiveAccountMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.Run();