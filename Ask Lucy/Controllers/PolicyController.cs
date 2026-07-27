using Microsoft.AspNetCore.Mvc;

namespace AskLucy.Controllers
{
    public class PolicyController : Controller
    {
        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Cookies()
        {
            return View();
        }
    }
}
