using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using AskLucy.Data;
using AskLucy.Services;
using System.Net;
using AskLucy.Areas.Identity.Models;
using static System.Formats.Asn1.AsnWriter;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Google.Apis.PeopleService.v1.Data;
using System.Drawing;
using AskLucy.Classes;
using Microsoft.AspNetCore.Builder;

//https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-7.0
var builder = WebApplication.CreateBuilder(args);
//var builder = WebApplication.CreateBuilder(new WebApplicationOptions
//{
//    EnvironmentName = Environments.Development
//});
builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.AddDebug();
});

builder.Services.AddTransient<ErrorHandlingMiddleware> ();

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

    options.SignIn.RequireConfirmedAccount = true;
    options.SignIn.RequireConfirmedEmail = true;
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

// Persist additional claims and tokens from external providers in ASP.NET Core
// https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/additional-claims?view=aspnetcore-7.0

builder.Services.AddAuthentication()
   .AddGoogle(options =>
   {
       IConfigurationSection googleAuthNSection = builder.Configuration.GetSection("Authentication:Google");
       options.ClientId = googleAuthNSection.GetValue<string>("ClientId")!;
       options.ClientSecret = googleAuthNSection.GetValue<string>("ClientSecret")!;
       //options.AccessDeniedPath = new PathString("/");
       //options.Scope.Add("https://www.googleapis.com/auth/user.birthday.read");
       options.SaveTokens = true;
       options.UserInformationEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
       options.ClaimActions.MapJsonKey(ClaimTypes.Gender, "user_gender");
       options.ClaimActions.MapJsonKey(ClaimTypes.DateOfBirth, "user_birthday");

       options.Events.OnCreatingTicket = (context) =>
       {
           string? picture = context.User.GetProperty("picture").GetString();
           //string? dob = context.User.GetProperty("birthdate").GetString();

           context.Identity!.AddClaim(new Claim("picture", picture!));

           List<AuthenticationToken> tokens = context.Properties.GetTokens().ToList();

           tokens.Add(new AuthenticationToken()
           {
               Name = "TicketCreated",
               Value = DateTime.UtcNow.ToString()
           });

           context.Properties.StoreTokens(tokens);

           return Task.CompletedTask;
       };
   })
   .AddFacebook(options =>
  {
       //https://stackoverflow.com/questions/45855660/how-to-retrieve-facebook-profile-picture-from-logged-in-user-with-asp-net-core-i
       //https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/additional-claims?view=aspnetcore-7.0
       IConfigurationSection FBAuthNSection = builder.Configuration.GetSection("Authentication:FB");
       options.ClientId = FBAuthNSection.GetValue<string>("ClientId")!;
       options.ClientSecret = FBAuthNSection.GetValue<string>("ClientSecret")!;
       options.SaveTokens = true;
       //options.AccessDeniedPath = new PathString("/");
       options.Fields.Add("picture");
       //options.Fields.Add("birthday");

       options.Events.OnCreatingTicket = (context) =>
       {
           var picture = context.User.GetProperty("picture").GetProperty("data").GetProperty("url").ToString();
           //var birthday = context.User.GetProperty("user_birthday").ToString();

           context.Identity!.AddClaim(new Claim("picture", picture!));
           //context.Identity!.AddClaim(new Claim("birthday", birthday!));

           List<AuthenticationToken> tokens = context.Properties.GetTokens().ToList();

           tokens.Add(new AuthenticationToken()
           {
               Name = "TicketCreated",
               Value = DateTime.UtcNow.ToString()
           });

           context.Properties.StoreTokens(tokens);

           return Task.CompletedTask;
       };
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

// Todo: to complete exception handling
// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-7.0
// https://www.c-sharpcorner.com/article/exception-handling-3-in-asp-net-core-mvc/
// https://stackoverflow.com/questions/56127508/how-to-a-i-redirect-to-custom-error-handler-page

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHttpsRedirection(options =>
    {
        options.RedirectStatusCode = (int)HttpStatusCode.PermanentRedirect;
        options.HttpsPort = 443;
    });
}

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

//Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
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
