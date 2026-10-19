using AutoMapper;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Entities;

namespace LevBakehila.Service.Mapping;

// All Entity <-> DTO mappings in one AutoMapper Profile (registered in Program.cs with AddAutoMapper).
// Request DTOs never set Id, Role, AvailableSpots, IsActive, concurrency tokens or navigation properties -
// those fields are controlled only by the Service layer, so they are ignored here explicitly.
// Security note (README, "AutoMapper"): AutoMapper 14.0.0 has a known DoS advisory for deeply nested object graphs.
// Every type mapped here is flat or one level deep (Skills), and request DTOs contain no nested objects.
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // ---------- Skill ----------
        CreateMap<Skill, SkillResponseDto>();

        CreateMap<CreateSkillDto, Skill>()
            .ForMember(s => s.Id, o => o.Ignore())
            .ForMember(s => s.Users, o => o.Ignore())
            .ForMember(s => s.Activities, o => o.Ignore());

        CreateMap<UpdateSkillDto, Skill>()
            .ForMember(s => s.Id, o => o.Ignore())
            .ForMember(s => s.Users, o => o.Ignore())
            .ForMember(s => s.Activities, o => o.Ignore());

        // ---------- User ----------
        CreateMap<User, UserResponseDto>();

        // ---------- VolunteerActivity ----------
        CreateMap<VolunteerActivity, ActivityResponseDto>();

        CreateMap<CreateActivityDto, VolunteerActivity>()
            .ForMember(a => a.Id, o => o.Ignore())
            .ForMember(a => a.AvailableSpots, o => o.Ignore())
            .ForMember(a => a.IsActive, o => o.Ignore())
            .ForMember(a => a.RowVersion, o => o.Ignore())
            .ForMember(a => a.Registrations, o => o.Ignore())
            .ForMember(a => a.RequiredSkills, o => o.Ignore());

        CreateMap<UpdateActivityDto, VolunteerActivity>()
            .ForMember(a => a.Id, o => o.Ignore())
            .ForMember(a => a.AvailableSpots, o => o.Ignore())
            .ForMember(a => a.IsActive, o => o.Ignore())
            .ForMember(a => a.RowVersion, o => o.Ignore())
            .ForMember(a => a.Registrations, o => o.Ignore())
            .ForMember(a => a.RequiredSkills, o => o.Ignore());

        // ---------- Registration ----------
        // Flattened from User / Activity (loaded with Include by the repositories); null-safe in case they are not
        CreateMap<Registration, RegistrationResponseDto>()
            .ForMember(d => d.UserFullName, o => o.MapFrom(r => r.User != null ? r.User.FullName : string.Empty))
            .ForMember(d => d.ActivityTitle, o => o.MapFrom(r => r.Activity != null ? r.Activity.Title : string.Empty))
            .ForMember(d => d.ActivityStartTime, o => o.MapFrom(r => r.Activity != null ? r.Activity.StartTime : default))
            .ForMember(d => d.ActivityEndTime, o => o.MapFrom(r => r.Activity != null ? r.Activity.EndTime : default));
    }
}
