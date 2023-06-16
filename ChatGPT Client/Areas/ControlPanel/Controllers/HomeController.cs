using AskLucy.Areas.Identity.Models;
using AskLucy.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AskLucy.Areas.ControlPanel.Controllers
{
    [Route("[area]/[controller]/[action]")]
    [Area("ControlPanel")]

    public class HomeController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;

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

        public async Task<IActionResult> UsersManager()
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
