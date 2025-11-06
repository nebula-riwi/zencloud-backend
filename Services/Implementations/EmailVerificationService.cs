using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ZenCloud.Services.Implementations;

public class EmailVerificationService : IEmailVerificationService
{
    private readonly byte[] _jwtKeyBytes;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly double _expireHours;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(ILogger<EmailVerificationService> logger)
    {
        var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ??
                     throw new ArgumentNullException("JWT_KEY no está configurada");

        // Log para verificar la clave cargada
        logger.LogInformation($"JWT_KEY cargada: {jwtKey}");
        logger.LogInformation($"Longitud de JWT_KEY: {jwtKey.Length} caracteres");

        try
        {
            _jwtKeyBytes = Convert.FromBase64String(jwtKey);
            logger.LogInformation($"JWT_KEY bytes length: {_jwtKeyBytes.Length} bytes");

            if (_jwtKeyBytes.Length < 32)
            {
                logger.LogWarning($"JWT_KEY tiene solo {_jwtKeyBytes.Length} bytes, debería tener al menos 32 bytes");
            }
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "JWT_KEY no es un Base64 válido");
            throw;
        }

        _issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "zencloud-api";
        _audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "zencloud-users";
        _logger = logger;

        _logger.LogInformation($"JWT Issuer: {_issuer}, Audience: {_audience}");

        if (!double.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRE_HOURS") ?? "24", out _expireHours))
        {
            _expireHours = 24;
        }

        _logger.LogInformation($"JWT Expire Hours: {_expireHours}");
    }

    public string GenerateVerificationToken(string email)
    {
        _logger.LogInformation($"Generando token de verificación para: {email}");

        var key = new SymmetricSecurityKey(_jwtKeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("purpose", "email_verification")
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expireHours),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation($"Token generado exitosamente: {tokenString}");
        return tokenString;
    }

    public (bool isValid, string email) ValidateVerificationToken(string token)
    {
        _logger.LogInformation("=== INICIANDO VALIDACIÓN DE TOKEN ===");
        _logger.LogInformation($"Token a validar: {token}");

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Token está vacío o nulo");
            return (false, null);
        }

        try
        {
            var key = new SymmetricSecurityKey(_jwtKeyBytes);

            var tokenHandler = new JwtSecurityTokenHandler();

            // Verificar si el token es un JWT válido antes de validar
            if (!tokenHandler.CanReadToken(token))
            {
                _logger.LogWarning("El token no es un JWT válido (no se puede leer)");
                return (false, null);
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            };

            _logger.LogInformation("Parámetros de validación configurados, validando token...");

            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;

            _logger.LogInformation("✅ Token JWT validado exitosamente");
            _logger.LogInformation($"📋 Todos los claims del token:");

            // Log detallado de claims con delimitadores
            foreach (var claim in jwtToken.Claims)
            {
                _logger.LogInformation($"   - [{claim.Type}] = [{claim.Value}]");
            }

            var purpose = principal.FindFirst("purpose")?.Value;
            _logger.LogInformation($"🔍 Propósito del token: {purpose}");

            if (purpose != "email_verification")
            {
                _logger.LogWarning("❌ Token sin propósito correcto");
                return (false, null);
            }

            // BUSCAR EMAIL: Primero en el jwtToken por el tipo "email"
            var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            // Si no se encuentra, intentar en el principal
            if (string.IsNullOrEmpty(email))
            {
                email = principal.FindFirst("email")?.Value;
            }

            // Si aún no, intentar con el nombre registrado
            if (string.IsNullOrEmpty(email))
            {
                email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
            }

            _logger.LogInformation($"📧 Email extraído del token: {email}");

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("❌ Token sin claim de email");
                return (false, null);
            }

            _logger.LogInformation($"✅ Validación exitosa para email: {email}");
            return (true, email);
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogError($"❌ Token expirado: {ex.Message}");
            return (false, null);
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogError($"❌ Firma inválida: {ex.Message}");
            _logger.LogError($"   Esto usualmente significa que la JWT_KEY no coincide");
            return (false, null);
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            _logger.LogError($"❌ Emisor inválido: {ex.Message}");
            return (false, null);
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            _logger.LogError($"❌ Audiencia inválida: {ex.Message}");
            return (false, null);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError($"❌ Argumento inválido: {ex.Message}");
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error inesperado validando token: {ex.Message}");
            _logger.LogError($"   StackTrace: {ex.StackTrace}");
            return (false, null);
        }
    }
}