using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SigpaBackend.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<SigpaContext>(options => 
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention()); // ¡Esta es la línea mágica!

var supabaseUrl = builder.Configuration["Supabase:Url"];

// Configuración de seguridad actualizada para llaves asimétricas (ES256/RS256)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority obliga a .NET a descargar la llave pública oficial de tu Supabase
        options.Authority = $"{supabaseUrl}/auth/v1";
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidAudience = "authenticated",
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateLifetime = true
        };
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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