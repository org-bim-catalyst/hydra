// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using AskLucy.Areas.Identity.Models;
using Newtonsoft.Json;
using Google.Apis;
using Google.Apis.Auth.OAuth2;
using Microsoft.Build.Logging;
using Google.Apis.Util;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.PeopleService.v1;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Google.Apis.Http;
using Google.Apis.Services;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Google.Apis.Discovery;

namespace AskLucy.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ExternalLoginModel> _logger;

        public ExternalLoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            ILogger<ExternalLoginModel> logger,
            IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _logger = logger;
            _emailSender = emailSender;
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
        public string ProviderDisplayName { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

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
            public string Email { get; set; }
        }
        
        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string returnUrl = null)
        {
            // Request a redirect to the external login provider.
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                _logger.LogInformation("{Name} logged in with {LoginProvider} provider.", info.Principal.Identity.Name, info.LoginProvider);
                return LocalRedirect(returnUrl);
            }
            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }
            if (result.IsNotAllowed)
            {
                if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
                {
                    string Email = info.Principal.FindFirstValue(ClaimTypes.Email);
                    return RedirectToPage("./ResendEmailConfirmation", new { Email = Email });
                }
                return Page();
            }
            else
            {
                // If the user does not have an account, then ask the user to create an account.
                ReturnUrl = returnUrl;
                ProviderDisplayName = info.ProviderDisplayName;
                if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
                {
                    Input = new InputModel
                    {
                        Email = info.Principal.FindFirstValue(ClaimTypes.Email)
                    };
                }
                return Page();
            }
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            // Get the information about the user from the external login provider
            // https://www.appsloveworld.com/csharp/100/1610/google-people-api-c-code-to-get-list-of-contact-groups
            // https://www.daimto.com/asp-net-core-3-and-google-login/

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                switch (info.LoginProvider)
                {
                    case "Google":
                        {
                            user.FirstName = info.Principal.FindFirst(ClaimTypes.GivenName).Value;
                            user.LastName = info.Principal.FindFirst(ClaimTypes.Surname).Value;

                            string url = info.Principal.FindFirst("picture").Value;

                            var birthday = info.Principal.FindFirst("urn:google:birthday");
                            var gender = info.Principal.FindFirst("urn:google:gender");

                            using (HttpClient httpClient = new HttpClient())
                            {
                                byte[] thumbnail = await httpClient.GetByteArrayAsync(url);

                                if (thumbnail.Length > 0)
                                {
                                    user.ProfilePicture = thumbnail;
                                }
                                else
                                {
                                    user.ProfilePicture = Convert.FromBase64String(@"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RqOcobRgeUeEw7ATYcJh2ARtHB4yZHne6zgR52umSBMaqGZnIuTOBMzAXCCI0ci5gHtOuYD7ZGH9RnUf6hkYf1GdR/qGRh/UZ1F+oZGD9RnUf6hkYf1GdR/qGRh/UZ1H+oZGH9RnUf6hkQ9hKLoaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6Giuhoi9xP/AE1P/9oADAMBAAIAAwAAABDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzjJ321vDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzyX777779njzzzzzzzzzzzzzzzzzzzjzzzzTzzgf777777776njzjjTzzzjzzzTDzzzzzzzzzzzxf/wD++/8A/vvvvjfPPPPPPPPPPPPPPPPPPPPPPPPHf/vv/vvvvv8A7pTzzzzzzzzzzzzzzzzzzzzzzzzX77/7/wD+++++++8888888888888888888888889++++++++++++c888888888888888888888888n+++++++++++/U888888888888888888888888+/++++/wDvv/v69PPPPPPPPPPPPPPPPPPPPPPPPOvv/vvvv/vv/r/PPPPPPPPPPPPPPPPPPPPPPPPH/vv/AL77777767/zzzzzzzzzzzzzzzzzzzzzzzzX77777777777vzzzzzzzzzzzzzzzzzzzzzzzzx/8A++++++++++/c88888888888888888888888843/+/wDv/vv/AP8A/wDPPPPPPPPPPPPPPPPPPPPPPPPPC/8A/wC+/wDvvvu1PPPPPPPPPPPPPPPPPPPPPPPPPPN/vvvvvv8A7+vzzzzzzzzzzzzzzzzzzzzzzzzzzzjb7777777+3zzzzzzzzzzzzzzzzzzzzzzzzzzzyP777/7/AOr08888888888888888888888888888o++++/8AvqfPPPPPPPPPPPPPPPPPPPNPPPPPPPPKPvvv/vvqfPPPPPPPPPPPPPPPPPPPPPPPPPPPOPvvvvvvvv8Ajzzzzzzzzzzzzzzzzzzzzzzzzzy6X77777777++fzzzzzzzzzzzzzzzzzzzzzDr0/wC/+++++++++++ud74088888888888888mNe+++/wDvvv8A77777777777483Tzzzzzzzzzzg3/AO++/wDvv/v/AL7/AO/+/wD/AP7/AP8Av/r2vPPPPPPPPM/vvvvvvvvvvvvvvvvvvvvvvvvvvu1PPPPPPPPHfvvvv/vvvvvvvv8A7/7/AP8A/v8A/wD++++c88888888V++/+++++++/++/+++//AP8A77777775TzzzzzzzxX77777777//AO+++/8Avvv/AL77777775TzzzzzzzxX/wC+++++++++/wDv/v8A7/7/AO+/+++++U88888888OOOOOOOOOOOOOOOOOOOOOOOOOOOOOc88888888888888888888888888888888888888888888888888888888888888888888888888888888/8QAHREAAwEBAAMBAQAAAAAAAAAAAAERUEAQMGAgcP/aAAgBAwEBPxD+nUpS4N/dyL1v2L6Z4zxnjPTXS8Z/GvGegu15yxl3XEpfXeel4KXivPdJ4z+DWMsZeqEIQhCEJ4hCEIQhCeIQhCEIT5L/xAAdEQADAAMBAAMAAAAAAAAAAAAAAREwQFAxIGBw/9oACAECAQE/EP02EIiE4KXzhN1LE1trI1tLzjLzK9lcZcZcZZHx3srjLI9pFxt7a4yxPjPdT5z4z3ksDV3IJY5rwhM8ITQhNaEypbbUxLjLdeFbr9wrdfuFbr9w0pWUpSlKVlKUrKUpSlKUpSlKUr+pf//EACsQAQABAQcDBAIDAQEAAAAAAAERACExQEFQUWGBkaEwcbHwwdEgYOFwgP/aAAgBAQABPxD+iP8A5Omp9GfRn0ZqfRn0Z/pj/wBgn1D+L67TMnyI80mh2XlQio1ygvkUK2jTfD33/angWrpftCgiBcjI/wBCWlg4LLV99utMpe5tB7ruhShYveu7/IS/NwH3LmohRevylnimUbJYSeLjQgIyN0a9mmEV/AXrwUsRdi/uPPNM3IlVlX03QE2+BzOlQFNZLadjow65HkHllrzsKYKVsdhbDL1yIooi/bzHDRqR7C7M+KAHm2D1W0avb3RK3Pi+al0GaVcEdYiRIR96dWTEln7/AJd62rgXrZMnVW8kL23KHvVpyp2GQbBhZQ7sfoHnZqcEEP7hc6msURk5e5b+xZ3w6Aihdshyfuh8mANiOepebZqFh3phCe+VlcQkix2m1V3Zs7ak76Ho5axJWILs3Hhh6UaS+hAdsU9APziRUIwlzVscqXvFvnUfCxM08ltXZi8ajYrLvQfxinPFB31GBy1C8IfqssSyh+yn41GCyRHrPgOKdohgzn/TUS5k64SKGiCDkYfjEG7PvAoPmhfgJOAjUWgEEoei3zOIImkn5aNSET3/ACfwcIZ+rbRdqUW6xGQXi9haNS9prsv3iIBt2WpRt7QkuW4W4fesl/DK/OIbL/gH5qR18pfF8NzHGogyWRbteeIpTDoTbZD0sdsQQ1IfKiXvAnWpFojHgm1FSSOqC18HfFNegDk/Q99RzSFfXMO+KsQG+02eAPXUbwCB4fkDEjj34UfmgRAa4CDURJDUdEdWCPEYgICRToWeUoI1JYeB/UD5HETSWGvMmppDw1zZtXeTriIEyOxHnUxdlFbUG2wqFDCs3OlJDG2FAL68KCpbY96DMCbILtUEiJY1kb4xcICob1F5B24NWsUiLPqwkClvfGKAAXGmnoqaIBOg/M4RTyZ6ZS8GrraoG5J+HxhFQvU6Dw6uW3AOSLnc0YOfj30Wxs/J66uTBIkJvTLBlspt3S7BS3EZZN77nFEMINcAQGsCGE+8c1w/qnrIgJCO2ABUAVbgprDgA35H7/5rLdRnQ4OUvPJnv8+vdw7Gvd58d9qLta+t30euCf6x1v6XfgPpdut/S78B9Lt1v6XfgPpdutmIuq1SLDrfgDFXVYIFp0v1lYoSNpGG05c3g8VnncVgbBkcYDPE4rE2TM4oCVoCW04cnh80M6rJRAQLH7u3WhGS2XQ8nsQYSUkNl2PB7MlEBQt8bv0qdRfBqWEHWihz7J5Vnaajl4Gjm92ikoireuGSCIjIjdUcvMwcXu80QmfdPAs7xQYNSSB66Y9MLxA6tQALYO6CtgUP+kPNcM6PD2LjpjOWdHl7lz1rcFD/ALR8VIAtg7JKOuFwg9TRVM6kQ9/k0tSZ2SfsfiphRuPnS8RXKcGvOhcpwK8VECGXziPM1uYxP0PzUKP7vM0NSOOULVKUF33iw6pWzPE8Cw7tOIx/xcCr2dIueacAn/m5Fbs8TyLHuU4rvvNj0WhG0TEkyABKrYFPxXZMTfLp3p8fzrPmL+s6geH5zHxN3SKfmuyZm4yde9ESCJIjfhhSDYFq7Bm0QFLa7nLN8aowFLa7nLJ8UKRbEsXYZOEt6rQXrIOWlWlw32B+XVlWtx32D+6tyrQ3rMeRwTdV6exBszXpd31i9PYC2ZL1u7UYG6JgN0LClFJIzW1dYUUEhklo1ETEBspaYFBDD34L8a0wll7ck+cC+t8WtHPF8WBffba0+22wL7bbWn0W2BfVba0+22wL77bWn222Bfbba0+22wL7bbWn322BFwExoQCZDvrT58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+bgZjUhAzDb/pD/AG3/2Q==");
                                }
                            }
                        }
                        break;

                    case "Facebook":
                        {
                            user.FirstName = info.Principal.FindFirst(ClaimTypes.GivenName).Value;
                            user.LastName = info.Principal.FindFirst(ClaimTypes.Surname).Value;

                            string user_birthday = info.Principal.FindFirst("user_birthday").Value;
                            string url = info.Principal.FindFirst("picture").Value;


                            using (HttpClient httpClient = new HttpClient())
                            {
                                byte[] thumbnail = await httpClient.GetByteArrayAsync(url);

                                if (thumbnail.Length > 0)
                                {
                                    user.ProfilePicture = thumbnail;
                                }
                                else
                                {
                                    user.ProfilePicture = Convert.FromBase64String(@"/9j/4AAQSkZJRgABAQEAYABgAAD//gA7Q1JFQVRPUjogZ2QtanBlZyB2MS4wICh1c2luZyBJSkcgSlBFRyB2NjIpLCBxdWFsaXR5ID0gODUK/9sAQwAFAwQEBAMFBAQEBQUFBgcMCAcHBwcPCwsJDBEPEhIRDxERExYcFxMUGhURERghGBodHR8fHxMXIiQiHiQcHh8e/9sAQwEFBQUHBgcOCAgOHhQRFB4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4e/8IAEQgCWAJYAwEiAAIRAQMRAf/EABsAAQEAAwEBAQAAAAAAAAAAAAAGBAUHAwIB/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAH/2gAMAwEAAhADEAAAAeygAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAANfMFproH4st/mKF1m84HVP3mG5i2anbKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAANYZsjqMWz9/AAAAAbDXi93HKt2XSe28uUAAAAAAAAAAAAAAAAAAAAAAAAAAAYxhwf34WAAAAAAAAP38G+sOY5h0li5UoAAAAAAAAAAAAAAAAAAAAAAAACJsOZJ8igAAAAAAAAANn0HldoUIlAAAAAAAAAAAAAAAAAAAAAAAA0kLWyVgAAAAAAAAAADYa/9OqMfIlAAAAAAAAAAAAAAAAAAAAAAAAk5atkrAAAAAAAAAAAAL/baTdygAAAAAAAAAAAAAAAAAAAAAAAT8V0PnlgAAAAAAAAAAAF7uMDPlAAAAAAAAAAAAAAAAAAAAAAAA+OYdS58mrFAAAAAAAAAAPv4zTo30SgAAAAAAAAAAAAAAAAAAAAAAAIa5iDQiwAAAAAAAAABnYOwOiiUAAAAAAAAAAAAAAAAAAAAAAABGU88S4sAAAAAAAAAAbTV7kvGJlygAAAAAAAAAAAAAAAAAAAAAAAc49MnSWAAAAAAAAAAAMrFGz6FG2UAoAAAAAAAAAAAAAAAAAAAAAAE5G9K5rYAAAAAAAAAAAPQuNz8fcoAAAAAAAAAAAAAAAAAAAAAAADmfTIFNQKAAAAAAAAAAbbU0hYiUAAAAAAAAAAAAAAAAAAAAAAABL1HmcuWUbYAAAAAAAAAAtsekj7CgAAAAAAAAAAAAAAAAAAAAAAAAOXdR5omKKAAAAAAAAHodN9CUAAAAAAAAAAAAAAAAAAAAAAAAABz3oUck2KAAAAAAAAZuFvi3EoAAAAAAAAAAAAAAAAAAAAAAAAADU7YcqbHXWAAAAAAAALmR6QfQlAAAAAAAAAAAAAAAAAAAAAAAAAAA1fP+qTKSAoAAAAAB+/lSbPckoAAAAAAAAAAAAAAAAAAAAAAAAAAADw9/A5iLAAAAAAHTuY9OPcSgAAAAAAAAAAAAAAAAAAAAAAAAAAAPD38DmIsAAAAAAdO5j049xKAAAAAAAAAAAAAAAAAAAAAAAAAAAA1+FGniLAAAAAAHRedex09ot7KAAAAAAAAAAAAAAAAAAAAAAAAAPk+mlnCsk9R+WAAAAAAAAANxpx0TY8r28XrS7hfoAAAAAAAAAAAAAAAAAAAAwzMTeoLjVQ3nZR6THAAAAAAAAAAAAADIxxR7qCHUvvl22i7Te4XMAAAAAAAAAAAAAAPw/Wily00UwszcIAAAAAAAAAAAAAAAAAAAAM3dTA6JseVbA6K0W8l/QAAAAAAAAADwPmIxcSwAAAAAAAAAAAAAAAAAAAAAAAABtdUOmZPNehy+4AAAAAAAAERY8yT5FAAAAAAAAAAAAAAAAAAAAAAAAAAN9ofo6m8faUAAAAAAADUQNxD2AAAAAAAAAAAAAAAAAAAAAAAAAAAAX230W9lAAAAAAAA0UPcQ9gAAAAAAAAAAAAAAAAAAAAAAAAAAAFxvdFvZQAAAAAAANFD3EPYAAAAAAAAAAAAAAAAAAAAAAAAAAABcb3Rb2UAAAAAAADRQ9xD2AAAAAAAAAAAAAAAAAAAAAAAAAAAAXG90W9lAAAAAAAA1s7aCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWgi1oItaCLWg1uyAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD//xAAsEAABAgYCAgEDAwUAAAAAAAADAgQAAQUVQFASNBETMBQiMSAkMyEjYICQ/9oACAEBAAEFAv8AocpaUyW/apidVbRdm8JqbWcIdtlxKfn/AAN07C3hxUzrhalLn+kZSDgNUOiG9Qbl35iIEh5Ulk+Rs8OCGlQCfePXaGyXByHX8zSolDCKqCcBcBNuKg7k2QRSlrwfxDSpFHASoMjaOTJAExFFJiNXBG5GxkHFs6yf2Hxqe5m2NL+stidfrCqc1Kx6KfmHY1pfFnkU4vqd7Gvz+zJbL9jfYV/85NJn5YbCvy+3JovS2FcT5a5NHl4Y7Cpo5scmnp4stguXJK5cVY6Jclpl4Tsaqjg9x2Pc2Vb7uOw7uyrncx6d3tlXZfuMelS8v9ipyBJa/L7sejS/fIcBWTYP/P1hj+5pjgLMUUuU5v8AYVpHF5k0Ifk2wro/IMmjj9bPYPB+1rkDTzIhMko2LpPBzj0lHN9squni+x6Cn+9sq6LynHog+LXZEQkiE0saJ4qaYMokJkhG0JLwvEHLwPau5eHOGOXle2qieL7DYp5O9tXUeC4dERyd7aqi9rTDowuDXb1AHoc4LQMznTKSU7eotvqAfjBpbX0B3NVZcsCks924/g+dv/BunHX+dv19046/zt+vunHX+dv190+MMTf52Jhlb7l7UBhgpFlX84iLEtlURm27l4AEO35j4jR+YENngD7Kc5SkepNxw4qBy5DeoHFAKk3JEpynLVqVJMjVFsODVUqoKYpZ5YjFFMNVKmA1FsSEqkqWlK6bigtWHKC1JyuFrWuehQtaJiqTlECqw5wJ03LoDvW4YNVlTgrpwXVCdOBQGrKlAHrc2W6qQhw4eHNsG7w4Ya1IRMdwYYBvHpXE9mzelbzbmGceGcqQidHW4LtWp1tygKkwsKsuPYfb0Zx6z4J1+sM5znPbynOUwL9gcCrq4sdzSFcmOBW+nuaJ08Ct9Pc0Tp4Fb6e5ofTwK509zROngVvp7midPArfT3NE6eBW+nuaJ08CogU4BaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxaTxTgKbg/00/8QAFBEBAAAAAAAAAAAAAAAAAAAAoP/aAAgBAwEBPwE0n//EABQRAQAAAAAAAAAAAAAAAAAAAKD/2gAIAQIBAT8BNJ//xAA3EAABAwIDBQcDAgUFAAAAAAABAAIDETISQFAhIjFRYRMjQVJxcoJigaFCsQQgMDORYICQweH/2gAIAQEABj8C/wCQ6rnAeq/u19FwkP2Vkn+FxcPst2Zq2f6D3nVd5QqR92PyqucXHr/NuPc30K7wCQf4VMWB3J2v45HABYYdxvPx/qbrqt8pWE7j+R1zbtf4NWKR1f8Ar+vhf3jVvMe1d3IDrGzbIeARe81JyWxYZe8b+VjjdUaqZHeCMjztOVxM+45oSMP/AJqnZC1n75ev6DcqjUnPPgEXHicwYncWcPTUqeY0zLHeB2HUoh1OaY/m3UYvvmmdK6jEfXND3HUQeTs03qTqMg6VzUQ+nUS3mi0+BpmA3maIDlqT+u3MRe7U/iMxF7tTHtzEXrqbD9OYj1Ls3SNDuSiPrmB0BXZtkBdy1GWvmTGuO9GafbMPLeLm0Ueo4vMK5p8nlFNRbJ5TmgfF+3UZGcxmWsHiaINHAalIzk45hnTbqbuu3MSO5DU2TDw2HMF/nOpljhUFOcXl+zYMtE/EW7oxdUGtFANVcOuVaOmrSj6jlGjrq8nXblIh9Wrsk5imUx+Qau6nFu8MpjPF+3WC39J2tyTYx91QcBrGy8W5LE7+47j01ozxDb+oZAfxEo9o1uT2nIR+0a3J7TkI/aNbk9pyEftGtye05CP2jW3YzxFAMg3AeAoRrWGPff8AgLHI6pyGON1Cg2Tcf+Dq+86ruQWEbjOQymE77ORW66juR1KpNFuntD0VAezb0zFCcbeqo49meqqDXTKuIAWx2M/Su6YGfld49zs53b3NXesD/wALa7AfqVWkEaNvytXdsLvXYthDPRVe4uPXQqscWnotpDx1XeMLfTatyVugbz6nkF3UdOrlvyu9NK3JXLvY69WrdfQ8jm8MfeO/C3n0HIahuvqORWGTu3fjL45DRUtZy1SlzOSxxmuUMj+AWN/2HLVsbPuOaEjOByfZC1n76x2Rtf8AvknyeUVVTx1io4pknmFci/rs1pnTZkfkNa+RyPyGtfI5H5DWvkcj8hrXyOR+Q1r5HI/Ia18jkfkNa+RyPZsIBrXar41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr41fGr412byCa12f7Nf/EACwQAAECBAUFAAIDAAMAAAAAAAEAETFQ0fEhQEFRcWGBkaHwscEwYOEggJD/2gAIAQEAAT8h/wDQ5rJ3Jlh5AuklAHDgBVWFVRTl0rXTsS35QAcgR/Qwzd4CnUA6OPkurdCf/kbdYNg8kJjJx0PacT4uBe6dn/IURJJclyf4yIHfEJoL4g8Gea9UD9rg8DTh/OJhMATiO6ON1hgQo8e2vicNVgPF1KKhOck5IEk5MRqERDm6YKodAP6msAeAbnZP+DeMqJmw7ALBQGI1LaaHc9zrlwyRMMP2jACODiDMgwsYooLkcnMPymUOI6f7frM4nMTtGZd0mZgugUZj8XGa7dPZmPcof0tHSf8Awc089x7mO4Q9GOa7kvOMxCWgBBRY+MWYKLiIO6EJgDTJniH58x6/+ll6OZhidP5OYF5mWt5++Y7hJ9GZdZUlycB+Mw4Xdekej3RMQDXPRnexrch/mYxcBpOzxTA7k+jMXfQP9M05owZcmYsWMWTwc064xH/qY4Q4lbmIzJIgEHdQlgATErZgAcPmMYQfM790EemYd2qPJ/yZnAPk2YOYGLo4HxmbZpsQhwhzwZq5YUjSYAHgTcxsBNCughDK9IAE2+Mscp1MAIYCbczDyGU4NHxjN22GD7kZR2ZgU9zhWb4aP/uesphxi+Gk3IcMUUUPwGShMEuWw1QQNgMBOBGG5L9IgkQQxEcjhTq9DadNMEh97rkHzgETfmd/a2yH2tp39LbIfS2nf0tsh9Lad/S2yH0tp20qCfUEtkGlSR6gFp0+NpD4g++QHyB7LCHN0Ekd8inzljE8nKNnLGI4KEgdsjMmkgGpKeQTZh8p/dGkflHEucsMC4TeytI/KaSNmHym0gOoMs6mKJZOwLsA/tPgBbnEnlyhwzjy4Q4JsALcYkzAuwDe11MUC8mhAOwLnwnodfwFh4/px9rrIwnkXWRhMsP6e4pqB/fAUYB2JY+JA9APKlPQEPIfCjIGwsPUqhIGxLj2moCHgPhMQDwpzRLByncHAcHdEyCcOJgTANy4TODkOLugXDjLHDFoNSjp+2OvM0Gn756cIYYtRqOcobRgeUeEw7ATYcJh2ARtHB4yZHne6zgR52umSBMaqGZnIuTOBMzAXCCI0ci5gHtOuYD7ZGH9RnUf6hkYf1GdR/qGRh/UZ1F+oZGD9RnUf6hkYf1GdR/qGRh/UZ1H+oZGH9RnUf6hkQ9hKLoaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6GiuhoroaK6Giuhoi9xP/AE1P/9oADAMBAAIAAwAAABDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzjJ321vDzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzyX777779njzzzzzzzzzzzzzzzzzzzjzzzzTzzgf777777776njzjjTzzzjzzzTDzzzzzzzzzzzxf/wD++/8A/vvvvjfPPPPPPPPPPPPPPPPPPPPPPPPHf/vv/vvvvv8A7pTzzzzzzzzzzzzzzzzzzzzzzzzX77/7/wD+++++++8888888888888888888888889++++++++++++c888888888888888888888888n+++++++++++/U888888888888888888888888+/++++/wDvv/v69PPPPPPPPPPPPPPPPPPPPPPPPOvv/vvvv/vv/r/PPPPPPPPPPPPPPPPPPPPPPPPH/vv/AL77777767/zzzzzzzzzzzzzzzzzzzzzzzzX77777777777vzzzzzzzzzzzzzzzzzzzzzzzzx/8A++++++++++/c88888888888888888888888843/+/wDv/vv/AP8A/wDPPPPPPPPPPPPPPPPPPPPPPPPPC/8A/wC+/wDvvvu1PPPPPPPPPPPPPPPPPPPPPPPPPPN/vvvvvv8A7+vzzzzzzzzzzzzzzzzzzzzzzzzzzzjb7777777+3zzzzzzzzzzzzzzzzzzzzzzzzzzzyP777/7/AOr08888888888888888888888888888o++++/8AvqfPPPPPPPPPPPPPPPPPPPNPPPPPPPPKPvvv/vvqfPPPPPPPPPPPPPPPPPPPPPPPPPPPOPvvvvvvvv8Ajzzzzzzzzzzzzzzzzzzzzzzzzzy6X77777777++fzzzzzzzzzzzzzzzzzzzzzDr0/wC/+++++++++++ud74088888888888888mNe+++/wDvvv8A77777777777483Tzzzzzzzzzzg3/AO++/wDvv/v/AL7/AO/+/wD/AP7/AP8Av/r2vPPPPPPPPM/vvvvvvvvvvvvvvvvvvvvvvvvvvu1PPPPPPPPHfvvvv/vvvvvvvv8A7/7/AP8A/v8A/wD++++c88888888V++/+++++++/++/+++//AP8A77777775TzzzzzzzxX77777777//AO+++/8Avvv/AL77777775TzzzzzzzxX/wC+++++++++/wDv/v8A7/7/AO+/+++++U88888888OOOOOOOOOOOOOOOOOOOOOOOOOOOOOc88888888888888888888888888888888888888888888888888888888888888888888888888888888/8QAHREAAwEBAAMBAQAAAAAAAAAAAAERUEAQMGAgcP/aAAgBAwEBPxD+nUpS4N/dyL1v2L6Z4zxnjPTXS8Z/GvGegu15yxl3XEpfXeel4KXivPdJ4z+DWMsZeqEIQhCEJ4hCEIQhCeIQhCEIT5L/xAAdEQADAAMBAAMAAAAAAAAAAAAAAREwQFAxIGBw/9oACAECAQE/EP02EIiE4KXzhN1LE1trI1tLzjLzK9lcZcZcZZHx3srjLI9pFxt7a4yxPjPdT5z4z3ksDV3IJY5rwhM8ITQhNaEypbbUxLjLdeFbr9wrdfuFbr9w0pWUpSlKVlKUrKUpSlKUpSlKUr+pf//EACsQAQABAQcDBAIDAQEAAAAAAAERACExQEFQUWGBkaEwcbHwwdEgYOFwgP/aAAgBAQABPxD+iP8A5Omp9GfRn0ZqfRn0Z/pj/wBgn1D+L67TMnyI80mh2XlQio1ygvkUK2jTfD33/angWrpftCgiBcjI/wBCWlg4LLV99utMpe5tB7ruhShYveu7/IS/NwH3LmohRevylnimUbJYSeLjQgIyN0a9mmEV/AXrwUsRdi/uPPNM3IlVlX03QE2+BzOlQFNZLadjow65HkHllrzsKYKVsdhbDL1yIooi/bzHDRqR7C7M+KAHm2D1W0avb3RK3Pi+al0GaVcEdYiRIR96dWTEln7/AJd62rgXrZMnVW8kL23KHvVpyp2GQbBhZQ7sfoHnZqcEEP7hc6msURk5e5b+xZ3w6Aihdshyfuh8mANiOepebZqFh3phCe+VlcQkix2m1V3Zs7ak76Ho5axJWILs3Hhh6UaS+hAdsU9APziRUIwlzVscqXvFvnUfCxM08ltXZi8ajYrLvQfxinPFB31GBy1C8IfqssSyh+yn41GCyRHrPgOKdohgzn/TUS5k64SKGiCDkYfjEG7PvAoPmhfgJOAjUWgEEoei3zOIImkn5aNSET3/ACfwcIZ+rbRdqUW6xGQXi9haNS9prsv3iIBt2WpRt7QkuW4W4fesl/DK/OIbL/gH5qR18pfF8NzHGogyWRbteeIpTDoTbZD0sdsQQ1IfKiXvAnWpFojHgm1FSSOqC18HfFNegDk/Q99RzSFfXMO+KsQG+02eAPXUbwCB4fkDEjj34UfmgRAa4CDURJDUdEdWCPEYgICRToWeUoI1JYeB/UD5HETSWGvMmppDw1zZtXeTriIEyOxHnUxdlFbUG2wqFDCs3OlJDG2FAL68KCpbY96DMCbILtUEiJY1kb4xcICob1F5B24NWsUiLPqwkClvfGKAAXGmnoqaIBOg/M4RTyZ6ZS8GrraoG5J+HxhFQvU6Dw6uW3AOSLnc0YOfj30Wxs/J66uTBIkJvTLBlspt3S7BS3EZZN77nFEMINcAQGsCGE+8c1w/qnrIgJCO2ABUAVbgprDgA35H7/5rLdRnQ4OUvPJnv8+vdw7Gvd58d9qLta+t30euCf6x1v6XfgPpdut/S78B9Lt1v6XfgPpdutmIuq1SLDrfgDFXVYIFp0v1lYoSNpGG05c3g8VnncVgbBkcYDPE4rE2TM4oCVoCW04cnh80M6rJRAQLH7u3WhGS2XQ8nsQYSUkNl2PB7MlEBQt8bv0qdRfBqWEHWihz7J5Vnaajl4Gjm92ikoireuGSCIjIjdUcvMwcXu80QmfdPAs7xQYNSSB66Y9MLxA6tQALYO6CtgUP+kPNcM6PD2LjpjOWdHl7lz1rcFD/ALR8VIAtg7JKOuFwg9TRVM6kQ9/k0tSZ2SfsfiphRuPnS8RXKcGvOhcpwK8VECGXziPM1uYxP0PzUKP7vM0NSOOULVKUF33iw6pWzPE8Cw7tOIx/xcCr2dIueacAn/m5Fbs8TyLHuU4rvvNj0WhG0TEkyABKrYFPxXZMTfLp3p8fzrPmL+s6geH5zHxN3SKfmuyZm4yde9ESCJIjfhhSDYFq7Bm0QFLa7nLN8aowFLa7nLJ8UKRbEsXYZOEt6rQXrIOWlWlw32B+XVlWtx32D+6tyrQ3rMeRwTdV6exBszXpd31i9PYC2ZL1u7UYG6JgN0LClFJIzW1dYUUEhklo1ETEBspaYFBDD34L8a0wll7ck+cC+t8WtHPF8WBffba0+22wL7bbWn0W2BfVba0+22wL77bWn222Bfbba0+22wL7bbWn322BFwExoQCZDvrT58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+fPnz58+bgZjUhAzDb/pD/AG3/2Q==");
                                }

                            }
                        }
                        break;

                    default:
                        break;
                }


                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await _userManager.AddLoginAsync(user, info);
                    if (result.Succeeded)
                    {
                        _logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);

                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId = userId, code = code },
                            protocol: Request.Scheme);

                        //I disabled this line as it was throwing exception after trying to connect mail to gmail account
                        if (_userManager.Options.SignIn.RequireConfirmedEmail)
                        {
                            await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                            $"<div><img width='250' height='75' src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAPoAAABLCAIAAADWGA26AAAFVGlUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz4KPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIgeDp4bXB0az0iWE1QIENvcmUgNS41LjAiPgogPHJkZjpSREYgeG1sbnM6cmRmPSJodHRwOi8vd3d3LnczLm9yZy8xOTk5LzAyLzIyLXJkZi1zeW50YXgtbnMjIj4KICA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIgogICAgeG1sbnM6ZGM9Imh0dHA6Ly9wdXJsLm9yZy9kYy9lbGVtZW50cy8xLjEvIgogICAgeG1sbnM6ZXhpZj0iaHR0cDovL25zLmFkb2JlLmNvbS9leGlmLzEuMC8iCiAgICB4bWxuczp0aWZmPSJodHRwOi8vbnMuYWRvYmUuY29tL3RpZmYvMS4wLyIKICAgIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIKICAgIHhtbG5zOnhtcD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wLyIKICAgIHhtbG5zOnhtcE1NPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIgogICAgeG1sbnM6c3RFdnQ9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC9zVHlwZS9SZXNvdXJjZUV2ZW50IyIKICAgZXhpZjpQaXhlbFhEaW1lbnNpb249IjI1MCIKICAgZXhpZjpQaXhlbFlEaW1lbnNpb249Ijc1IgogICBleGlmOkNvbG9yU3BhY2U9IjEiCiAgIHRpZmY6SW1hZ2VXaWR0aD0iMjUwIgogICB0aWZmOkltYWdlTGVuZ3RoPSI3NSIKICAgdGlmZjpSZXNvbHV0aW9uVW5pdD0iMiIKICAgdGlmZjpYUmVzb2x1dGlvbj0iNzIuMCIKICAgdGlmZjpZUmVzb2x1dGlvbj0iNzIuMCIKICAgcGhvdG9zaG9wOkNvbG9yTW9kZT0iMyIKICAgcGhvdG9zaG9wOklDQ1Byb2Zp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UUNjO8vtrNe1C/OOjjledjdsrF64I8nivBO5CL5yvFLAaGuVAByZZ7Y2XtGHD19msNNuxceOTda8zZddNr1zc6AgWexGkLqEF1712HuPHEEirxSQy5kGnRkACKNU6wGX8hOEgdZYMoVqL/D8529vNNxmsy/KWWZuueW+N77xsWuuMGriwykTjOuKW2551vvff/ejHz25e/fElVdONBru2W9pz569//2/f390y1/8xdNf9rKLzun9/JzhjW/81ujwT/7k8W9+81XF39PTwW/91qPuumv+5ptXvoKbb763EPcLLqg99ambhgZks5l+9avHRufbL37x8CiZ9rKXXei6D+PSDMR9wFAXS9kN2b04ihhRKOl7ru+qXq9X9p00RUS11OrlFpgkkRRS1CrlWuDjhkltrECslkvj0+O1Rk0KbM4mE2M1YQwZC8YggSuEdAQEDl62qzw9EXfDDXkihfBrpUrNdwVzFNost3nuAIrAP9RsmsBXnseZDbNUa5Nr0+71uIisDmNLp9anPpxpc+BA+2tfWzEYiPD661ekynXFy19+0Qc/+OPhlj177vmDP3jsGoHetas+Onz967/2gQ/88AUv2PGCF+x4/ONnhMDduyduvvmZD3MrA3zxi4c+85lVc/Sf/dkTX/e6K850/M8z0tSMcgClkvrt3147rb31rVcnid69e/LKK8evvHJiw4bScNeNN1466i996lP7R8X9fC0ZWKlmwoKZQWa21gopi0TyNI6TJPW9oNdtW62RLQD7rtK5NtZY5tSwZdKZNZkJGk6t7HslX3muH/iu4yhJkOZoWAKRtZhbyA2AAWPYoOO5jQnP8x1rAdgS54KM0JlNkjTJdZinaV5p1JNet5VmnuMIIm10nNky24VOGEZx0WRjhJgZqeYbhonPilG9AgDPetaW0Y8bAF796ktHxX3//tY///Oxpz991cqV11wzs4Yduuee5XvuWX7nO++amPCvu27bC16w49nP3lqtrjLiz4RR4g8AHEe88pUXn8uJP4d46KH2yrJZAJde2qjX185sO3ZUP/ax55z29Je8ZNcb3vD1Xi8vhn/3dw+m6dMLFR5F+gtfODQ8cteu2jXXzDzs/QyYBwYoSpkBmMEYywxCCLams7xsGMqVqhJSAtZLQcmV2prMGCQKs9wwxHEetnt5lCrGilI13624quRKF0DHWZ6meZKZ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' /></div>" +
                            $"<h3>Hello {user.FirstName}</h3>" +
                            $"<strong>Congratulations!</strong><p> You've just created an account with asklucy.io</p>" +
                            $"<p>But you're not finished yet.</p>" +
                            $"<p>To activate your account and confirm ownership of this email address mustafa.salaheldin@yahoo.com, please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a></p>" +
                            $"<p>Remember to confirm your email address today, so you can start using your new account.</p>" +
                            $"<p>Sincerely,</p>" +
                            $"<p>Ask Lucy Support Team</p>" +
                            $"<p>support@asklucy.io</p>");
                        }

                        // If account confirmation is required, we need to show the link if we don't have a real email sender
                        if (_userManager.Options.SignIn.RequireConfirmedAccount)
                        {
                            return RedirectToPage("./RegisterConfirmation", new { Email = Input.Email });
                        }

                        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
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
                    $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml");
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
