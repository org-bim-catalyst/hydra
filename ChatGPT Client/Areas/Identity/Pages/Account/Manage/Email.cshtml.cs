// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using AskLucy.Areas.Identity.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace AskLucy.Areas.Identity.Pages.Account.Manage
{
    public class EmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public EmailModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            IWebHostEnvironment hostingEnvironment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _hostingEnvironment = hostingEnvironment;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public bool IsEmailConfirmed { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

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
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            [Display(Name = "New email")]
            public string NewEmail { get; set; }
        }

        private async Task LoadAsync(ApplicationUser user)
        {
            var email = await _userManager.GetEmailAsync(user);
            Email = email;

            Input = new InputModel
            {
                NewEmail = email,
            };

            IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostChangeEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var email = await _userManager.GetEmailAsync(user);
            if (Input.NewEmail != email)
            {


                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmailChange",
                    pageHandler: null,
                    values: new { area = "Identity", userId = userId, email = Input.NewEmail, code = code },
                    protocol: Request.Scheme);

                HtmlDocument htmDoc = new HtmlDocument();

                string htmlFilePath = Path.Combine(_hostingEnvironment.WebRootPath, "templates", "email", "index.html");

                string html = System.IO.File.ReadAllText(htmlFilePath);
                htmDoc.LoadHtml(html);
                htmDoc.GetElementbyId("span-user-name").InnerHtml = user.FirstName;
                htmDoc.GetElementbyId("link-verify-email").SetAttributeValue("href", HtmlEncoder.Default.Encode(callbackUrl));

                await _emailSender.SendEmailAsync(email, "Confirm your email", htmDoc.Text);

                StatusMessage = "Confirmation link to change email sent. Please check your email.";
                return RedirectToPage();
            }

            StatusMessage = "Your email is unchanged.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSendVerificationEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var email = await _userManager.GetEmailAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, code = code },
                protocol: Request.Scheme);
                await _emailSender.SendEmailAsync(email, "Confirm your email",
                $"<div><img src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAPoAAABLCAIAAADWGA26AAAFVGlUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz4KPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIgeDp4bXB0az0iWE1QIENvcmUgNS41LjAiPgogPHJkZjpSREYgeG1sbnM6cmRmPSJodHRwOi8vd3d3LnczLm9yZy8xOTk5LzAyLzIyLXJkZi1zeW50YXgtbnMjIj4KICA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIgogICAgeG1sbnM6ZGM9Imh0dHA6Ly9wdXJsLm9yZy9kYy9lbGVtZW50cy8xLjEvIgogICAgeG1sbnM6ZXhpZj0iaHR0cDovL25zLmFkb2JlLmNvbS9leGlmLzEuMC8iCiAgICB4bWxuczp0aWZmPSJodHRwOi8vbnMuYWRvYmUuY29tL3RpZmYvMS4wLyIKICAgIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIKICAgIHhtbG5zOnhtcD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wLyIKICAgIHhtbG5zOnhtcE1NPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIgogICAgeG1sbnM6c3RFdnQ9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC9zVHlwZS9SZXNvdXJjZUV2ZW50IyIKICAgZXhpZjpQaXhlbFhEaW1lbnNpb249IjI1MCIKICAgZXhpZjpQaXhlbFlEaW1lbnNpb249Ijc1IgogICBleGlmOkNvbG9yU3BhY2U9IjEiCiAgIHRpZmY6SW1hZ2VXaWR0aD0iMjUwIgogICB0aWZmOkltYWdlTGVuZ3RoPSI3NSIKICAgdGlmZjpSZXNvbHV0aW9uVW5pdD0iMiIKICAgdGlmZjpYUmVzb2x1dGlvbj0iNzIuMCIKICAgdGlmZjpZUmVzb2x1dGlvbj0iNzIuMCIKICAgcGhvdG9zaG9wOkNvbG9yTW9kZT0iMyIKICAgcGhvdG9zaG9wOklDQ1Byb2ZpbGU9InNSR0IgSUVDNjE5NjYtMi4xIgogICB4bXA6TW9kaWZ5RGF0ZT0iMjAyMy0wNC0xM1QxODowODo0MCswNDowMCIKICAgeG1wOk1ldGFkYXRhRGF0ZT0iMjAyMy0wNC0xM1QxODowODo0MCswNDowMCI+CiAgIDxkYzp0aXRsZT4KICAgIDxyZGY6QWx0PgogICAgIDxyZGY6bGkgeG1sOmxhbmc9IngtZGVmYXVsdCI+ZW1haWwgYmFubmVyPC9yZGY6bGk+CiAgICA8L3JkZjpBbHQ+CiAgIDwvZGM6dGl0bGU+CiAgIDx4bXBNTTpIaXN0b3J5PgogICAgPHJkZjpTZXE+CiAgICAgPHJkZjpsaQogICAgICBzdEV2dDphY3Rpb249InByb2R1Y2VkIgogICAgICBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZmZpbml0eSBQaG90byAxLjEwLjAiCiAgICAgIHN0RXZ0OndoZW49IjIwMjMtMDQtMTNUMTg6MDg6NDArMDQ6MDAiLz4KICAgIDwvcmRmOlNlcT4KICAgPC94bXBNTTpIaXN0b3J5PgogIDwvcmRmOkRlc2NyaXB0aW9uPgogPC9yZGY6UkRGPgo8L3g6eG1wbWV0YT4KPD94cGFja2V0IGVuZD0iciI/Ps9MGe0AAAGBaUNDUHNSR0IgSUVDNjE5NjYtMi4xAAAokXWRzyvDYRzHX9uIsKY4ODgsjZMx1OLisMUoHGbKcNm++6X249v3O0muynVFiYtfB/4CrspZKSIlV87EhfX1+W6rLdnz9Dyf1/N+Pp9Pn+fzgDWUVjJ6gwcy2bwWDPici+ElZ9MrDdhlDuKNKLo6Oz8Zou74esBi2ju3mau+37+jNRbXFbA0C48rqpYXnhKeWc+rJu8KdyqpSEz4XLhfkwKF7009WuY3k5Nl/jFZCwX9YG0XdiZrOFrDSkrLCMvLcWXSa0qlHvMlbfHswrzYHlnd6AQJ4MPJNBP48TLEmOxe3AwzICfqxHtK8XPkJFaRXWUDjVWSpMjTL+qaZI+LTYgel5lmw+z/377qiZHhcvY2HzS+GMZHLzTtQLFgGN/HhlE8AdszXGWr8bkjGP0UvVDVXIfg2IKL66oW3YPLbeh6UiNapCTZZFkTCXg/A3sYOm6hZbncs8o9p48Q2pSvuoH9A+gTf8fKL7YpaAqbhzFpAAAACXBIWXMAAAsTAAALEwEAmpwYAAAgAElEQVR4nO29d7RkV3UnvPc+59xU+eXO3epWFmqCZCQymKgPWBiDBwQIa+BjWB5sOYA9sFj2DOAZ7EUc1oBpY30WHjLCNjZgCRgMmCQhLKAVWt1S5+6XK958ztnfH7eqXr3XQd3CMGDeT6333rmpblXtu8/evx0OTk/MIAAgrAIDYrGJiQgAEIGIEBAQGKA4A1efOfyrOBUREVEIEkREpISQJKREgUQExnCS60Rro9kyG2uYuf/ixR8MDLwyHGxg5tEjeXDQyr2v/I0AcOcP7ty8ZTOsYx0AshDZvqTiQGSHW/tSj0TF7/6PlROKv3m4CwtR9VxHkETCwFMlR5YcFThCEiKBBCa2OXMv5yTNwyTPte1mOtNGa7MitggIwIMHj5mLDYCIgAw82MUIDEjDh6S4E2bAwZO5jnUUkEPRXRFnYEDGoaQjYl/6ERELDV/Ime1fpBA/KHa4UvquQ4Rlz2kEquo7YyV3rOrXq67rCCnIWpskebsTdrtxM1VZbihLW6mYD0071XlutLHcV9LIfb3NfREfPmgAiMDMiMg88lQADx6S/ltZxzqGkAi8osP7gj9U64g0lHlAQgIgREmFukVAMmwLeUQERCp7brnkAfBY4NVKqh64E41gvF6q+Mr3ncBXvu8GgeN70mrTXO7ce/+x+4+3tCM2uklF5Qfb3BOEzElutTEWwHLfgCn0ti2kH1fJMSJaOxgjACODRcARq2Yd6wAAkGvGiH3tPSLnAABESEgEIIn6JgwO5byv78cqQdl3XEljFb8WuJ4rAs9xHZWneQTMwIhsrA27PaszNrpW8XZfNr1rU+W+A7MnFvJJH12CVpy6xPMxNTOKMpMba5GZERiZLSEOLPfhnMSFju8/AYwMjIh9w38d6xiBXLEO+mLb1+uwIupARIQoCEVhNyMQoSSplBCCgAERSq4zVvEDV/iOFIRCoCJyBPmuqteCSq1UDjw/cBxXOJKsNnGv25yd6y0vCTYXz8iqActYlogs09z6S3lZCl1yIgPN2PRSbW1hOxWGOQzNp2Jy6m9iAGQEAMa+pb+u4NcxAjnkWQpRL5QiUV/WCQt1D4WgGwBJWPbcDdONRikQxGBM4RBaa5FQALBlJala9qplr+SrUuD5nkMM1uQ6Y2ExFcB5hqynN47XqurQvQ8tz3ZKEhCxXJPSUZ1ICwnTsdWWDci5RB7qeAudnrHSMls2xd0C9t3VgpxZoW8KUR+dA9axDgAAkCtGAaxS7UMgIiEVpoEksW3zxm2bZxQhsGads87ZarYWGIlQEkpCR5JgY3MtAlcQIefIVpBfHRsr1etg8qjTTnvduNfOk9z3neZc1o2SksSJqbLnVyxir5ellm2aOFLsqAaBK/eyt9RJABiI+u4CMyAB84CKYegreuSBsbVu0axjFH3bHUc5yKF1M6DVEbigZWrl8qbJcclGARJYBm1BWzZsNQCiRUmoSLhS+L5bKQeOQ4jWr4xNbt0ysWmz63qsU0E4vsVBk9o0TDutzvyJSu2BfT+6v720VApk0KiWqtVpIIc63VYWp4lluyGo2unK3Ro6UUIIxnJfr/eFfUBKcl/2sa/c7WlCCuv4JYaEVeGhgS08atADMLMgQQQTY42kOadN6kiSQiAiEEiphCShpBJCIDpKuo70XAfYekF1864L6jMbytWaVwooC8OFh5oL8yePHl+cW9K5LpX9sUZQq7nXPPnRxw+dPHL/g5VEz+y6AI4ebi+2sjAW1hJw2umOl+Vlm8fvPDCnbV6QoRaYGQwCAQKCKRzWQsyZYd1qX8cpkAArEaKhVhwAaeC0MrAQ0lcYLS3ZLBGIUim/HPiez4LZMmeaFSvfc1zF1gqkmS2bxzZM+bWG53sct5qHf5S15qJOqzm/sLTYbC53j812FptJnOSbJoLLt4+Nj9e3X7il2+lapvrGra355ZbqdNopx7kqQ9aLyyX/gk3j+48tMBtARAZAoL4NU5g1ff2ODBbglLezjl92FMbMQJcPZIOZCQn6UZu+hhdEhR2f5UYLAMfR2iZpmlvtOa5T8t2Sr6Rgw+MTjelNGysTY8IveSU/7y625o52lxc6S8txp700v/TgoYX5VuQpWuyEea5bvTgK48u3xhfs2qyEXDh6dPPFl1XHG525pTTOOmGetSO35oVJunl8bKkTzTfb1PdUhwkEPEJQcvHs8jAOto51AMAq3n0kSEmEA0FHCyAYGIGEsIi1iSkCi0IQIkgkKYMgCAJPSYHMQjq1Sml8y1Z/cgocp1SRWXvhrn/60rFDRyyDYWz3omOLvS0TpYlGad+RZtlVuQUJZqGTLbXi2lLXc1Xr5OKGnVCb3tSenUt6cZrqRHPUaZfGgrlOtGPDRLMXZVlePKO2CEHZlQwCZBwM1rGOVeiLe6Ef+ykoA4kv/LzCnGcA5SjH9QK/4nkyTyM2hhAd13EdCcwoHBQSlF/btlNV66j8UkDdE8fCuUMbN481qmppqfXgoblONxnz5f1HWzVPNKpBN0x8V2UZIMBsO/XmO9s2j4fNbrvdqdQmg8akPNlyRKaNZWSO2kGlmhJtm5l86MS80brQ49wPumI/W6x/v0Nafh3r6IMACmtgKCjDrK+VRMTCNp6enPRdFWe5KtVKlVqlUilXq36pJB3fKdU0i1jz5M5LnVIDlO+XK91mt1QfG9sw02529t578Id7D+dplhqe72lP0kJXIwCgyBkMCAPYTsx8O+lFeS/OW7NzwnHdUlW4PqMwxuaZFiYt645Ns01jVc9xEIkBzNBkGbKTw3v+GX+W6/i5hxwhGwv3rx+Dh36AvjiMhRSlkg9ZqnXa0WngOX4pkEJaZmO5tdhSSu14zNVAot0Jt05Ox532hk0zd33xM3d86w4lyPclue6BE21FpDUUgc8wYxQiTa1AzAGJMYzzVicGwO7SchRGslpzqhV3qZsZ2411GmkX266oZJnaOFZ7aDbX1vTJ9WEmDSJbO4i2rrOQ61gFORB2GIo4QF9Wirg8IwCj57hSuVKbPI1ZqTS12lhC0GkWh2G5Vh3ftks6fhqGMzsvjqJuxVGfvfn/++rt3yg5QjMEnnKVsECpYdeVOkfXsSQlMjsWXEGI4ApkIXILTGJhvjmx1AqXlw8fnI+6aZLkiQFLhpLEg5PIU5vqpWNLUutcw4qDClBkrsHIvLSOdaxgVYpY4aquFX8GQNbaAAnreMr1bJ4ncU8bbbRRjhif3liemK5v2Jr1ltj1SIikE0Mv+vsvfZsMuI6QSmgDjlLVQClHuZ6bZhoZBIEA9HzXUZKNUcBlT/qejLs9neqj9z+4tLCQ5zpKcgmIwJMSXJu2Q82GHNepuE6SZmAGtszg5yChgHkkgX8d64ARV7WfaTUsjxgA+6JPyIReZTyxVnKusljnueu4fn1MKNevjoWdJsfdysR0p7lYduCeb35r61hNCfIc8jw3CNygVKqUAj/wlaTCM3Y913cksLW5IbQCGdnoTAeB31peas3Px91e2It9iWHGMx56YA6n0LPoOQZzU/HUYk8A5KO3jIjczxled1TXsRYrSQRri/D6YXkAACFIScfxKtWpmTzuRksdxy/XpidczzVZAhbcUiWcOyaEcFyfODNJ7DE/59rH+CUPhQjKvuM6QlBRKKIcFVT8Wr3WGKtKSWmY9JqtXruTRXGaJL12JwKL2FjIZjnPKg7FqZ3yyEU7l4DWXJOWhOiBlMIOpiGGgXAP2HcGHCTLr2MdA0iAgaXO/Wq9PklDfU+VEKWQlVrFD4I8SeJ2J2dRqU/XNmxKO0thq7XhwsviTlsnoaxNCIGSPJFF2zZOzYzVxmamy/WadGSRaJanKQgqlX2v7KnAJ0HMGtJcR5NplMbtbpqkCydmF2dnpZQ6z/M47Sy2SpI8AScibGfWMvvAgSMT5WKmRyv9+m8EAIZ8JPbTgdexjgIrSQR9FFJfaMx+miR5vu/7JeU4Ool6YaiCclBrMJtwaUH5ZSlVb/Zo1gv9yU3WsuO7JafCvlcuBaWy73mOE3iIAgmwWhEShSJyJArZZ4KsIYsKGFwF1o5NTyrfr3Q61sLiyfmSo8DYluFY68xaiVh2pPED6TgQRrjCtq/CgJhBC+uB1XWsgIpf/aR3HCHvBs8AEQmlyrVaEPitxfnccqnW8MtB0lwyxjRmNsbtpbTTNEhCKGs0Arh+yZWOkIqQSAhAQkGIElECSgACFsAEQAASSAIQAhGRFKRI+J7veUG5Vi3XqqRkytCNdE+zYfQIPN9Pg7phK4QYzECrMUyJWLdl1rEafXHv2wIjBZ9DIJLvBdV63VUi7vWqYxONqQ3IrPPEDcrA3JmfTZPUDcokSadpksSMKIUAZrYM1qK1Q1KfmdkSWADDYBiMBW2K/HRgJCQiEkK4rqfIReHEQO0MQmOYWQHUPWVLtYQJAAVSkZ1/OtoR10n3dZwKOeTqBrUd/Y4D/SgNIgCTENt3bM/DjuOX69Mb/MAnq9Ea6fpxtxV1225QYkDlqDSKbBKWqTHuSAZiC0YzWRDWWkAmQgsWGNESIDIy26KlxsDFJCKSSJKE63mu51kSiTaZQQsYSKj4blxukMkyEEQoiXglFtzHkJBZZ2bWsQZyhHrsV+sBMCARgBCCGQRRrVYZq9eOLi/Wxic8z3eUSFrLYEyep0mnLZSrvJJlJmuTsN2LY0cnYzUgIiACQLBgGZEZrbVMAIyGES0wgDVsDQMzIRAxIgAhEjITIpH0SiWn1YtTFIxVh4TvY7VGrShKUiTpKrlSlL1Guke6cZw75uaiF7/4i6NbnvSkDX/2Z0/8yT7kh8F733v3Zz97YDh861uvft7ztv2bXPnYsd673/2vo1ve/e4n9VsG/VJiEFUdqVwCQGYrlVMK/LAXOY471mg0F+d1nrme53muicOs085yjRbjXq8xOQUMG3fsSFuLutvttVuUhBf4MygJpUAlQCASICIKgqINErNlKxBZILNgo60QlgyLYb8MQSiyTDueI5REzEoKA0ck5KLyS1VanGtaIEfJQRZbP7NnpcPYI8JHP3r/t799cnTL978//6Y3PXZiwv8JrvowOHiwM/qiCwvxv9WV5+fj973v7tEt7373k/6tLv6LiFWu6opJwKCECPyACMfqFQW2s7RgtSZCnYTh8kKSJChlFoeVWkMAKNdN28vR4nwWdsLl5e7ycpJoi5KEIClRCpQCFEHRp6ao9BYShERHkuOQckgqVAqlAuWAIEZwlFurNsJIO65T8qXnCCtV5pWTNKuNNwRJbY0gXLGDBr5Hv1fB+dvuzPCRj9yzZmOWmY9+9P5H8Mmu4+cQw4mNh7p9AHSlnJmaqgR+FveSMLTW6DSKmgtxt5vnBowVJCqVErP1JESzxzhPes0lq7MsCuNeYhkAhGW2xlpj2bC1bC2zHVAmiIAChEDhgFCoXHRckg6QQCQUYnpm2rBIMmsYE8ue76NSJssMkB941jIwENHAD4YRo/2R9Mv7l3858cADrVO379lzz3pR1L8P9Hn3QVR1xelL0wRJbN0w1W02CdBqDTqL88QanWcmqFQRbCnwkl4bCZOljgKdxWnc6eVsa47SeZ7EWZppUsTMaAt1SwhgCRRLYMtWEBIiGFNUwyIpQhcwzlBKJCEd5Xp+mhsLEDjSqVTZFUQYh8nYeK0Vpak2RYXVSG47rqT/cr+I7xzxV39172m379vX/MY3jj/1qZt+kg96HT8PWFW8N9JQqeBn2FXKqVck6ryzxGy1McwcVGuKrADOoy6wlWgdNHmc9Do9IORYJ0m6tNSqLXUcPzAcKCWFEEKQZUZBQmCe5URFsyZERG04idPFhaXFpeW4F9XKwXi5DIJISMd1dd+y8jIncBzXIYytcSol13GMtQhIsNKSYFieynAKZXNWtNvZpz+9/0x7P/zhvevi/u8AK31mRkmMwrKJojgNu55DNs+QAAX6ruc6jiAhCSQJIRwB1qZR2O5FYSSQelF078lFQLF/oXXR8bldF2zZvHnD2FgjCAI/cIUQpARJBAaT50kSt5qdoyfmjp6YO3ZyvtmOBFHJ98ueu2Nm4srLdiJJpZQi8gn8ajW0VFNuJfA4M6lhx5FI/aIrWF2H3W8MPMgnOBd84hMPxLEeDq++evrOO+eGw1tvfXBxMT4vh9UY7nSyWs35ZWZCzoJOJyuX1c/4w5HDWCoAMANR0ccdkChMknaUIEtJNk8yZJZCsO8HpRIRWJ3laZJ0e4KtMdpaXO50Wr2wKiiz9sRy+9hS818PHNpQq1x+wdadO7ZPTU1Uxmqu55KAPM2W5xd+/ON7Dxw5fnB2cTmKNoyN7dy08dJLdk2NNwRjFkVRN/YCx3EcpaTrCHKDXpI6uW54TqPkHO9oiyj6nm8fRGStRewnva9wlOeANU7qn/7ptX/4h9+6++6FYphl5pZb7v+DP3jM2S/S6WQf+MAPv/zlo4cPd48d62lthcDp6WDTpvKv/doFN9542cxMcM5fTR/HjvXe+MZ/MSOdwCcn/fe858meJ873Uo8An/3sgU99amXSe9WrLnnhC3esOeYf/uHgqDf/G79x4Utfuuu0V/v2t09++tP79+5d3rt3aW4uUoq2bKlceGHtda+74kUvumBU9D/ykXtuu+3IcPiEJ2z4vd979Gmv+d/+2x179y4Nh294w5VnmYdX8t0ZgBCYwVpLSNoYay2QaHUjAdpRwlEFi6K0tmmcpHGURHEWRTbLWZtc52GcRHHazU1sjK+UsbbXi5aNfgDYGKszvZmQGjUgirvdhdm5xYWFLE0V0li5Kh3v6LFZG0atsfrkWKNWrbrVkhTSc92K687UKl3H9WxGSMLzlHCg3R22Ll7TX6PoQgDn0yDy7rsX7rprfjjcuLH0jGdsfvWrLxmKOwDs2bP393//MWfh8ffs2fvmN39neTkZ3WgMnzgRnjgR3nnn3B//8ffe/e4n/c7v7D7HuwKAdjt73vM+P/p1VirO1772az8bWQeA++5rjsYEHv/4aYC14r5/f2v0mEc9avzU68Sxfutbv/ve9/7r6HeS5/ahh9oPPdS+7bYjF1/c+Mxnnjc8d9u26ug1v/a1Y294w5VK0ZrLdrvZ//gf309TUwylpA9+8GlneTvD80fLUxEJBZE1BgUagCg1i83eycXWYrN3YrHzowcOfvOue75yx95v/uv937/3wYWldpjmSW6WwuRwL1rWlktBi6CyYzPU6u0k7/aiEyfnl5rLWZaTJOUQsNV5HrhurKGt/AOJiaY33xcnD7S6//LjfUeOzmapVlJIJN/zN1ZrV4zNBKWgUS8jCOG4QknRjyNZAMABvfmI8wbWOKmveMXFQuD1118s5crn+8ADra9//fiZrvCP/3joP/2nr62R9TXQ2t500zc+8IEfnuNdZZl58Yu/MCrrnif+8R+f/7jHTZ3jFX5OcOJE+OhHf+I97/nXs+ifffuaz3723x040C6Gz3jG5q1bK8O9S0vJ//k/x0496wtfODSUdQC47rptk5NnMzhpQD/2wzSFFFljrLXGWsvgB6Wiu7tlaofx8ROzC0vNMI4JSRSdgZVEKSwQoxjzy4+5aOe11z7uyU//lWsef80rX/nyi7ZscMtBN0mTLJWBqk3WGxsaTq1cmZ7cuHPnhZdc/Csvvu7iqy+rTZWff/0NT/jVpwdKGbalWuD7rhTkuV7F8UpjdScI6o2aFKBzE1TLQghjrTFm0CcPYIWROT/Esf7f/3vf6JZXveoSAJia8tdENz/84b1nusKrXnX7cIgIF1/cePnLL3rLW6563vO2VSrO6MGnzgCnBTO85jVfHf2OpaRbb73uKU/5xfOY3/Smf1nD8AqBO3fW1nxXs7PRi170j0WrfiHw1a++ZHTvaYmE0RkAAG644dKz34nkIsdqZFGMfq4MAluba1Nv1NmYTquplDTWpkSSyFVKIbU6mSLpeX61XBJSVKuVJIwaNt/u+mPTmzZMT7hxr/HkJ2YK7v/hfY5yy5VyY2aCFIWxqUxPs4aNex9MqvWGkQRmx2R96aH7nnDlxRddecmW7TPC2qidOEJO1Oq2XGKTlcqeBYcZgEAI1FrneQ5s+61leOQd8OnSgs+Az33uwVYrHQ53754YTqmvfvWl//APB0ePXFiIT9UfP/jBwugVPvjBp7/+9VcMh2lqHv/4T//wh4vFMAzzj3/8gTe84cqz39Vb3/qd0YcQEf7mb5513XXbz/FN/fzgG984/vGPPzAcCoFvf/s1v/u7j/Z9OTcX3Xrrg7/zO18feib33LP8+c8ffNGLLgCA3/zNS9/+9juHJ/7t3z74oQ89zXFWrLgwzL/4xcPDYb3uPv/5289+MwPbfeQ5I6RhHw6trfICJ01LgS8F5XkeMXhKegKtsZmxJUe6nj82NjY2UUNjO8vtrNe1C/OOjjledjdsrF64I8nivBO5CL5yvFLAaGuVAByZZ7Y2XtGHD19msNNuxceOTda8zZddNr1zc6AgWexGkLqEF1712HuPHEEirxSQy5kGnRkACKNU6wGX8hOEgdZYMoVqL/D8529vNNxmsy/KWWZuueW+N77xsWuuMGriwykTjOuKW2551vvff/ejHz25e/fElVdONBru2W9pz569//2/f390y1/8xdNf9rKLzun9/JzhjW/81ujwT/7k8W9+81XF39PTwW/91qPuumv+5ptXvoKbb763EPcLLqg99ambhgZks5l+9avHRufbL37x8CiZ9rKXXei6D+PSDMR9wFAXS9kN2b04ihhRKOl7ru+qXq9X9p00RUS11OrlFpgkkRRS1CrlWuDjhkltrECslkvj0+O1Rk0KbM4mE2M1YQwZC8YggSuEdAQEDl62qzw9EXfDDXkihfBrpUrNdwVzFNost3nuAIrAP9RsmsBXnseZDbNUa5Nr0+71uIisDmNLp9anPpxpc+BA+2tfWzEYiPD661ekynXFy19+0Qc/+OPhlj177vmDP3jsGoHetas+Onz967/2gQ/88AUv2PGCF+x4/ONnhMDduyduvvmZD3MrA3zxi4c+85lVc/Sf/dkTX/e6K850/M8z0tSMcgClkvrt3147rb31rVcnid69e/LKK8evvHJiw4bScNeNN1466i996lP7R8X9fC0ZWKlmwoKZQWa21gopi0TyNI6TJPW9oNdtW62RLQD7rtK5NtZY5tSwZdKZNZkJGk6t7HslX3muH/iu4yhJkOZoWAKRtZhbyA2AAWPYoOO5jQnP8x1rAdgS54KM0JlNkjTJdZinaV5p1JNet5VmnuMIIm10nNky24VOGEZx0WRjhJgZqeYbhonPilG9AgDPetaW0Y8bAF796ktHxX3//tY///Oxpz991cqV11wzs4Yduuee5XvuWX7nO++amPCvu27bC16w49nP3lqtrjLiz4RR4g8AHEe88pUXn8uJP4d46KH2yrJZAJde2qjX185sO3ZUP/ax55z29Je8ZNcb3vD1Xi8vhn/3dw+m6dMLFR5F+gtfODQ8cteu2jXXzDzs/QyYBwYoSpkBmMEYywxCCLams7xsGMqVqhJSAtZLQcmV2prMGCQKs9wwxHEetnt5lCrGilI13624quRKF0DHWZ6meZKZ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' /></div><h3>Hello {user.FirstName}</h3><br/>" +
                $"<strong>Congratulations!</strong><p> You've just created an account with asklucy.io</p><br/>" +
                $"<p>But you're not finished yet.</p><br/>" +
                $"<p>To activate your account and confirm ownership of this email address mustafa.salaheldin@yahoo.com, please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a></p><br/>" +
                $"<p>Remember to confirm your email address today, so you can start using your new account.</p><br/>" +
                $"<p>Sincerely,</p><br/>" +
                $"<p>Ask Lucy Support Team</p><br/>" +
                $"<p>support@asklucy.io</p><br/>");

            StatusMessage = "Verification email sent. Please check your email.";
            return RedirectToPage();
        }
    }
}
