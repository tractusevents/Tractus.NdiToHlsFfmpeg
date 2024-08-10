using Microsoft.AspNetCore.Mvc;

namespace Tractus.NdiToHlsFfmpeg.Controllers;
[ApiController]
public class NdiWrapController : ControllerBase
{
    private readonly NdiFfmpegWrapper ndiWrapper;
    private readonly FinderWrapper finder;

    public NdiWrapController(
        NdiFfmpegWrapper wrapper,
        FinderWrapper finder)
    {
        this.finder = finder;
        this.ndiWrapper = wrapper;
    }

    [HttpGet]
    [Route("/sources")]
    public IResult GetSources()
    {
        var sources = this.finder.Finder.Sources
            .OrderBy(x => x.ComputerName)
            .ThenBy(x => x.SourceName)
            .Select(x => x.Name).ToList();

        return Results.Ok(sources);
    }

    [HttpGet]
    [Route("/start/{sourceName}")]
    public IResult StartNdiSourceTransmitter(string sourceName)
    {
        try
        {
            this.ndiWrapper.Start(sourceName);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.ToString());    
        }

        return Results.Ok();
    }

    [HttpGet]
    [Route("/stop")]
    public IResult RequestStopNdiSource()
    {
        try
        {
            this.ndiWrapper.Stop();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.ToString());
        }

        return Results.Ok();
    }

    [HttpGet]
    [Route("/status")]
    public IResult GetStatus()
    {
        return Results.Ok(new
        {
            this.ndiWrapper.Source,
            this.ndiWrapper.Width,
            this.ndiWrapper.Height,
            this.ndiWrapper.FrameRateNumerator,
            this.ndiWrapper.FrameRateDenominator,
            this.ndiWrapper.FramesReceived,
            this.ndiWrapper.Running
        });
    }
}
