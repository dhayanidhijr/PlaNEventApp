using System.Text;
using Amazon;
using Amazon.BedrockAgentCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Infrastructure;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.Configure<AgentCoreOptions>(builder.Configuration.GetSection(AgentCoreOptions.SectionName));

builder.Services.AddSingleton<IAmazonBedrockAgentCore>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentCoreOptions>>().Value;
    var regionName = string.IsNullOrWhiteSpace(options.Region)
        ? "us-east-1"
        : options.Region;

    return new AmazonBedrockAgentCoreClient(RegionEndpoint.GetBySystemName(regionName));
});
builder.Services.AddScoped<IAgentCoreChatService, AgentCoreChatService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddSignInManager<SignInManager<ApplicationUser>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IActivityService, ActivityService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy =>
        policy.AllowAnyOrigin()
        //  policy.WithOrigins(builder.Configuration["ClientUrl"] ?? "https://localhost:7209")
            .AllowAnyMethod()
            .AllowAnyHeader());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PlaNEvent API",
        Version = "v1",
        Description = "Calendar, occurrence, booking and admin service for PlaNEvent."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter JWT token in format: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    options.OperationFilter<McpExamplesOperationFilter>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in Roles.All)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var adminEmail = builder.Configuration["BootstrapAdmin:Email"] ?? "admin@planevent.local";
    var adminPassword = builder.Configuration["BootstrapAdmin:Password"] ?? "Admin123!";

    var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
    if (existingAdmin is null)
    {
        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            DisplayName = "Platform Admin",
            PublicSlug = "admin"
        };

        var create = await userManager.CreateAsync(admin, adminPassword);
        if (create.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, Roles.Admin);
            await userManager.AddToRoleAsync(admin, Roles.Standard);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("Client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
