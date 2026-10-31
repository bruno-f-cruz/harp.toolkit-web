using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Harp.Toolkit.Web;
using Harp.Toolkit.Web.Devices;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
if (OperatingSystem.IsBrowser())
{
    builder.Services.AddScoped<DeviceSession>();
}

var host = builder.Build();
if (OperatingSystem.IsBrowser())
{
    await host.Services.GetRequiredService<DeviceSession>().InitializeAsync();
}
await host.RunAsync();
