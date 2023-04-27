// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using AskLucy.Areas.Identity.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AskLucy.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IWebHostEnvironment hostingEnvironment)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _hostingEnvironment = hostingEnvironment;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }


        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        //TODO: Add, download, and delete custom user data to Identity in an ASP.NET Core project
        //https://learn.microsoft.com/en-us/aspnet/core/security/authentication/add-user-data?view=aspnetcore-7.0&tabs=visual-studio
        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                user.ProfilePicture = Convert.FromBase64String(@"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RqOcobRgeUeEw7ATYcJh2ARtHB4yZHne6zgR52umSBMaqGZnIuTOBMzAXCCI0ci5gHtOuYD7ZGH9RnUf6hkYf1GdR/qGRh/UZ1F+oZGD9RnUf6hkYf1GdR/qGRh/UZ1H+oZGH9RnUf6hkQ9hKLoaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6Giuhoi9xP/AE1P/9oADAMBAAIAAwAAABDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzjJ321vDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzyX777779njzzzzzzzzzzzzzzzzzzzjzzzzTzzgf777777776njzjjTzzzjzzzTDzzzzzzzzzzzxf/wD++/8A/vvvvjfPPPPPPPPPPPPPPPPPPPPPPPPHf/vv/vvvvv8A7pTzzzzzzzzzzzzzzzzzzzzzzzzX77/7/wD+++++++8888888888888888888888889++++++++++++c888888888888888888888888n+++++++++++/U888888888888888888888888+/++++/wDvv/v69PPPPPPPPPPPPPPPPPPPPPPPPOvv/vvvv/vv/r/PPPPPPPPPPPPPPPPPPPPPPPPH/vv/AL77777767/zzzzzzzzzzzzzzzzzzzzzzzzX77777777777vzzzzzzzzzzzzzzzzzzzzzzzzx/8A++++++++++/c88888888888888888888888843/+/wDv/vv/AP8A/wDPPPPPPPPPPPPPPPPPPPPPPPPPC/8A/wC+/wDvvvu1PPPPPPPPPPPPPPPPPPPPPPPPPPN/vvvvvv8A7+vzzzzzzzzzzzzzzzzzzzzzzzzzzzjb7777777+3zzzzzzzzzzzzzzzzzzzzzzzzzzzyP777/7/AOr08888888888888888888888888888o++++/8AvqfPPPPPPPPPPPPPPPPPPPNPPPPPPPPKPvvv/vvqfPPPPPPPPPPPPPPPPPPPPPPPPPPPOPvvvvvvvv8Ajzzzzzzzzzzzzzzzzzzzzzzzzzy6X77777777++fzzzzzzzzzzzzzzzzzzzzzDr0/wC/+++++++++++ud74088888888888888mNe+++/wDvvv8A77777777777483Tzzzzzzzzzzg3/AO++/wDvv/v/AL7/AO/+/wD/AP7/AP8Av/r2vPPPPPPPPM/vvvvvvvvvvvvvvvvvvvvvvvvvvu1PPPPPPPPHfvvvv/vvvvvvvv8A7/7/AP8A/v8A/wD++++c88888888V++/+++++++/++/+++//AP8A77777775TzzzzzzzxX77777777//AO+++/8Avvv/AL77777775TzzzzzzzxX/wC+++++++++/wDv/v8A7/7/AO+/+++++U88888888OOOOOOOOOOOOOOOOOOOOOOOOOOOOOc88888888888888888888888888888888888888888888888888888888888888888888888888888888/8QAHREAAwEBAAMBAQAAAAAAAAAAAAERUEAQMGAgcP/aAAgBAwEBPxD+nUpS4N/dyL1v2L6Z4zxnjPTXS8Z/GvGegu15yxl3XEpfXeel4KXivPdJ4z+DWMsZeqEIQhCEJ4hCEIQhCeIQhCEIT5L/xAAdEQADAAMBAAMAAAAAAAAAAAAAAREwQFAxIGBw/9oACAECAQE/EP02EIiE4KXzhN1LE1trI1tLzjLzK9lcZcZcZZHx3srjLI9pFxt7a4yxPjPdT5z4z3ksDV3IJY5rwhM8ITQhNaEypbbUxLjLdeFbr9wrdfuFbr9w0pWUpSlKVlKUrKUpSlKUpSlKUr+pf//EACsQAQABAQcDBAIDAQEAAAAAAAERACExQEFQUWGBkaEwcbHwwdEgYOFwgP/aAAgBAQABPxD+iP8A5Omp9GfRn0ZqfRn0Z/pj/wBgn1D+L67TMnyI80mh2XlQio1ygvkUK2jTfD33/angWrpftCgiBcjI/wBCWlg4LLV99utMpe5tB7ruhShYveu7/IS/NwH3LmohRevylnimUbJYSeLjQgIyN0a9mmEV/AXrwUsRdi/uPPNM3IlVlX03QE2+BzOlQFNZLadjow65HkHllrzsKYKVsdhbDL1yIooi/bzHDRqR7C7M+KAHm2D1W0avb3RK3Pi+al0GaVcEdYiRIR96dWTEln7/AJd62rgXrZMnVW8kL23KHvVpyp2GQbBhZQ7sfoHnZqcEEP7hc6msURk5e5b+xZ3w6Aihdshyfuh8mANiOepebZqFh3phCe+VlcQkix2m1V3Zs7ak76Ho5axJWILs3Hhh6UaS+hAdsU9APziRUIwlzVscqXvFvnUfCxM08ltXZi8ajYrLvQfxinPFB31GBy1C8IfqssSyh+yn41GCyRHrPgOKdohgzn/TUS5k64SKGiCDkYfjEG7PvAoPmhfgJOAjUWgEEoei3zOIImkn5aNSET3/ACfwcIZ+rbRdqUW6xGQXi9haNS9prsv3iIBt2WpRt7QkuW4W4fesl/DK/OIbL/gH5qR18pfF8NzHGogyWRbteeIpTDoTbZD0sdsQQ1IfKiXvAnWpFojHgm1FSSOqC18HfFNegDk/Q99RzSFfXMO+KsQG+02eAPXUbwCB4fkDEjj34UfmgRAa4CDURJDUdEdWCPEYgICRToWeUoI1JYeB/UD5HETSWGvMmppDw1zZtXeTriIEyOxHnUxdlFbUG2wqFDCs3OlJDG2FAL68KCpbY96DMCbILtUEiJY1kb4xcICob1F5B24NWsUiLPqwkClvfGKAAXGmnoqaIBOg/M4RTyZ6ZS8GrraoG5J+HxhFQvU6Dw6uW3AOSLnc0YOfj30Wxs/J66uTBIkJvTLBlspt3S7BS3EZZN77nFEMINcAQGsCGE+8c1w/qnrIgJCO2ABUAVbgprDgA35H7/5rLdRnQ4OUvPJnv8+vdw7Gvd58d9qLta+t30euCf6x1v6XfgPpdut/S78B9Lt1v6XfgPpdutmIuq1SLDrfgDFXVYIFp0v1lYoSNpGG05c3g8VnncVgbBkcYDPE4rE2TM4oCVoCW04cnh80M6rJRAQLH7u3WhGS2XQ8nsQYSUkNl2PB7MlEBQt8bv0qdRfBqWEHWihz7J5Vnaajl4Gjm92ikoireuGSCIjIjdUcvMwcXu80QmfdPAs7xQYNSSB66Y9MLxA6tQALYO6CtgUP+kPNcM6PD2LjpjOWdHl7lz1rcFD/ALR8VIAtg7JKOuFwg9TRVM6kQ9/k0tSZ2SfsfiphRuPnS8RXKcGvOhcpwK8VECGXziPM1uYxP0PzUKP7vM0NSOOULVKUF33iw6pWzPE8Cw7tOIx/xcCr2dIueacAn/m5Fbs8TyLHuU4rvvNj0WhG0TEkyABKrYFPxXZMTfLp3p8fzrPmL+s6geH5zHxN3SKfmuyZm4yde9ESCJIjfhhSDYFq7Bm0QFLa7nLN8aowFLa7nLJ8UKRbEsXYZOEt6rQXrIOWlWlw32B+XVlWtx32D+6tyrQ3rMeRwTdV6exBszXpd31i9PYC2ZL1u7UYG6JgN0LClFJIzW1dYUUEhklo1ETEBspaYFBDD34L8a0wll7ck+cC+t8WtHPF8WBffba0+22wL7bbWn0W2BfVba0+22wL77bWn222Bfbba0+22wL7bbWn322BFwExoQCZDvrT58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+bgZjUhAzDb/pD/AG3/2Q==");

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);
                    //I disabled this line as it was throwing exception after trying to connect mail account
                    if (_userManager.Options.SignIn.RequireConfirmedEmail)
                    {
                        HtmlDocument htmDoc = new HtmlDocument();

                        string htmlFilePath = Path.Combine(_hostingEnvironment.WebRootPath, "templates", "email", "index.html");

                        string html = System.IO.File.ReadAllText(htmlFilePath);
                        htmDoc.LoadHtml(html);
                        htmDoc.GetElementbyId("span-user-name").InnerHtml = user.FirstName ?? string.Empty;
                        htmDoc.GetElementbyId("span-email-account").InnerHtml = user.Email;
                        htmDoc.GetElementbyId("link-verify-email").SetAttributeValue("href", HtmlEncoder.Default.Encode(callbackUrl));
                        htmDoc.Save(htmlFilePath);
                        htmDoc.Load(htmlFilePath);

                        await _emailSender.SendEmailAsync(user.Email, "Confirm your email", htmDoc.Text);
                    }

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
