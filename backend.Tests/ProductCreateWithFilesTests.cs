using System.Text;
using System.Text.Json;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Delegat.Tests;

public sealed class ProductCreateWithFilesTests
{
    [Fact]
    public async Task Create_WithRequiredFileAttributeAndVideoGallery_IsAtomicAndReusesUpload()
    {
        var (db, controller, category, attribute, storage) = await CreateControllerAsync();
        var input = ProductInput(category.Id, attribute.Id, fileId: -1);
        var payload = JsonSerializer.Serialize(
            new ProductCreateWithFilesController.CreateProductPayload(input, [-1]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await SetMultipartRequestAsync(controller, payload, ("file_-1", "video/mp4", "clip.mp4", "video-bytes"u8.ToArray()));
        var result = await controller.Create(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var productId = (int)response.Value!.GetType().GetProperty("id")!.GetValue(response.Value)!;
        var file = Assert.Single(db.files);
        Assert.Equal(productId, file.productId);
        Assert.Equal("video/mp4", file.contentType);
        Assert.Equal("video-bytes"u8.Length, file.length);
        Assert.Single(storage.StoredObjects);

        var attributeValue = Assert.Single(db.productAttributeValues);
        var attachment = Assert.Single(db.productAttributeFiles);
        Assert.Equal(attributeValue.Id, attachment.attributeValueId);
        Assert.Equal(file.Id, attachment.fileId);
        var media = Assert.Single(db.images);
        Assert.Equal(file.Id, media.fileId);
    }

    [Fact]
    public async Task Create_WhenReferencedUploadIsMissing_LeavesNoProductOrStoredObject()
    {
        var (db, controller, category, attribute, storage) = await CreateControllerAsync();
        var input = ProductInput(category.Id, attribute.Id, fileId: -1);
        var payload = JsonSerializer.Serialize(
            new ProductCreateWithFilesController.CreateProductPayload(input, [-1]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await SetMultipartRequestAsync(controller, payload);

        var error = await Assert.ThrowsAsync<ApiException>(() => controller.Create(CancellationToken.None));

        Assert.Equal("file_not_found", error.Code);
        Assert.Empty(db.products);
        Assert.Empty(db.files);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task Create_WhenGalleryMetadataHasDuplicateFileIds_RejectsBeforeReadingFileParts()
    {
        var (db, controller, category, attribute, storage) = await CreateControllerAsync();
        var input = ProductInput(category.Id, attribute.Id, fileId: -1);
        var payload = JsonSerializer.Serialize(
            new ProductCreateWithFilesController.CreateProductPayload(input, [-1, -1]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await SetMultipartRequestAsync(
            controller,
            payload,
            ("file_-1", "video/mp4", "clip.mp4", "video-bytes"u8.ToArray()));

        var error = await Assert.ThrowsAsync<ApiException>(() => controller.Create(CancellationToken.None));

        Assert.Equal("duplicate_media_files", error.Code);
        Assert.Empty(db.products);
        Assert.Empty(db.files);
        Assert.Empty(storage.StoredObjects);
    }

    private static async Task<(
        DataContext Db,
        ProductCreateWithFilesController Controller,
        Category Category,
        CategoryAttribute Attribute,
        MemoryStorage Storage)> CreateControllerAsync()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(
            category.Id, "Manual", AttributeType.File, isRequired: true);
        await db.SaveChangesAsync();

        var unitOfWork = TestDbFactory.CreateUnitOfWork(db);
        var storage = new MemoryStorage();
        var currentUser = new FakeCurrentUserService
        {
            UserId = user.Id,
            SelectedStoreId = store.Id,
            User = user
        };
        var controller = new ProductCreateWithFilesController(
            currentUser,
            unitOfWork,
            db,
            storage,
            new QuotaService(unitOfWork, db),
            new AttributeUniquenessService(db),
            new ProductAttributeFileService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ObjectValidator = new NoopObjectValidator();
        return (db, controller, category, attribute, storage);
    }

    private static Product.UserAddForm ProductInput(int categoryId, int attributeId, int fileId)
        => new()
        {
            sku = "FILE-TEST-1",
            name = "Video product",
            description = "",
            categoryId = categoryId,
            price = 100,
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm
                {
                    attributeId = attributeId,
                    value = $"[{fileId}]",
                    fileIds = [fileId]
                }
            ],
            storeAttributeValues = []
        };

    private static async Task SetMultipartRequestAsync(
        ProductCreateWithFilesController controller,
        string payload,
        params (string FieldName, string ContentType, string FileName, byte[] Bytes)[] uploads)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "product");
        foreach (var upload in uploads)
        {
            var part = new ByteArrayContent(upload.Bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(upload.ContentType);
            content.Add(part, upload.FieldName, upload.FileName);
        }

        var body = new MemoryStream();
        await content.CopyToAsync(body);
        body.Position = 0;
        controller.HttpContext.Request.Body = body;
        controller.HttpContext.Request.ContentType = content.Headers.ContentType!.ToString();
        controller.HttpContext.Request.ContentLength = body.Length;
    }

    private sealed class MemoryStorage : IStorageService
    {
        public string Bucket => "tests";
        public Dictionary<string, byte[]> StoredObjects { get; } = new(StringComparer.Ordinal);
        public List<string> DeletedPaths { get; } = [];

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadStreamAsync(
            Stream stream, string path, string contentType, long? contentLength, CancellationToken ct = default)
            => SaveAsync(stream, path, contentType, ct);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            StoredObjects.Remove(path);
            return Task.FromResult(true);
        }
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(StoredObjects.ContainsKey(path));
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream?>(StoredObjects.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();

        private async Task<FileUploadResult?> SaveAsync(Stream input, string path, string contentType, CancellationToken ct)
        {
            using var output = new MemoryStream();
            await input.CopyToAsync(output, ct);
            var bytes = output.ToArray();
            StoredObjects[path] = bytes;
            return new FileUploadResult(contentType, bytes.Length);
        }
    }

    private sealed class NoopObjectValidator : IObjectModelValidator
    {
        public void Validate(
            ActionContext actionContext,
            ValidationStateDictionary? validationState,
            string prefix,
            object? model)
        {
        }
    }
}
