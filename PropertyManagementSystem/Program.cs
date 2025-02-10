using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using PropertyManagementSystem.Attributes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var domain =  builder.Configuration["Domain"];

// Add framework services.
builder.Services
    .AddControllersWithViews()
    // Maintain property names during serialization. See:
    // https://github.com/aspnet/Announcements/issues/194
    .AddNewtonsoftJson(options => options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
// Add Kendo UI services to the services container
builder.Services.AddKendo();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("ApplicationDbContextConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));


builder.Services.AddDbContext<DAL>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddHttpClient();
builder.Services.AddRazorPages();

builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<SsrsService>();
builder.Services.AddControllersWithViews();
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddTransient<EmailService>();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

builder.Services.AddScoped<UserManager<ApplicationUser>>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Override the default login path
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // SameSite settings
    options.Cookie.Domain = domain; // Ensure this matches app1
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    // Other cookie settings
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveRolePolicy", policy =>
    {
        policy.Requirements.Add(new ActiveRoleRequirement(new string[] { }));
    });
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".AspNet.SharedAuthCookie"; // Ensure this matches app1
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/Identity/Account/Login";
        options.LogoutPath = "/Identity/Account/Logout";
        options.Cookie.SameSite = SameSiteMode.Lax; // or SameSiteMode.None if required
        options.Cookie.Domain = domain; // Ensure the same domain is set as app1
    });

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\Keys\")) // Ensure same key directory as app1
    .SetApplicationName("SharedCookieApp");

builder.Services.AddSingleton<IAuthorizationHandler, ActiveRoleHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, CustomAuthorizationPolicyProvider>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// app.UseCors(builder =>
//     builder.AllowAnyOrigin()
//            .AllowAnyMethod()
//            .AllowAnyHeader());


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "ReservationDetails",
    pattern: "Accommodation/ReservationDetails/{Id}",
    defaults: new { controller = "Accommodation", action = "ReservationDetails" }
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Report}/{action=GenerateReport}/{_startDate?}/{_endDate?}/{_preparedBy?}");

app.UseDeveloperExceptionPage();

app.MapRazorPages();

app.Run();
