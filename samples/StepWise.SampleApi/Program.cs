using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using StepWise.SampleApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Listen on HTTP only — no HTTPS for local testing
builder.WebHost.UseUrls("http://localhost:5000");

var jwtKey = "sample-api-secret-key-for-testing-only-must-be-at-least-32-chars";
var jwtIssuer = "StepWise.SampleApi";

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// Do not require auth globally — endpoints opt in with [Authorize]
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.FallbackPolicy = null; // no auth required unless explicitly specified
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// In-memory stores
var users = new ConcurrentDictionary<string, UserResponse>();
var orders = new ConcurrentDictionary<string, OrderResponse>();

// --- Auth ---

// Explicitly anonymous — no JWT required
app.MapPost("/auth/login", ([FromBody] LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.Unauthorized();

    var userId = Guid.NewGuid().ToString();
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    return Results.Ok(new LoginResponse(
        Token: new JwtSecurityTokenHandler().WriteToken(token),
        UserId: userId
    ));
}).AllowAnonymous();

// --- Users ---

app.MapPost("/users", ([FromBody] CreateUserRequest req) =>
{
    var user = new UserResponse(
        Id: Guid.NewGuid().ToString(),
        Email: req.Email,
        FirstName: req.FirstName,
        LastName: req.LastName,
        Role: req.Role
    );
    users[user.Id] = user;
    return Results.Created($"/users/{user.Id}", user);
}).RequireAuthorization();

app.MapGet("/users/{id}", (string id) =>
    users.TryGetValue(id, out var user)
        ? Results.Ok(user)
        : Results.NotFound(new { error = $"User '{id}' not found." })
).RequireAuthorization();

// --- Orders ---

app.MapPost("/orders", ([FromBody] CreateOrderRequest req) =>
{
    if (!users.ContainsKey(req.UserId))
        return Results.BadRequest(new { error = $"User '{req.UserId}' does not exist." });

    var order = new OrderResponse(
        Id: Guid.NewGuid().ToString(),
        UserId: req.UserId,
        ProductName: req.ProductName,
        Quantity: req.Quantity,
        UnitPrice: req.UnitPrice,
        Status: "pending"
    );
    orders[order.Id] = order;
    return Results.Created($"/orders/{order.Id}", order);
}).RequireAuthorization();

app.MapGet("/orders/{id}", (string id) =>
    orders.TryGetValue(id, out var order)
        ? Results.Ok(order)
        : Results.NotFound(new { error = $"Order '{id}' not found." })
).RequireAuthorization();

app.Run();
