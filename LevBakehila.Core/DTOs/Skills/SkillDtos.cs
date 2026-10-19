using System.ComponentModel.DataAnnotations;

namespace LevBakehila.Core.DTOs.Skills;

public class CreateSkillDto
{
    [Required(ErrorMessage = "שם המיומנות הוא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}

public class UpdateSkillDto
{
    [Required(ErrorMessage = "שם המיומנות הוא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}

public class SkillResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
