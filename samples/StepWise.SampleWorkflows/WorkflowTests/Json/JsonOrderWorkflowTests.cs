using StepWise.Json;
using Xunit;

namespace StepWise.SampleWorkflows.WorkflowTests.Json;

public class JsonOrderWorkflowTests : JsonWorkflowTestBase
{
    protected override IReadOnlyList<string> RequestPaths =>
    [
        "Requests/auth.requests.json",
        "Requests/order.requests.json"
    ];

    protected override string TargetsPath => "WorkflowTests/Json/targets.json";

    [Fact]
    public Task NewUser_CanPlaceOrder_StatusIsPending() =>
        RunWorkflowAsync("WorkflowTests/Json/place-order.workflow.json");

    [Fact]
    public Task NewUser_CanPlaceOrder_WithSpecificItems() =>
        RunWorkflowAsync("WorkflowTests/Json/place-order-specific-items.workflow.json");

    [Fact]
    public Task PlacedOrder_CanBeRetrieved() =>
        RunWorkflowAsync("WorkflowTests/Json/retrieve-order.workflow.json");

    [Fact]
    public Task User_CanPlaceTwoOrders_EachGetsDistinctId() =>
        RunWorkflowAsync("WorkflowTests/Json/two-orders.workflow.json");
}
