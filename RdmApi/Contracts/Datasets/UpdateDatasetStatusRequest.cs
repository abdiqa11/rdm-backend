using System.ComponentModel.DataAnnotations;
using RdmApi.Data.Entities;

namespace RdmApi.Contracts.Datasets;

public class UpdateDatasetStatusRequest
{
    [Required]
    public DatasetStatus Status { get; set; }
}