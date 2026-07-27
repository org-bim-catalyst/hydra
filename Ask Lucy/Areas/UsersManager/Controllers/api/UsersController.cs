using AskLucy.Areas.Identity.Models;
using AskLucy.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AskLucy.Areas.UsersManager.Controllers.api
{
    [Route("[area]/api/[controller]")]
    [ApiController]
    [Area("UsersManager")]
    public class UsersController : ControllerBase
    {
        private readonly ILogger<UsersController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(ILogger<UsersController> logger, UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _userManager = userManager;
        }

        // GET: api/<UsersController>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                List<ApplicationUser> users = await _userManager.Users.ToListAsync();
                return StatusCode((int)HttpStatusCode.OK, users);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }

        }

        // GET api/<UsersController>/5
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                ApplicationUser? user = await _userManager.FindByIdAsync(id);

                if(user == null)
                {
                    return NotFound(); 
                }
                else
                {
                    return StatusCode((int)HttpStatusCode.OK, user);
                }
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // POST api/<UsersController>
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ApplicationUser user)
        {
            try
            {
                ApplicationUser? _user = await _userManager.FindByEmailAsync(user.Email!);

                if (_user == null)
                {
                    IdentityResult response = _userManager.CreateAsync(user).Result;

                    if (response.Succeeded)
                    {
                        return StatusCode((int)HttpStatusCode.OK, user.Id);
                    }
                    else
                    {
                        return StatusCode((int)HttpStatusCode.InternalServerError, response.Errors);
                    }
                }
                else
                {
                    return StatusCode((int)HttpStatusCode.Conflict, "User already exists with this specified email.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // PUT api/<UsersController>/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, [FromBody] ApplicationUser user)
        {
            try
            {
                ApplicationUser? _user = await _userManager.FindByEmailAsync(user.Email!);

                if (_user == null)
                {
                    return StatusCode((int)HttpStatusCode.NotFound, "A user with this specified email is not found.");
                }
                else
                {
                    IdentityResult response = _userManager.UpdateAsync(user).Result;

                    if (response.Succeeded)
                    {
                        return StatusCode((int)HttpStatusCode.OK, user.Id);
                    }
                    else
                    {
                        return StatusCode((int)HttpStatusCode.InternalServerError, response.Errors);
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // DELETE api/<UsersController>/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            ApplicationUser? user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return StatusCode((int)HttpStatusCode.NotFound, "A user with this specified id is not found.");
            }
            else
            {
                IdentityResult response = _userManager.DeleteAsync(user).Result;

                if (response.Succeeded)
                {
                    return StatusCode((int)HttpStatusCode.NoContent, "User removed.");
                }
                else
                {
                    return StatusCode((int)HttpStatusCode.InternalServerError, response.Errors);
                }
            }
        }
    }
}
