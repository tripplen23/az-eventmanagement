using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Messaging.ServiceBus;
using Microsoft.OpenApi.Models;
using EventManagementApi.Entity;
using EventManagementApi.Database;
using Microsoft.AspNetCore.HttpsPolicy;
using EventManagementApi.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        builder => builder.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader());
});

// TODO: Configure Cosmos DB for PostgreSQL Cluster connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// TODO: Configure Cosmos DB NoSQL connection
builder.Services.AddSingleton<CosmosClient>(serviceProvider =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var account = configuration["CosmosDb:Account"];
        var key = configuration["CosmosDb:Key"];
        return new CosmosClient(account, key);
    });

builder.Services.AddScoped<CosmosDbService>();

// TODO: Configure Blob storage
string blobStorageConnection = builder.Configuration["BlobStorage:ConnectionString"];
builder.Services.AddSingleton(s => new BlobServiceClient(blobStorageConnection));
builder.Services.AddSingleton<BlobStorageService>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("EventProvider", policy => policy.RequireRole("EventProvider"));
    options.AddPolicy("User", policy => policy.RequireRole("User"));
});

// TODO: Configure service bus queue
builder.Services.AddSingleton(s => new ServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]));

builder.Services.AddSingleton<ServiceBusQueueService>(s =>
{
    var serviceBusClient = s.GetRequiredService<ServiceBusClient>();
    var config = s.GetRequiredService<IConfiguration>();
    return new ServiceBusQueueService(serviceBusClient, config);
});

// TODO: Configure Redis cache
// builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration["RedisCache:ConnectionString"]));

// TODO: Configure application insights
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration["ApplicationInsights:ConnectionString"]);

/*
// TODO: Increase the Timeout Settings
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MinRequestBodyDataRate = null;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 104857600; // 100 MB, adjust as needed
});

// TODO: Adjust the Max Request Body Size
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB, adjust as needed
});*/


// TODO: Configure swagger UI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Event Management System API",
        Version = "v1"
    });

    // Add security definitions
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below. ",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Add security requirements
    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

builder.Services.Configure<HttpsRedirectionOptions>(options =>
{
    options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
    options.HttpsPort = 7202;  // Specify your HTTPS port here
});

var app = builder.Build();

CreateRoles(app.Services).Wait(); // Create roles after running the API

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Add Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Event Management System API v1");
    c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
    c.DefaultModelExpandDepth(2);
    c.DefaultModelsExpandDepth(-1);
    c.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
    c.DisplayRequestDuration();
    c.EnableValidator();
    c.ShowExtensions();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors("AllowAllOrigins");
app.MapControllers();

app.Run();

async Task CreateRoles(IServiceProvider serviceProvider)
{
    using (var scope = serviceProvider.CreateScope())
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // List all the roles you need in your application
        string[] roleNames = { "Admin", "User", "EventProvider" };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }
    }
}