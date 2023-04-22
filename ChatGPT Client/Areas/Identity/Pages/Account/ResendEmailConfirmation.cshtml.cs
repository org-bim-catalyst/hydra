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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace AskLucy.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public ResendEmailConfirmationModel(UserManager<ApplicationUser> userManager, 
                                            IEmailSender emailSender,
                                            IWebHostEnvironment hostingEnvironment)
        {
            _userManager = userManager;
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

        public void OnGet(string Email)
        {

            Input = new InputModel
            {
                Email = Email
            };
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "This email is not registered in our website. Please check your email.");
                return Page();
            }

            if (user.EmailConfirmed)
            {
                ModelState.AddModelError(string.Empty, "This email is already registered and confirmed. Please check your email.");
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { userId = userId, code = code },
                protocol: Request.Scheme);

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

            ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email.");

            return Page();
        }

        //public async Task<IActionResult> OnPostAsync()
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        return Page();
        //    }

        //    List<ApplicationUser> users = _userManager.Users.ToList();

        //    foreach (ApplicationUser user in users)
        //    {
        //        if (user == null && user.Email != null)
        //        {
        //            ModelState.AddModelError(string.Empty, "This email is not registered in our website. Please check your email.");
        //            return Page();
        //        }

        //        if (user.EmailConfirmed)
        //        {
        //            ModelState.AddModelError(string.Empty, "This email is already registered and confirmed. Please go to the login page.");
        //            return Page();
        //        }

        //        //user.EmailConfirmed = false;
        //        //await _userManager.UpdateAsync(user);

        //        var userId = await _userManager.GetUserIdAsync(user);
        //        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        //        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        //        var callbackUrl = Url.Page(
        //            "/Account/ConfirmEmail",
        //            pageHandler: null,
        //            values: new { userId = userId, code = code },
        //            protocol: Request.Scheme);

        //        HtmlDocument htmDoc = new HtmlDocument();

        //        string htmlFilePath = Path.Combine(_hostingEnvironment.WebRootPath, "templates", "email", "index.html");

        //        string html = System.IO.File.ReadAllText(htmlFilePath);
        //        htmDoc.LoadHtml(html);
        //        htmDoc.GetElementbyId("span-user-name").InnerHtml = user.FirstName ?? string.Empty;
        //        htmDoc.GetElementbyId("span-email-account").InnerHtml = user.Email;
        //        htmDoc.GetElementbyId("link-verify-email").SetAttributeValue("href", HtmlEncoder.Default.Encode(callbackUrl));
        //        htmDoc.Save(htmlFilePath);
        //        htmDoc.Load(htmlFilePath);

        //        await _emailSender.SendEmailAsync(user.Email, "Confirm your email", htmDoc.Text);

        //        ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email.");
        //    }

        //    return Page();
        //}

    }
}
