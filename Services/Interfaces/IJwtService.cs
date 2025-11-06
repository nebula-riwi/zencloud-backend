using ZenCloud.Data.Entities;

namespace ZenCloud.Services.Interfaces;

public interface IJwtService
{
    string GenerateToken(User user);
}