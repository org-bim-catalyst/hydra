using AskLucy.Application.Users;
using AskLucy.Persistence.Identity;
using AutoMapper;

namespace AskLucy.Persistence.Mapping;

/// <summary>
/// Maps <see cref="ApplicationUser"/> to <see cref="UserAdminDto"/> — explicitly field-by-field,
/// never <c>CreateMap&lt;ApplicationUser, UserAdminDto&gt;().ReverseMap()</c>, so a new
/// Identity property never accidentally becomes admin-visible or writable by default.
/// </summary>
public sealed class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<ApplicationUser, UserAdminDto>()
            .ForCtorParam(nameof(UserAdminDto.Id), opt => opt.MapFrom(u => u.Id))
            .ForCtorParam(nameof(UserAdminDto.Email), opt => opt.MapFrom(u => u.Email ?? string.Empty))
            .ForCtorParam(nameof(UserAdminDto.FirstName), opt => opt.MapFrom(u => u.FirstName))
            .ForCtorParam(nameof(UserAdminDto.LastName), opt => opt.MapFrom(u => u.LastName))
            .ForCtorParam(nameof(UserAdminDto.EmailConfirmed), opt => opt.MapFrom(u => u.EmailConfirmed))
            .ForCtorParam(nameof(UserAdminDto.TwoFactorEnabled), opt => opt.MapFrom(u => u.TwoFactorEnabled))
            .ForCtorParam(nameof(UserAdminDto.LockoutEnabled), opt => opt.MapFrom(u => u.LockoutEnabled));
    }
}
