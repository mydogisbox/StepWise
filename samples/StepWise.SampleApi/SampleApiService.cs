using System.Collections.Concurrent;
using StepWise.SampleApi.Models;

namespace StepWise.SampleApi;

/// <summary>
/// Core business logic for the sample API, extracted from HTTP handlers.
/// Can be called directly by tests to bypass authentication and HTTP transport.
/// </summary>
public class SampleApiService
{
    private readonly ConcurrentDictionary<string, UserResponse> _users = new();
    private readonly ConcurrentDictionary<string, OrderResponse> _orders = new();

    public LoginResponse Login(LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new ArgumentException("Username and password are required.");

        // In the real API a JWT is issued — here we return a dummy token since
        // direct tests don't need auth. The UserId is still generated so
        // From(...) lookups that reference it continue to work.
        var userId = Guid.NewGuid().ToString();
        return new LoginResponse(Token: "direct-test-token", UserId: userId);
    }

    public UserResponse CreateUser(CreateUserRequest req)
    {
        var user = new UserResponse(
            Id: Guid.NewGuid().ToString(),
            Email: req.Email,
            FirstName: req.FirstName,
            LastName: req.LastName,
            Role: req.Role
        );
        _users[user.Id] = user;
        return user;
    }

    public UserResponse GetUser(string id)
    {
        if (!_users.TryGetValue(id, out var user))
            throw new KeyNotFoundException($"User '{id}' not found.");
        return user;
    }

    public OrderResponse CreateOrder(CreateOrderRequest req)
    {
        if (!_users.ContainsKey(req.UserId))
            throw new ArgumentException($"User '{req.UserId}' does not exist.");

        var order = new OrderResponse(
            Id: Guid.NewGuid().ToString(),
            UserId: req.UserId,
            Items: req.Items.Select(i => new OrderItemResponse(i.ProductName, i.Quantity, i.UnitPrice)).ToList(),
            Status: "pending"
        );
        _orders[order.Id] = order;
        return order;
    }

    public OrderResponse GetOrder(string id)
    {
        if (!_orders.TryGetValue(id, out var order))
            throw new KeyNotFoundException($"Order '{id}' not found.");
        return order;
    }

    public UpdateUserAddressResponse UpdateUserAddress(string userId, UpdateUserAddressRequest req)
    {
        if (!_users.ContainsKey(userId))
            throw new KeyNotFoundException($"User '{userId}' not found.");
        return new UpdateUserAddressResponse(userId, req.Contact);
    }
}
