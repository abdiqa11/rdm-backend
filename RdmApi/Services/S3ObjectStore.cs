using Minio;
using Minio.DataModel.Args;

namespace RdmApi.Services;

public class S3ObjectStore
{
    private readonly IMinioClient _minio;
    private readonly string _bucket;

    public S3ObjectStore(IConfiguration config)
    {
        var section = config.GetSection("S3");
        var endpoint = section["Endpoint"]!;
        var accessKey = section["AccessKey"]!;
        var secretKey = section["SecretKey"]!;
        _bucket = section["Bucket"]!;

        _minio = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(false)
            .Build();
    }

    public async Task PutAsync(string objectKey, Stream data, string contentType, long size, CancellationToken ct)
    {
        var putArgs = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithStreamData(data)
            .WithObjectSize(size)
            .WithContentType(contentType);

        await _minio.PutObjectAsync(putArgs, ct);
    }
    public async Task<(Stream Stream, string ContentType)> GetAsync(string objectKey, CancellationToken ct)
    {
        // Get metadata (content-type etc.)
        var stat = await _minio.StatObjectAsync(
            new StatObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey),
            ct);

        var ms = new MemoryStream();

        await _minio.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithCallbackStream(s => s.CopyTo(ms)),
            ct);

        ms.Position = 0;

        var contentType = string.IsNullOrWhiteSpace(stat.ContentType)
            ? "application/octet-stream"
            : stat.ContentType;

        return (ms, contentType);
    }

}