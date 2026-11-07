using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentPlanEfficiencyGuardTests
{
    private static JsonElement Patch(int id, object body, string? path = null) => JsonSerializer.SerializeToElement(new
    {
        tool = "platform_request", description = "Update selected product",
        args = new { method = "PATCH", path = path ?? $"/products/{id}", body },
    });

    [Fact]
    public void IdenticalSupportedScalars_RequestANewBulkProposal()
    {
        var reason = AgentPlanEfficiencyGuard.RejectionReason([
            Patch(7, new { description = "Shared text", storageQuantity = 8 }),
            Patch(12, new { storageQuantity = 8, description = "Shared text" }),
        ]);
        Assert.Contains("Plan not presented", reason);
        Assert.Contains("exactly the same productIds", reason);
        Assert.Contains("/products/bulk", reason);
    }

    [Fact]
    public void BarcodeClearing_CanUseBulkButDistinctBarcodeAssignmentsCannot()
    {
        Assert.NotNull(AgentPlanEfficiencyGuard.RejectionReason([
            Patch(7, new { barcode = "" }), Patch(12, new { barcode = "" }),
        ]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([
            Patch(7, new { barcode = "2000000000015" }), Patch(12, new { barcode = "2000000000022" }),
        ]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([
            Patch(7, new { barcode = "2000000000015" }), Patch(12, new { barcode = "2000000000015" }),
        ]));
    }

    [Fact]
    public void DifferentValuesAttributeReplacementAndOrderedWrites_AreNotReplanned()
    {
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { name = "A" }), Patch(12, new { name = "B" })]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { categoryId = 2 }), Patch(12, new { categoryId = 2 })]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { attributeValues = new[] { new { attributeId = 1, value = "A" } } }),
            Patch(12, new { attributeValues = new[] { new { attributeId = 1, value = "A" } } })]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { name = "A" }), Patch(7, new { name = "A" })]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { name = "A" }), Patch(12, new { name = "A" }, "/products/12?extra=1")]));
        Assert.Null(AgentPlanEfficiencyGuard.RejectionReason([Patch(7, new { name = "A" })]));
    }
}
