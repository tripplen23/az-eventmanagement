using Azure.Storage.Blobs;

namespace EventManagementApi.Services
{
    public class BlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;

        public BlobStorageService(BlobServiceClient blobServiceClient)
        {
            _blobServiceClient = blobServiceClient;
        }

        #region Upload File to Blob Storage
        public async Task<string> UploadFileAsync(IFormFile file, string containerName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(Guid.NewGuid().ToString() + Path.GetExtension(file.FileName));
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream);
            }

            return blobClient.Uri.ToString();
        }
        #endregion

        #region Download File from Blob Storage
        public async Task<byte[]> DownloadFileAsync(string fileName, string containerName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            var downloadInfo = await blobClient.DownloadAsync();
            using (var memoryStream = new MemoryStream())
            {
                await downloadInfo.Value.Content.CopyToAsync(memoryStream);
                return memoryStream.ToArray();
            }
        }
        #endregion
    }
}