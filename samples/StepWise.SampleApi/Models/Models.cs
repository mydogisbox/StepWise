namespace StepWise.SampleApi.Models;

// --- Auth ---

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string UserId);

// --- Users ---

public record CreateUserRequest(string Email, string FirstName, string LastName, string Role);

public record UserResponse(string Id, string Email, string FirstName, string LastName, string Role);

// --- Orders ---

public record CreateOrderRequest(string UserId, string ProductName, int Quantity, decimal UnitPrice);

public record OrderResponse(string Id, string UserId, string ProductName, int Quantity, decimal UnitPrice, string Status);
