using System.ComponentModel.DataAnnotations;
using LevBakehila.Core.Enums;

namespace LevBakehila.Core.DTOs.Auth;

public class RegisterRequestDto
{
    [Required(ErrorMessage = "שם מלא הוא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "אימייל הוא שדה חובה.")]
    [EmailAddress(ErrorMessage = "כתובת האימייל אינה תקינה.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "סיסמה היא שדה חובה.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "הסיסמה חייבת להכיל לפחות 8 תווים.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "שכונה היא שדה חובה.")]
    [StringLength(100, MinimumLength = 2)]
    public string Neighborhood { get; set; } = string.Empty;
}

public class LoginRequestDto
{
    [Required(ErrorMessage = "אימייל הוא שדה חובה.")]
    [EmailAddress(ErrorMessage = "כתובת האימייל אינה תקינה.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "סיסמה היא שדה חובה.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}
