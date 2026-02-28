using StepWise.Http;
using StepWise.Http.Auth;
using StepWise.SampleWorkflows.Requests;

namespace StepWise.SampleWorkflows.Steps;

public class HttpProtocol;

public class LoginStep : HttpStep<LoginRequest<HttpProtocol>, LoginResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/auth/login";
    public override IAuthProvider Auth => NoAuth.Instance;
}

public class CreateUserStep : HttpStep<CreateUserRequest<HttpProtocol>, UserResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/users";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}

public class CreateOrderStep : HttpStep<CreateOrderRequest<HttpProtocol>, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/orders";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}

public class GetOrderStep : HttpStep<GetOrderRequest<HttpProtocol>, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/orders/{orderId}";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}
