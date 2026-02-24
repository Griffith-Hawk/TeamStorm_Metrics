using TeamStorm.Metrics.Options;
using TeamStorm.Metrics.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StormOptions>(builder.Configuration.GetSection(StormOptions.SectionName));
builder.Services.AddHttpClient<IStormApiClient, StormApiClient>();
builder.Services.AddScoped<IWorkItemMetricsService, WorkItemMetricsService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
