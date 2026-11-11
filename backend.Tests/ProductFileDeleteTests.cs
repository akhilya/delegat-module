using Delegat.Enums;
using Delegat.Controllers;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class ProductFileDeleteTests
{
    [Fact]
    public async Task DeleteProductFile_WhenReferencedByFileAttribute_RejectsWithoutDeletingBlob()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, "Manual", AttributeType.File);
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        var file = new Models.File
        {
            productId = product.Id,
            authorUserId = user.Id,
            filePath = $"stores/{store.Id}/products/{product.Id}/manual.pdf",
            contentType = "application/pdf",
            length = 32,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        await db.SaveChangesAsync();

        var value = new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = attribute.Id,
            value = ""
        };
        db.productAttributeValues.Add(value);
        await db.SaveChangesAsync();
        db.productAttributeFiles.Add(new ProductAttributeFile
        {
            attributeValueId = value.Id,
            fileId = file.Id,
            index = 0
        });
        await db.SaveChangesAsync();

        var storage = new DeleteRecordingStorage();
        var unitOfWork = TestDbFactory.CreateUnitOfWork(db);
        var controller = new FileController(
            new FakeCurrentUserService { UserId = user.Id, SelectedStoreId = store.Id, User = user },
            unitOfWork,
            storage,
            new ServiceCollection().BuildServiceProvider(),
            new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance),
            new QuotaService(unitOfWork, db),
            db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            controller.DeleteProductFile(product.Id, file.Id));

        Assert.Equal("file_in_use", error.Code);
        Assert.Empty(storage.DeletedPaths);
        Assert.Equal(file.Id, Assert.Single(db.files).Id);
        Assert.Single(db.productAttributeFiles);
    }

    private sealed class DeleteRecordingStorage : IStorageService
    {
        public string Bucket => "tests";
        public List<string> DeletedPaths { get; } = [];

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            return Task.FromResult(true);
        }

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
