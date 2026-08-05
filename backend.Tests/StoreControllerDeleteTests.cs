using Delegat.Controllers;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests;

public class StoreControllerDeleteTests
{
    private sealed class RecordingDeletionService : IStoreDeletionService
    {
        public int? LastStoreId { get; private set; }
        public string? LastConfirmation { get; private set; }
        public int CallCount { get; private set; }

        public Task DeleteStoreAsync(int storeId, string confirmationName, CancellationToken ct = default)
        {
            CallCount++;
            LastStoreId = storeId;
            LastConfirmation = confirmationName;
            return Task.CompletedTask;
        }
    }

    private static StoreController Create(
        FakeCurrentUserService current,
        RecordingDeletionService deletion)
    {
        var controller = new StoreController(
            current,
            unitOfWork: null!,
            serviceProvider: null!,
            authorizationService: null!,
            storeDeletion: deletion);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    [Fact]
    public async Task DeleteStore_NonAdmin_ThrowsForbidden()
    {
        var current = new FakeCurrentUserService
        {
            UserId = 1,
            User = new User { Id = 1, UserName = "op", isActive = true, isAdmin = false },
        };
        var deletion = new RecordingDeletionService();
        var controller = Create(current, deletion);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.DeleteStore(5, new StoreController.DeleteStoreForm { confirmationName = "X" }));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("store_delete_forbidden", ex.Code);
        Assert.Equal(0, deletion.CallCount);
    }

    [Fact]
    public async Task DeleteStore_Admin_DelegatesToService()
    {
        var current = new FakeCurrentUserService
        {
            UserId = 2,
            SelectedStoreId = 9,
            User = new User { Id = 2, UserName = "owner", isActive = true, isAdmin = true },
        };
        var deletion = new RecordingDeletionService();
        var controller = Create(current, deletion);

        var result = await controller.DeleteStore(
            9,
            new StoreController.DeleteStoreForm { confirmationName = "Acme" });

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, deletion.CallCount);
        Assert.Equal(9, deletion.LastStoreId);
        Assert.Equal("Acme", deletion.LastConfirmation);
    }
}
