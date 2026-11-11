using Delegat.Enums;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public sealed class ImageControllerMediaTests
{
    [Fact]
    public async Task LegacyImageReplacement_PreservesVideoSlotsAndAppendsSurplusImages()
    {
        var (db, controller, product) = CreateController();
        AddFile(db, product.Id, 1, "image/jpeg");
        AddFile(db, product.Id, 2, "video/mp4");
        AddFile(db, product.Id, 3, "image/png");
        AddFile(db, product.Id, 4, "video/webm");
        AddFile(db, product.Id, 5, "image/webp");
        AddFile(db, product.Id, 6, "image/jpeg");
        AddFile(db, product.Id, 7, "image/png");
        AddFile(db, product.Id, 8, "image/webp");
        AddFile(db, product.Id, 9, "image/gif");
        db.images.AddRange(
            new ImageReference { productId = product.Id, fileId = 1, index = 0 },
            new ImageReference { productId = product.Id, fileId = 2, index = 1 },
            new ImageReference { productId = product.Id, fileId = 3, index = 2 },
            new ImageReference { productId = product.Id, fileId = 4, index = 3 },
            new ImageReference { productId = product.Id, fileId = 5, index = 4 });
        await db.SaveChangesAsync();

        var result = await controller.PostProductImages(product.Id, [6, 7, 8, 9]);

        Assert.IsType<OkResult>(result);
        var media = db.images.Where(i => i.productId == product.Id).OrderBy(i => i.index).ToArray();
        Assert.Equal(new[] { 6, 2, 7, 4, 8, 9 }, media.Select(i => i.fileId));
        Assert.Equal(Enumerable.Range(0, 6), media.Select(i => i.index));
    }

    [Fact]
    public async Task LegacyImageReplacement_RemovesUnusedImageSlotsWithoutRemovingVideos()
    {
        var (db, controller, product) = CreateController();
        AddFile(db, product.Id, 1, "image/jpeg");
        AddFile(db, product.Id, 2, "video/mp4");
        AddFile(db, product.Id, 3, "image/png");
        AddFile(db, product.Id, 4, "image/webp");
        AddFile(db, product.Id, 5, "image/gif");
        db.images.AddRange(
            new ImageReference { productId = product.Id, fileId = 1, index = 0 },
            new ImageReference { productId = product.Id, fileId = 2, index = 1 },
            new ImageReference { productId = product.Id, fileId = 3, index = 2 },
            new ImageReference { productId = product.Id, fileId = 4, index = 3 },
            new ImageReference { productId = product.Id, fileId = 5, index = 4 });
        await db.SaveChangesAsync();

        await controller.PostProductImages(product.Id, [4]);

        var media = db.images.Where(i => i.productId == product.Id).OrderBy(i => i.index).ToArray();
        Assert.Equal(new[] { 4, 2 }, media.Select(i => i.fileId));
        Assert.Equal(new[] { 0, 1 }, media.Select(i => i.index));
    }

    private static (DataContext Db, Delegat.Controllers.ImageController Controller, Models.Products.Product Product)
        CreateController()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var product = data.AddProduct(store.Id);
        db.SaveChanges();

        var currentUser = new FakeCurrentUserService
        {
            UserId = user.Id,
            SelectedStoreId = store.Id,
            User = user
        };
        var controller = new Delegat.Controllers.ImageController(
            currentUser,
            TestDbFactory.CreateUnitOfWork(db),
            db,
            new ServiceCollection().BuildServiceProvider());
        return (db, controller, product);
    }

    private static void AddFile(DataContext db, int productId, int id, string contentType)
    {
        db.files.Add(new Models.File
        {
            Id = id,
            productId = productId,
            authorUserId = 1,
            filePath = $"stores/1/products/{productId}/{id}",
            contentType = contentType,
            length = 10,
            fileType = contentType.StartsWith("image/") ? FileType.Image : FileType.Other,
            uploadDateTime = DateTime.UtcNow
        });
    }
}
