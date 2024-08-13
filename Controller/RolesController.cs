using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using EventManagementApi.DTO;
using EventManagementApi.Entity;

namespace EventManagementApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "Admin")]
    public class RolesController : ControllerBase
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public RolesController(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
        }

        #region Create a new role (Admin)
        [HttpPost("create")]
        [Authorize(Policy = "Admin")]
        public async Task<IActionResult> CreateRole([FromBody] RoleCreateDto model)
        {
            var roleExists = await _roleManager.RoleExistsAsync(model.RoleName);
            if (!roleExists)
            {
                var result = await _roleManager.CreateAsync(new IdentityRole(model.RoleName));
                if (result.Succeeded)
                {
                    return Ok(new { Message = "Role created successfully" });
                }
                return BadRequest(result.Errors);
            }
            return BadRequest(new { Message = "Role already exists" });
        }
        #endregion

        #region Assign a role to a user (Admin)
        [HttpPost("assign")]
        [Authorize(Policy = "Admin")]
        public async Task<IActionResult> AssignRole([FromBody] UserRoleUpdateDto model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return NotFound(new { Message = "User not found" });
            }

            var result = await _userManager.AddToRoleAsync(user, model.Role);
            if (result.Succeeded)
            {
                return Ok(new { Message = "Role assigned successfully" });
            }

            return BadRequest(result.Errors);
        }
        #endregion

        #region Remove a role from a user (Admin)
        [HttpPost("remove")]
        [Authorize(Policy = "Admin")]
        public async Task<IActionResult> RemoveRole([FromBody] UserRoleUpdateDto model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return NotFound(new { Message = "User not found" });
            }

            var result = await _userManager.RemoveFromRoleAsync(user, model.Role);
            if (result.Succeeded)
            {
                return Ok(new { Message = "Role removed successfully" });
            }

            return BadRequest(result.Errors);
        }
        #endregion
    }
}
