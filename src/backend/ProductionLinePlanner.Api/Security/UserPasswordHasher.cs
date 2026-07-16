using Microsoft.AspNetCore.Identity;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Api.Security;

public sealed class UserPasswordHasher(IPasswordHasher<AppUser> passwordHasher) : IUserPasswordHasher
{
    public string HashPassword(AppUser user, string password) => passwordHasher.HashPassword(user, password);
}
