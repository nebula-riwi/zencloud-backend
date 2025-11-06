using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ZenCloud.Data.Entities;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class JwtService : IJwtService
{
    private readonly string _jwtKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly double _expireHours;

    public JwtService()
    {
        _jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? 
                  throw new ArgumentNullException("JWT_KEY no está configurada");
        _issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "zencloud-api";
        _audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "zencloud-users";
        
        if (!double.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRE_HOURS") ?? "24", out _expireHours))
        {
            _expireHours = 24;
        }
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.FullName ?? string.Empty),
            new Claim("userId", user.UserId.ToString()),
            new Claim("email", user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expireHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}