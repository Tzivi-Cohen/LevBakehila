using System.ComponentModel.DataAnnotations;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.Enums;

namespace LevBakehila.Core.DTOs.Activities;

public class CreateActivityDto
{
    [Required(ErrorMessage = "כותרת היא שדה חובה.")]
    [StringLength(150, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(ActivityCategory), ErrorMessage = "סוג הפעילות אינו תקין.")]
    public ActivityCategory Category { get; set; }

    [Required(ErrorMessage = "שכונה היא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Neighborhood { get; set; } = string.Empty;

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Range(1, 100, ErrorMessage = "מספר המתנדבות חייב להיות בין 1 ל־100.")]
    public int MaxVolunteers { get; set; }

    public List<int> RequiredSkillIds { get; set; } = new();
}

public class UpdateActivityDto
{
    [Required(ErrorMessage = "כותרת היא שדה חובה.")]
    [StringLength(150, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(ActivityCategory), ErrorMessage = "סוג הפעילות אינו תקין.")]
    public ActivityCategory Category { get; set; }

    [Required(ErrorMessage = "שכונה היא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Neighborhood { get; set; } = string.Empty;

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Range(1, 100, ErrorMessage = "מספר המתנדבות חייב להיות בין 1 ל־100.")]
    public int MaxVolunteers { get; set; }

    public List<int> RequiredSkillIds { get; set; } = new();
}

public class ActivityResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ActivityCategory Category { get; set; }
    public string Neighborhood { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int MaxVolunteers { get; set; }
    public int AvailableSpots { get; set; }
    public bool IsActive { get; set; }
    public List<SkillResponseDto> RequiredSkills { get; set; } = new();
}

// SortBy: startTime (default) | title | neighborhood | availableSpots
public class ActivityFilterDto : PaginationParams
{
    [StringLength(100)]
    public string? Neighborhood { get; set; }

    [EnumDataType(typeof(ActivityCategory), ErrorMessage = "סוג הפעילות אינו תקין.")]
    public ActivityCategory? Category { get; set; }

    // true = only active, future activities that still have free spots
    public bool OnlyAvailable { get; set; }

    // Rule 5 ("close to home"): activities in the logged-in volunteer's own neighborhood come first,
    // then the usual sort. No geographic distance is calculated (PLAN 6, Rule 5).
    public bool MyNeighborhoodFirst { get; set; }
}
