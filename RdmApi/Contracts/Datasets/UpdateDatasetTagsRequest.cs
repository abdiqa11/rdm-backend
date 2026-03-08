using System.ComponentModel.DataAnnotations;

namespace RdmApi.Contracts.Datasets;

public class UpdateDatasetTagsRequest
{
    [Required]
    public string[] Tags { get; set; } = Array.Empty<string>();
}