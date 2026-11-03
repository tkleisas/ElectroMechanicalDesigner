using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EMD.Web;
using Emd.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var emdServer = builder.Configuration["EmdServer"] ?? "http://127.0.0.1:8080";
var apiToken = builder.Configuration["ApiToken"];
builder.Services.AddScoped(sp =>
{
    var http = new HttpClient { BaseAddress = new Uri(emdServer) };
    if (!string.IsNullOrWhiteSpace(apiToken))
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
    return http;
});
builder.Services.AddScoped<WorkspaceClient>();

await builder.Build().RunAsync();
