namespace ZenCloud.Services.Interfaces;

public interface IEmailVerificationService
{
    string GenerateVerificationToken(string email);
    (bool isValid, string email) ValidateVerificationToken(string token);
}