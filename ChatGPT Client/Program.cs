using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using AskLucy.Data;
using AskLucy.Services;
using System.Net;
using AskLucy.Areas.Identity.Models;

//https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-7.0
var builder = WebApplication.CreateBuilder(args);
//var builder = WebApplication.CreateBuilder(new WebApplicationOptions
//{
//    EnvironmentName = Environments.Development
//});

var connectionString = builder.Configuration.GetConnectionString("ChatGPT_ClientContextConnection") ?? throw new InvalidOperationException("Connection string 'ChatGPT_ClientContextConnection' not found.");

builder.Services.AddDbContext<ChatGPT_ClientContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<ChatGPT_ClientContext>();

builder.Services.AddRazorPages();

builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration);

string CorsAllowAllOrigins = "_corsAllowAllOrigins";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: CorsAllowAllOrigins,
                      builder =>
                      {
                          builder.WithOrigins("*");
                      });
});

builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings.
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = false;

    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    // Cookie settings
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);

    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.SlidingExpiration = true;
});

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication()
   .AddGoogle(options =>
   {
       IConfigurationSection googleAuthNSection = builder.Configuration.GetSection("Authentication:Google");
       options.ClientId = googleAuthNSection.GetValue<string>("ClientId")!;
       options.ClientSecret = googleAuthNSection.GetValue<string>("ClientSecret")!;
   })
   .AddFacebook(options =>
   {
       IConfigurationSection FBAuthNSection = builder.Configuration.GetSection("Authentication:FB");
       options.ClientId = FBAuthNSection.GetValue<string>("ClientId")!;
       options.ClientSecret = FBAuthNSection.GetValue<string>("ClientSecret")!;
   })
   //.AddMicrosoftAccount(microsoftOptions =>
   //{
   //    microsoftOptions.ClientId = builder.Configuration.GetSection("Authentication:Microsoft").GetValue<string>("ClientId")!;
   //    microsoftOptions.ClientSecret = builder.Configuration.GetSection("Authentication:Microsoft").GetValue<string>("ClientSecret")!;
   //})
   //.AddTwitter(twitterOptions =>
   //{
   //    twitterOptions.ConsumerKey = builder.Configuration.GetSection("Authentication:Twitter").GetValue<string>("ConsumerAPIKey");
   //    twitterOptions.ConsumerSecret = builder.Configuration.GetSection("Authentication:Twitter").GetValue<string>("ConsumerSecret");
   //    twitterOptions.RetrieveUserDetails = true;
   //})
   ;
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHttpsRedirection(options =>
    {
        options.RedirectStatusCode = (int)HttpStatusCode.PermanentRedirect;
        options.HttpsPort = 443;
    });
}

var app = builder.Build();

//Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}").RequireAuthorization(); ;

app.Run();
