using System.Collections;
using AutoMapper;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Service.Mapping;

namespace LevBakehila.Tests.Unit;

// The AutoMapper MappingProfile: the configuration is valid, every response DTO property is filled,
// and request DTOs never touch fields controlled by the Service layer.
public class MappingProfileTests
{
    private static readonly IMapper Mapper = TestHelpers.Mapper;

    private static readonly Skill Patience = new() { Id = 7, Name = "Patience" };

    private static VolunteerActivity FullActivity()
    {
        var activity = TestHelpers.FutureActivity(id: 3, maxVolunteers: 4, availableSpots: 2);
        activity.RequiredSkills.Add(Patience);
        return activity;
    }

    private static User FullUser()
    {
        var user = TestHelpers.Volunteer(5);
        user.Skills.Add(Patience);
        return user;
    }

    // Every public property of the DTO has a non-default value (non-empty for collections)
    private static void AssertAllPropertiesFilled(object dto)
    {
        foreach (var property in dto.GetType().GetProperties())
        {
            var value = property.GetValue(dto);
            Assert.False(value is null, $"{property.Name} is null");

            if (value is IEnumerable collection and not string)
            {
                Assert.True(collection.Cast<object>().Any(), $"{property.Name} is empty");
            }
            else if (property.PropertyType.IsValueType && property.PropertyType != typeof(bool) && !property.PropertyType.IsEnum)
            {
                Assert.NotEqual(Activator.CreateInstance(property.PropertyType), value);
            }
            else if (value is string text)
            {
                Assert.False(string.IsNullOrEmpty(text), $"{property.Name} is empty");
            }
        }
    }

    [Fact]
    public void Configuration_IsValid_EveryDestinationMemberIsMappedOrIgnored()
    {
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).AssertConfigurationIsValid();
    }

    [Fact]
    public void ResponseDtos_AllPropertiesAreMapped()
    {
        var activity = FullActivity();
        var user = FullUser();
        var registration = new Registration
        {
            Id = 9, UserId = user.Id, User = user, ActivityId = activity.Id, Activity = activity,
            RegisteredAt = DateTime.UtcNow, Status = RegistrationStatus.Active
        };

        AssertAllPropertiesFilled(Mapper.Map<SkillResponseDto>(Patience));
        AssertAllPropertiesFilled(Mapper.Map<UserResponseDto>(user));
        AssertAllPropertiesFilled(Mapper.Map<ActivityResponseDto>(activity));
        AssertAllPropertiesFilled(Mapper.Map<RegistrationResponseDto>(registration));
    }

    [Fact]
    public void ActivityToDto_CopiesValues()
    {
        var activity = FullActivity();

        var dto = Mapper.Map<ActivityResponseDto>(activity);

        Assert.Equal(activity.Id, dto.Id);
        Assert.Equal(activity.Title, dto.Title);
        Assert.Equal(activity.StartTime, dto.StartTime);
        Assert.Equal(activity.EndTime, dto.EndTime);
        Assert.Equal(activity.MaxVolunteers, dto.MaxVolunteers);
        Assert.Equal(activity.AvailableSpots, dto.AvailableSpots);
        Assert.Equal(activity.IsActive, dto.IsActive);
        Assert.Equal("Patience", Assert.Single(dto.RequiredSkills).Name);
    }

    [Fact]
    public void RegistrationToDto_FlattensUserAndActivity()
    {
        var activity = FullActivity();
        var user = FullUser();
        var registration = new Registration
        {
            Id = 1, UserId = user.Id, User = user, ActivityId = activity.Id, Activity = activity,
            RegisteredAt = DateTime.UtcNow, Status = RegistrationStatus.Cancelled
        };

        var dto = Mapper.Map<RegistrationResponseDto>(registration);

        Assert.Equal(user.FullName, dto.UserFullName);
        Assert.Equal(activity.Title, dto.ActivityTitle);
        Assert.Equal(activity.StartTime, dto.ActivityStartTime);
        Assert.Equal(activity.EndTime, dto.ActivityEndTime);
        Assert.Equal(RegistrationStatus.Cancelled, dto.Status);
    }

    [Fact]
    public void UpdateActivityDto_MapIntoExisting_DoesNotTouchServiceControlledFields()
    {
        var activity = FullActivity();
        const uint rowVersion = 123;
        activity.RowVersion = rowVersion;
        var dto = new UpdateActivityDto
        {
            Title = "New title", Description = "New", Category = ActivityCategory.SocialActivity, Neighborhood = "Gilo",
            StartTime = activity.StartTime.AddDays(1), EndTime = activity.EndTime.AddDays(1), MaxVolunteers = 10,
            RequiredSkillIds = new List<int> { 99 }
        };

        Mapper.Map(dto, activity);

        Assert.Equal("New title", activity.Title);
        Assert.Equal(10, activity.MaxVolunteers);
        Assert.Equal(3, activity.Id);
        Assert.Equal(2, activity.AvailableSpots);
        Assert.True(activity.IsActive);
        Assert.Equal(rowVersion, activity.RowVersion);
        Assert.Equal(Patience.Id, Assert.Single(activity.RequiredSkills).Id);
    }

    [Fact]
    public void CreateSkillDto_ToNewEntity_AndUpdateSkillDto_IntoExisting()
    {
        var skill = Mapper.Map<Skill>(new CreateSkillDto { Name = "Music" });
        Assert.Equal(0, skill.Id);
        Assert.Equal("Music", skill.Name);

        skill.Id = 4;
        Mapper.Map(new UpdateSkillDto { Name = "Art" }, skill);
        Assert.Equal("Art", skill.Name);
        Assert.Equal(4, skill.Id);
    }

    [Fact]
    public void CreateActivityDto_ToNewEntity_LeavesServiceControlledFieldsAtDefaults()
    {
        var start = DateTime.UtcNow.Date.AddDays(3);
        var activity = Mapper.Map<VolunteerActivity>(new CreateActivityDto
        {
            Title = "Homework", Description = "Math", Category = ActivityCategory.HomeworkHelp, Neighborhood = "Ramot",
            StartTime = start, EndTime = start.AddHours(2), MaxVolunteers = 3, RequiredSkillIds = new List<int> { 1 }
        });

        Assert.Equal("Homework", activity.Title);
        Assert.Equal(3, activity.MaxVolunteers);
        Assert.Equal(start, activity.StartTime);
        Assert.Equal(0, activity.Id);
        Assert.Equal(0, activity.AvailableSpots);   // set by ActivityService.CreateAsync
        Assert.Empty(activity.RequiredSkills);       // set by ActivityService from RequiredSkillIds
        Assert.Empty(activity.Registrations);
    }

    [Fact]
    public void RegistrationToDto_WithoutLoadedNavigations_DoesNotThrow()
    {
        var dto = Mapper.Map<RegistrationResponseDto>(new Registration { Id = 2, UserId = 5, ActivityId = 6 });

        Assert.Equal(string.Empty, dto.UserFullName);
        Assert.Equal(string.Empty, dto.ActivityTitle);
        Assert.Equal(6, dto.ActivityId);
    }
}
