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

        var endpoint = section["Endpoint"];
        var accessKey = section["AccessKey"];
        var secretKey = section["SecretKey"];
        var bucket = section["Bucket"];
        var useSslRaw = section["UseSsl"];

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("S3:Endpoint is missing.");

        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException("S3:AccessKey is missing.");

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("S3:SecretKey is missing.");

        if (string.IsNullOrWhiteSpace(bucket))
            throw new InvalidOperationException("S3:Bucket is missing.");

        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("S3:Endpoint must not include http:// or https://");
        }

        var useSsl = false;
        if (!string.IsNullOrWhiteSpace(useSslRaw) &&
            !bool.TryParse(useSslRaw, out useSsl))
        {
            throw new InvalidOperationException("S3:UseSsl must be true or false.");
        }

        _bucket = bucket;

        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey);

        client = useSsl ? client.WithSSL() : client.WithSSL(false);

        _minio = client.Build();
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

    public async Task CopyAsync(string sourceObjectKey, string destinationObjectKey, CancellationToken ct)
    {
        var copySource = new CopySourceObjectArgs()
            .WithBucket(_bucket)
            .WithObject(sourceObjectKey);

        var copyArgs = new CopyObjectArgs()
            .WithBucket(_bucket)
            .WithObject(destinationObjectKey)
            .WithCopyObjectSource(copySource);

        await _minio.CopyObjectAsync(copyArgs, ct);
    }

    public async Task<(Stream Stream, string ContentType)> GetAsync(string objectKey, CancellationToken ct)
    {
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