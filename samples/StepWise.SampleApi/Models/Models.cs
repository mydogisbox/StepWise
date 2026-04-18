namespace StepWise.SampleApi.Models;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string UserId);

public record CreateUserRequest(string Email, string FirstName, string LastName, string Role);
public record UserResponse(string Id, string Email, string FirstName, string LastName, string Role);

public record OrderItemRequest(string ProductName, int Quantity, decimal UnitPrice);
public record OrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);
public record CreateOrderRequest(string UserId, List<OrderItemRequest> Items);
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record RegionInfo(string State, string Country);
public record AddressInfo(string Street, string City, RegionInfo Region);
public record PrimaryContact(AddressInfo Address);
public record ContactInfo(PrimaryContact Primary);
public record UpdateUserAddressRequest(ContactInfo Contact);
public record UpdateUserAddressResponse(string UserId, ContactInfo Contact);

