using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using StepWise.SampleApi.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace StepWise.SampleApi;

public static class AppFactory
{
    private const string JwtKey = "sample-api-secret-key-for-testing-only-must-be-at-least-32-chars";
    private const string JwtIssuer = "StepWise.SampleApi";

    public static WebApplication Create(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtIssuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey))
                };
            });

        builder.Services.AddAuthorization(options => options.FallbackPolicy = null);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        var users = new ConcurrentDictionary<string, UserResponse>();
        var orders = new ConcurrentDictionary<string, OrderResponse>();

        app.MapPost("/auth/login", ([FromBody] LoginRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.Unauthorized();

            var userId = Guid.NewGuid().ToString();
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: JwtIssuer,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return Results.Ok(new LoginResponse(
                Token: new JwtSecurityTokenHandler().WriteToken(token),
                UserId: userId
            ));
        }).AllowAnonymous();

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

        app.MapPost("/orders", ([FromBody] CreateOrderRequest req) =>
        {
            if (!users.ContainsKey(req.UserId))
                return Results.BadRequest(new { error = $"User '{req.UserId}' does not exist." });

            var order = new OrderResponse(
                Id: Guid.NewGuid().ToString(),
                UserId: req.UserId,
                Items: req.Items.Select(i => new OrderItemResponse(i.ProductName, i.Quantity, i.UnitPrice)).ToList(),
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

        return app;
    }
}
