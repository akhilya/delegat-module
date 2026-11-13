using Delegat.Enums;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class ProductTransferMediaTests
{
    [Fact]
    public async Task Clone_RemapFileAttributeGalleryAndFolderReferences()
    {
        var scenario = await CreateScenarioAsync(copySucceeds: true);

        var result = await scenario.Service.ExecuteAsync(
            Request(scenario, "clone"), scenario.SourceStore.Id, scenario.User.Id);

        Assert.True(result.success);
        Assert.Equal(1, result.filesCopied);
        Assert.Equal(1, result.imagesCopied);
        Assert.False(result.originalSoftDeleted);
        Assert.Null(scenario.SourceProduct.deletedAt);

        var copiedFile = await scenario.Db.files.SingleAsync(file => file.productId == result.newProductId);
        Assert.NotEqual(scenario.SourceFile.Id, copiedFile.Id);
        Assert.NotEqual(scenario.SourceFile.filePath, copiedFile.filePath);
        Assert.Equal(scenario.TargetStore.Id, (await scenario.Db.products
            .SingleAsync(product => product.Id == result.newProductId)).storeId);
        Assert.NotEqual(scenario.SourceFolder.Id, copiedFile.folderId);

        var copiedValue = await scenario.Db.productAttributeValues
            .SingleAsync(value => value.productId == result.newProductId);
        Assert.Equal(scenario.TargetAttribute.Id, copiedValue.attributeId);
        var attributeLink = await scenario.Db.productAttributeFiles.SingleAsync(
            link => link.attributeValueId == copiedValue.Id);
        Assert.Equal(copiedFile.Id, attributeLink.fileId);
        Assert.Equal(0, attributeLink.index);

        var galleryLink = await scenario.Db.images.SingleAsync(image => image.productId == result.newProductId);
        Assert.Equal(copiedFile.Id, galleryLink.fileId);
        Assert.Equal(0, galleryLink.index);
        Assert.Single(scenario.Storage.Copies);
    }

    [Fact]
    public async Task Move_CopiesMediaEvenWhenCopyFilesIsFalseAndSoftDeletesSource()
    {
        var scenario = await CreateScenarioAsync(copySucceeds: true);
        var request = Request(scenario, "move");
        request.copyFiles = false;

        var result = await scenario.Service.ExecuteAsync(
            request, scenario.SourceStore.Id, scenario.User.Id);

        Assert.True(result.success);
        Assert.True(result.originalSoftDeleted);
        Assert.NotNull((await scenario.Db.products.IgnoreQueryFilters()
            .SingleAsync(product => product.Id == scenario.SourceProduct.Id)).deletedAt);
        var copiedFile = await scenario.Db.files.SingleAsync(file => file.productId == result.newProductId);
        var copiedValue = await scenario.Db.productAttributeValues
            .SingleAsync(value => value.productId == result.newProductId);
        Assert.Equal(copiedFile.Id, (await scenario.Db.productAttributeFiles
            .SingleAsync(link => link.attributeValueId == copiedValue.Id)).fileId);
        Assert.Equal(copiedFile.Id, (await scenario.Db.images
            .SingleAsync(image => image.productId == result.newProductId)).fileId);
        Assert.Single(scenario.Storage.Copies);
    }

    [Fact]
    public async Task FailedObjectCopyLeavesNoDanglingAttributeOrGalleryLinks()
    {
        var scenario = await CreateScenarioAsync(copySucceeds: false);

        var result = await scenario.Service.ExecuteAsync(
            Request(scenario, "clone"), scenario.SourceStore.Id, scenario.User.Id);

        Assert.True(result.success);
        Assert.Equal(0, result.filesCopied);
        Assert.Equal(1, result.filesFailed);
        Assert.Equal(0, result.imagesCopied);
        Assert.Single(await scenario.Db.files.ToListAsync());
        Assert.Empty(await scenario.Db.productAttributeFiles
            .Where(link => link.attributeValue.productId == result.newProductId).ToListAsync());
        Assert.Empty(await scenario.Db.images.Where(image => image.productId == result.newProductId).ToListAsync());
        Assert.Equal(Assert.Single(scenario.Storage.Copies).Destination, Assert.Single(scenario.Storage.DeletedPaths));
        Assert.DoesNotContain(scenario.SourceFile.filePath, scenario.Storage.DeletedPaths);
    }

    [Fact]
    public async Task Clone_WithCopyFilesFalseSkipsFilesFoldersGalleryAndAttachmentLinks()
    {
        var scenario = await CreateScenarioAsync(copySucceeds: true);
        var request = Request(scenario, "clone");
        request.copyFiles = false;

        var result = await scenario.Service.ExecuteAsync(
            request, scenario.SourceStore.Id, scenario.User.Id);

        Assert.True(result.success);
        Assert.Equal(0, result.filesCopied);
        Assert.Equal(0, result.imagesCopied);
        Assert.Single(await scenario.Db.files.ToListAsync());
        Assert.Empty(await scenario.Db.productFolders
            .Where(folder => folder.productId == result.newProductId).ToListAsync());
        Assert.Empty(await scenario.Db.productAttributeFiles
            .Where(link => link.attributeValue.productId == result.newProductId).ToListAsync());
        Assert.Empty(await scenario.Db.images.Where(image => image.productId == result.newProductId).ToListAsync());
        Assert.Empty(scenario.Storage.Copies);
    }

    private static TransferRequest Request(Scenario scenario, string mode) => new()
    {
        sourceProductId = scenario.SourceProduct.Id,
        targetStoreId = scenario.TargetStore.Id,
        mode = mode,
        newSku = mode == "clone" ? "COPY-1" : "MOVE-1",
        categoryAction = "match",
        groupAction = "none"
    };

    private static async Task<Scenario> CreateScenarioAsync(bool copySucceeds)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var sourceStore = data.AddStore("Source", members: user);
        var targetStore = data.AddStore("Target", members: user);
        var sourceCategory = data.AddCategory(sourceStore.Id, "Equipment");
        var targetCategory = data.AddCategory(targetStore.Id, "Equipment");
        var sourceAttribute = data.AddCategoryAttribute(
            sourceCategory.Id, "Manual", AttributeType.File, key: "manual");
        var targetAttribute = data.AddCategoryAttribute(
            targetCategory.Id, "Manual", AttributeType.File, key: "manual");
        var product = data.AddProduct(sourceStore.Id, "Desk lamp", "LAMP-1", sourceCategory.Id);
        var folder = new ProductFolder { productId = product.Id, name = "Documents" };
        db.productFolders.Add(folder);
        await db.SaveChangesAsync();

        var file = new Models.File
        {
            productId = product.Id,
            authorUserId = user.Id,
            folderId = folder.Id,
            filePath = $"stores/{sourceStore.Id}/products/{product.Id}/media/manual.pdf",
            displayName = "manual.pdf",
            contentType = "application/pdf",
            length = 12,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        var value = new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = sourceAttribute.Id,
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
        db.images.Add(new ImageReference { productId = product.Id, fileId = file.Id, index = 0 });
        await db.SaveChangesAsync();

        var unitOfWork = TestDbFactory.CreateUnitOfWork(db);
        var storage = new TransferStorage(copySucceeds);
        var service = new ProductTransferService(
            db,
            unitOfWork,
            storage,
            new QuotaService(unitOfWork, db),
            NullLogger<ProductTransferService>.Instance);
        return new Scenario(
            db, user, sourceStore, targetStore, product, sourceAttribute, targetAttribute,
            folder, file, storage, service);
    }

    private sealed record Scenario(
        DataContext Db,
        User User,
        Store SourceStore,
        Store TargetStore,
        Product SourceProduct,
        CategoryAttribute SourceAttribute,
        CategoryAttribute TargetAttribute,
        ProductFolder SourceFolder,
        Models.File SourceFile,
        TransferStorage Storage,
        ProductTransferService Service);

    private sealed class TransferStorage(bool copySucceeds) : IStorageService
    {
        public string Bucket => "tests";
        public List<(string Source, string Destination)> Copies { get; } = [];
        public List<string> DeletedPaths { get; } = [];

        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            Copies.Add((sourcePath, destinationPath));
            return Task.FromResult(copySucceeds);
        }

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            return Task.FromResult(true);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
