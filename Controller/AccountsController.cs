using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using EventManagementApi.DTO;
using EventManagementApi.Entity;
using EventManagementApi.Database;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.ApplicationInsights;

namespace EventManagementApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountsController : ControllerBase
    {
        #region Properties
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AccountsController> _logger;
        private readonly TelemetryClient _telemetryClient;
        #endregion

        #region Constructors
        public AccountsController(
            IConfiguration configuration,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            BlobServiceClient blobServiceClient,
            ILogger<AccountsController> logger,
            TelemetryClient telemetryClient)

        {
            _configuration = configuration;
            _context = context;
            _userManager = userManager;
            _logger = logger;
            _telemetryClient = telemetryClient;
        }
        #endregion

        #region Register a new user (Accessible by all, typically used for user self-registration)
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] AccountCreateDto model)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User registered successfully with email: {Email}", model.Email);
                await _userManager.AddToRoleAsync(user, model.Role);
                _telemetryClient.TrackEvent("UserRegistered", new Dictionary<string, string>
                {
                    { "Email", model.Email },
                    { "Role", model.Role },
                    { "FullName", model.FullName }
                });
                return Ok(new { Message = "User registered successfully" });
            }

            _logger.LogWarning("User registration failed for email: {Email}", model.Email);
            _telemetryClient.TrackException(new Exception($"User registration failed for email: {model.Email}"));
            return BadRequest(result.Errors);
        }
        #endregion

        #region Get Profile - General account management (Accessible by authenticated users)
        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                _logger.LogError("User not found");
                return NotFound();
            }
            // TODO: Fetch user roles
            var userRoles = await _userManager.GetRolesAsync(user);
            _logger.LogInformation("User profile retrieved successfully for email: {Email}", user.Email);
            _telemetryClient.TrackEvent("UserProfileRetrieved", new Dictionary<string, string>
            {
                { "Id", user.Id },
                { "Email", user.Email },
                { "FullName", user.FullName },
                { "Timestamp", DateTime.Now.ToString() }
            });

            return Ok(new { user.UserName, user.Email, user.FullName, user.Id, Roles = userRoles });
        }
        #endregion

        #region Login (Accessible by all, typically used for user self-login) - Return a server token
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
            {
                _logger.LogWarning("Invalid login attempt for email: {Email}", model.Email);
                return Unauthorized(new { Message = "Invalid login attempt" });
            }

            // TODO: Fetch user roles
            var userRoles = await _userManager.GetRolesAsync(user);

            // TODO: Add claims
            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
            };

            // TODO: Add role claims
            authClaims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

            // TODO: Add JWT token
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(authClaims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"]
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            _telemetryClient.TrackEvent("UserLoggedIn", new Dictionary<string, string>
            {
                { "Email", user.Email },
                { "Login time", DateTime.Now.ToString() },
            });

            _logger.LogInformation("User logged in successfully with email: {Email}", user.Email);

            return Ok(new { Token = tokenString });
        }
        #endregion

        #region Update account details (Accessible by authenticated users)
        [HttpPut("update")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] AccountUpdateDto model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogError("User not found");
                return NotFound();
            }
            user.Email = model.Email;
            user.FullName = model.FullName;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                _logger.LogInformation("User profile updated successfully with email: {Email}", user.Email);
                _telemetryClient.TrackEvent("UserProfileUpdated", new Dictionary<string, string>
                {
                    { "Id", user.Id },
                    { "Email", user.Email },
                    { "FullName", user.FullName }
                });
                return Ok(new { Message = "User profile updated successfully" });
            }

            _logger.LogWarning("User profile update failed for email: {Email}", user.Email);
            return BadRequest(result.Errors);
        }
        #endregion

        #region Get all users (Accessible by Admins)
        [HttpGet("users")]
        [Authorize(Policy = "Admin")]
        public IActionResult GetUsers()
        {
            var users = _context.Users.Select(u => new { u.Id, u.UserName, u.Email, u.FullName }).ToList();
            _logger.LogInformation("All users retrieved successfully");
            _telemetryClient.TrackEvent("GetAllUsers", new Dictionary<string, string> { { "Count", users.Count.ToString() } });
            return Ok(users);
        }
        #endregion

        #region Delete a user (Accessible by Admins)
        [HttpDelete("{id}")]
        [Authorize(Policy = "Admin")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());

            if (user == null)
            {
                _logger.LogError("User not found");
                return NotFound($"User with id {id} not found");
            }

            var result = await _userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID {Id} deleted successfully", id);
                _telemetryClient.TrackEvent("UserDeleted", new Dictionary<string, string> {
                    { "DeletedId", id.ToString() },
                    { "DeletedEmail", user.Email },
                    { "DeletedFullName", user.FullName }
                });
                return Ok(new { Message = $"User {user.FullName} with ID {id} deleted successfully" });
            }

            _logger.LogWarning("User deletion failed for user with ID {Id}", id);
            return BadRequest(result.Errors);
        }
        #endregion
    }
}