using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer; // For TransferUtility
using Amazon.Runtime; // For BasicAWSCredentials
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using WinUI3App1; // For PhotoBoothSettings en App.Logger

namespace WinUI3App 
{
    public class S3Service
    {
        private readonly IAmazonS3 _s3Client;
        private readonly MinioSettings _minioSettings;
        private readonly ILogger _logger;

        public S3Service(PhotoBoothSettings settings, ILogger logger)
        {
            _minioSettings = settings?.Minio ?? throw new ArgumentNullException(nameof(settings.Minio), "MinIO settings cannot be null.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrEmpty(_minioSettings.ServiceUrl) ||
                string.IsNullOrEmpty(_minioSettings.BucketName) ||
                string.IsNullOrEmpty(_minioSettings.AccessKey) ||
                string.IsNullOrEmpty(_minioSettings.SecretKey))
            {
                _logger.Error("S3Service: MinIO configuration is incomplete (ServiceUrl, BucketName, AccessKey, or SecretKey is missing).");
                throw new InvalidOperationException("MinIO configuration is incomplete.");
            }

            var credentials = new BasicAWSCredentials(_minioSettings.AccessKey, _minioSettings.SecretKey);
            var config = new AmazonS3Config
            {
                ServiceURL = _minioSettings.ServiceUrl,
                ForcePathStyle = true, // BELANGRIJK voor MinIO en andere S3-compatibele systemen
                UseHttp = _minioSettings.UseHttp // Bepaalt of http of https wordt gebruikt.
                                                 // Als ServiceURL al http:// of https:// bevat, heeft deze minder effect,
                                                 // maar het is goed om consistent te zijn.
            };
            _s3Client = new AmazonS3Client(credentials, config);
            _logger.Information("S3Service initialized. Endpoint: {Endpoint}, Bucket: {Bucket}", _minioSettings.ServiceUrl, _minioSettings.BucketName);
        }

        public async Task<string> UploadFileAsync(string filePath, string objectKeyInBucket, string contentType = "image/jpeg")
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _logger.Error("S3Service: File path is invalid or file does not exist for upload: {FilePath}", filePath);
                return null;
            }
            if (string.IsNullOrEmpty(objectKeyInBucket))
            {
                _logger.Error("S3Service: Object key cannot be empty for upload.");
                return null;
            }

            try
            {
                var transferUtility = new TransferUtility(_s3Client);
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    BucketName = _minioSettings.BucketName,
                    Key = objectKeyInBucket,
                    FilePath = filePath,
                    ContentType = contentType
                    // Optioneel: CannedACL voor publieke objecten als je dat via ACLs wilt regelen ipv bucket policy
                    // CannedACL = S3CannedACL.PublicRead 
                };

                _logger.Information("S3Service: Starting upload of {FilePath} to bucket {Bucket} with key {Key}", filePath, _minioSettings.BucketName, objectKeyInBucket);
                await transferUtility.UploadAsync(uploadRequest);
                _logger.Information("S3Service: Upload successful for key {Key}", objectKeyInBucket);

                // Construeer de publieke URL
                // Zorg dat PublicBaseUrl correct eindigt (of niet) met een '/'
                string publicBase = _minioSettings.PublicBaseUrl.TrimEnd('/');
                string bucket = _minioSettings.BucketName;
                string key = objectKeyInBucket.TrimStart('/');

                string publicUrl = $"{publicBase}/{bucket}/{key}";
                _logger.Information("S3Service: Public URL constructed: {PublicUrl}", publicUrl);
                return publicUrl;
            }
            catch (AmazonS3Exception e)
            {
                _logger.Error(e, "S3Service: Error encountered on server. Message:'{Message}' when writing an object", e.Message);
                return null;
            }
            catch (Exception e)
            {
                _logger.Error(e, "S3Service: Unknown S3 error encountered on server. Message:'{Message}' when writing an object", e.Message);
                return null;
            }
        }

        // Je zou hier later methodes kunnen toevoegen voor het genereren van pre-signed URLs
        // of het verwijderen van objecten (voor je retentie policy, hoewel MinIO's lifecycle beter is).
    }
}