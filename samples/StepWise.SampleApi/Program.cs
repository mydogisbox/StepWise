using StepWise.SampleApi;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");

var app = AppFactory.Create(builder);
app.Run();
