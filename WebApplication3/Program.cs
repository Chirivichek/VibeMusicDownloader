using WebApplication3.Services;

namespace WebApplication3
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var clientId = builder.Configuration["Spotify:ClientId"];
            var clientSecret = builder.Configuration["Spotify:ClientSecret"];
            builder.Services.AddHttpClient<MusicService>();
            builder.Services.AddScoped<MusicService>();
            builder.Services.AddSingleton(new Services.SpotifyService(clientId, clientSecret));

            // Add services to the container.
            builder.Services.AddRazorPages();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var musicService = scope.ServiceProvider.GetRequiredService<MusicService>();
                musicService.CheckDownloadDirectory();
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseStaticFiles();
            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
