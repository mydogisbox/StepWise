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
        builder.Services.AddSingleton<SampleApiService>();

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

        app.MapPost("/users", (SampleApiService svc, [FromBody] CreateUserRequest req) =>
        {
            var user = svc.CreateUser(req);
            return Results.Created($"/users/{user.Id}", user);
        }).RequireAuthorization();

        app.MapGet("/users/{id}", (SampleApiService svc, string id) =>
        {
            try { return Results.Ok(svc.GetUser(id)); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = $"User '{id}' not found." }); }
        }).RequireAuthorization();

        app.MapPost("/orders", (SampleApiService svc, [FromBody] CreateOrderRequest req) =>
        {
            try
            {
                var order = svc.CreateOrder(req);
                return Results.Created($"/orders/{order.Id}", order);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireAuthorization();

        app.MapGet("/orders/{id}", (SampleApiService svc, string id) =>
        {
            try { return Results.Ok(svc.GetOrder(id)); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = $"Order '{id}' not found." }); }
        }).RequireAuthorization();

        app.MapPut("/users/{userId}/address", (SampleApiService svc, string userId, [FromBody] UpdateUserAddressRequest req) =>
        {
            try { return Results.Ok(svc.UpdateUserAddress(userId, req)); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = $"User '{userId}' not found." }); }
        }).RequireAuthorization();

        app.MapGet("/health", () => Results.Ok());

        return app;
    }
}
