using System.ComponentModel.DataAnnotations;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.Enums;

namespace LevBakehila.Core.DTOs.Users;

public class UserResponseDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string Neighborhood { get; set; } = string.Empty;
    public List<SkillResponseDto> Skills { get; set; } = new();
}

// No Role or Email fields: a user can never promote herself to Admin through the profile
public class UpdateProfileDto
{
    [Required(ErrorMessage = "שם מלא הוא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "שכונה היא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Neighborhood { get; set; } = string.Empty;

    public List<int> SkillIds { get; set; } = new();
}
