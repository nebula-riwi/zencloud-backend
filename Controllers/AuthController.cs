using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using ZenCloud.Data.Repositories.Interfaces;
using System.Threading.Tasks;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool result = await _authService.RegisterAsync(model);

        if (result)
        {
            return Ok("Registration successful. Please check your email to verify your account.");
        }
        else
        {
            return BadRequest("Registration failed.");
        }
    }

    [HttpGet("verify")]
    public async Task<IActionResult> VerifyEmail(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return BadRequest("Invalid verification request.");
        }

        bool result = await _authService.VerifyEmailAsync(email, token);

        if (result)
        {
            return Ok("Email verification successful. You can now log in.");
        }
        else
        {
            return BadRequest("Email verification failed. Invalid email or token.");
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string? token = await _authService.LoginAsync(model);

        if (token == null)
        {
            return Unauthorized("Invalid credentials.");
        }

        return Ok(new { Token = token });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool result = await _authService.RequestPasswordResetAsync(model.Email);

        return Ok(new { message = "If the email exists, password reset instructions will be sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool result = await _authService.ResetPasswordAsync(model.Email, model.Token, model.NewPassword);

        if (result)
        {
            return Ok(new { message = "Password reset successfully." });
        }
        else
        {
            return BadRequest(new { message = "Failed to reset password. Invalid or expired token." });
        }
    }
}