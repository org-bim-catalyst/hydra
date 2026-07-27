using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AskLucy.Data;
using AskLucy.Models;
using AskLucy.Areas.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AskLucy.Controllers.api
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserChatsController : ControllerBase
    {
        private readonly ChatGPT_ClientContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserChatsController(ChatGPT_ClientContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: api/UserChats
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserChat>>> GetUserChat()
        {
          if (_context.UserChats == null)
          {
              return NotFound();
          }


            return await _context.UserChats.ToListAsync();
        }

        // GET: api/UserChats/5
        [HttpGet("{id}")]
        public async Task<ActionResult<UserChat>> GetUserChat(int id)
        {
          if (_context.UserChats == null)
          {
              return NotFound();
          }
            var userChat = await _context.UserChats.FindAsync(id);

            if (userChat == null)
            {
                return NotFound();
            }

            return userChat;
        }

        // PUT: api/UserChats/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutUserChat(int id, UserChat userChat)
        {
            if (id != userChat.Id)
            {
                return BadRequest();
            }

            _context.Entry(userChat).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserChatExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/UserChats
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<UserChat>> PostUserChat([FromBody] UserChat userChat)
        {
            if (_context.UserChats == null)
            {
                return Problem("Entity set 'ChatGPT_ClientContext.UserChat'  is null.");
            }

            userChat.CreationDateTime = DateTime.Now;
            userChat.LastAccessDateTime = DateTime.Now;
            userChat.SessionId = HttpContext.Session.Id;

            ApplicationUser? user = await _userManager.GetUserAsync(User);
            user!.UserChats.Add(userChat);

            _context.Users.Update(user);

            await _context.SaveChangesAsync();

            return CreatedAtAction("GetUserChat", new { id = userChat.Id }, userChat);
        }

        // DELETE: api/UserChats/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUserChat(int id)
        {
            if (_context.UserChats == null)
            {
                return NotFound();
            }
            var userChat = await _context.UserChats.FindAsync(id);
            if (userChat == null)
            {
                return NotFound();
            }

            _context.UserChats.Remove(userChat);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool UserChatExists(int id)
        {
            return (_context.UserChats?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}
