using Microsoft.AspNetCore.Identity;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (versioned format, upgrades transparently).</summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _h = new();
    public string Hash(string password) => _h.HashPassword(new object(), password);
    public bool Verify(string? hash, string password) =>
        !string.IsNullOrEmpty(hash) && _h.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
}
