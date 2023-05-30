using AskLucy.Areas.Identity.Models;
using AskLucy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AskLucy.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(ILogger<HomeController> logger, UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _userManager= userManager;
        }

        public async Task<IActionResult> Index()
        {
            string? userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return View();
            }
            else
            {
                ApplicationUser? user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return Error();
                }
                else
                {
                    return View(user);
                }
            }
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        public async Task<IActionResult> ControlPanel()
        {
            ApplicationUser? user = await _userManager.GetUserAsync(User);
            return View(user);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
