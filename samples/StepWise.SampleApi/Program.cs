using StepWise.SampleApi;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:4200");

var app = AppFactory.Create(builder);
app.Run();
