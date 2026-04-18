using StepWise.Json;
using Xunit;

namespace StepWise.SampleWorkflows.WorkflowTests.Json;

public class JsonOrderWorkflowTests : JsonWorkflowTestBase
{
    protected override IReadOnlyList<string> RequestPaths =>
    [
        "Requests/auth.requests.json",
        "Requests/order.requests.json",
        "Requests/user.requests.json"
    ];

    protected override string TargetsPath => "WorkflowTests/Json/targets.json";

    protected override IReadOnlyList<string> SharedWorkflowPaths =>
    [
        "WorkflowTests/Json/setup-user.workflow.json"
    ];

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

    [Fact]
    public Task PlacedOrder_ItemsCanBeInspectedByIndex() =>
        RunWorkflowAsync("WorkflowTests/Json/order-item-details.workflow.json");

    [Fact]
    public Task BuildStep_ResultIsAvailableAsCapture() =>
        RunWorkflowAsync("WorkflowTests/Json/build-step-capture.workflow.json");

    [Fact]
    public Task CreateUser_RequestFieldsAvailableViaCaptureRequestAs() =>
        RunWorkflowAsync("WorkflowTests/Json/capture-request.workflow.json");

    [Fact]
    public Task NestedWorkflow_StepsAndCapturesFlowIntoParent() =>
        RunWorkflowAsync("WorkflowTests/Json/nested-workflow.workflow.json");

    [Fact]
    public Task CreateUser_WithNestedAddress() =>
        RunWorkflowAsync("WorkflowTests/Json/user-with-address.workflow.json");

    [Fact]
    public Task CreatedUser_AppearsInUserList() =>
        RunWorkflowAsync("WorkflowTests/Json/user-list.workflow.json");

    [Fact]
    public Task GetUsersByRole_QueryParamFilters() =>
        RunWorkflowAsync("WorkflowTests/Json/get-users-by-role.workflow.json");

    [Fact]
    public Task GetOrder_PathParamOverride_RetrievesCorrectOrder() =>
        RunWorkflowAsync("WorkflowTests/Json/retrieve-specific-order.workflow.json");
}
