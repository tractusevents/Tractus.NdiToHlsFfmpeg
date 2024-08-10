using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Tractus.NdiToHlsFfmpeg.Controllers;

namespace Tractus.NdiToHlsFfmpeg;

public class Program
{
    public static void Main(string[] args)
    {

        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "hls"));

        var builder = WebApplication.CreateBuilder();

        // Add services to the container.

        builder.Services.AddCors(c =>
        {
            c.AddDefaultPolicy(o =>
            {
                o.AllowAnyHeader();
                o.AllowAnyMethod();
                o.AllowAnyOrigin();
            });

        }).AddSignalR();


        builder.Services.AddDirectoryBrowser();
        builder.Services.AddControllers();

        builder.Services.AddSingleton<NdiFfmpegWrapper>();
        builder.Services.AddSingleton<FinderWrapper>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.

        app.UseDefaultFiles();

        var hlsFileProvider = new PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "hls"));

        var hlsContentProvider = new FileExtensionContentTypeProvider();
        hlsContentProvider.Mappings[".m3u8"] = "application/x-mpegURL";
        hlsContentProvider.Mappings[".ts"] = "video/MP2T";


        app.UseStaticFiles();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
                ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                ctx.Context.Response.Headers.Append("Expires", "0");
            },
            ContentTypeProvider = hlsContentProvider,
            FileProvider = hlsFileProvider,
            RequestPath = "/hls"
        });

        app.UseDirectoryBrowser(new DirectoryBrowserOptions
        {
            FileProvider = hlsFileProvider,
            RequestPath = "/hls"
        });
        

        app.MapHub<ControlSignalRHub>("/ws", o =>
        {

        });

        app.MapControllers();

        var _ = app.Services.GetRequiredService<FinderWrapper>();

        app.Run();
    }
}

public class ControlSignalRHub : Hub
{

}