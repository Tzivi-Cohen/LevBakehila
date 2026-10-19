using System.ComponentModel.DataAnnotations;

namespace LevBakehila.Core.DTOs.Common;

public class PaginationParams
{
    public const int MaxPageSize = 50;

    [Range(1, int.MaxValue, ErrorMessage = "מספר העמוד חייב להיות 1 ומעלה.")]
    public int PageNumber { get; set; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "גודל העמוד חייב להיות בין 1 ל־50.")]
    public int PageSize { get; set; } = 10;

    [StringLength(30)]
    public string? SortBy { get; set; }

    [RegularExpression("^(asc|desc)$", ErrorMessage = "כיוון המיון חייב להיות asc או desc.")]
    public string SortDirection { get; set; } = "asc";

    public bool IsDescending => SortDirection == "desc";
}
