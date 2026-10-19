using System.ComponentModel.DataAnnotations;
using LevBakehila.Core.Enums;

namespace LevBakehila.Core.DTOs.Registrations;

public class CreateRegistrationDto
{
    [Range(1, int.MaxValue, ErrorMessage = "יש לבחור פעילות.")]
    public int ActivityId { get; set; }
}

public class RegistrationResponseDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public int ActivityId { get; set; }
    public string ActivityTitle { get; set; } = string.Empty;
    public DateTime ActivityStartTime { get; set; }
    public DateTime ActivityEndTime { get; set; }
    public DateTime RegisteredAt { get; set; }
    public RegistrationStatus Status { get; set; }
}
