using Microsoft.AspNetCore.SignalR;
using NewTek;
using NewTek.NDI;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Tractus.NdiToHlsFfmpeg.Controllers;

public unsafe class NdiFfmpegWrapper
{
    private readonly string writePath;
    private readonly IHubContext<ControlSignalRHub> hub;
    public string? Source { get; private set; }

    public NdiFfmpegWrapper(
        IHubContext<ControlSignalRHub> hub)
    {
        this.writePath = Path.Combine(AppContext.BaseDirectory, "hls");

        if (!Directory.Exists(this.writePath))
        {
            Directory.CreateDirectory(this.writePath);
        }

        this.hub = hub;
    }

    private Thread? workerThread;
    public bool Running { get; private set; } = true;

    public void Start(string sourceName)
    {
        if (this.workerThread is not null
            && this.workerThread.IsAlive)
        {
            throw new InvalidOperationException("A transmission thread for NDI is already running.");
        }

        this.Running = true;

        this.Source = sourceName;

        this.workerThread = new Thread(this.NdiReceiverWorkerThread)
        {
            Name = "NDI to HLS Thread"
        };

        this.Width = 0;
        this.Height = 0;
        this.FrameRateDenominator = 0;
        this.FrameRateNumerator = 0;
        this.FramesReceived = 0;

        this.workerThread.Start();
    }

    public void Stop()
    {
        if (this.workerThread is null
            || !this.Running)
        {
            return;
        }

        this.Running = false;
        this.Source = null;
        this.workerThread.Join();
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameRateNumerator { get; set; }
    public int FrameRateDenominator { get; set; }
    public int FramesReceived { get; set; }

    private void CleanUpFiles()
    {
        var directoryInfo = new DirectoryInfo(this.writePath);

        foreach (var file in directoryInfo.GetFiles("*.*"))
        {
            file.Delete();
        }
    }

    private void NdiReceiverWorkerThread()
    {
        this.CleanUpFiles();

        if (string.IsNullOrEmpty(this.Source))
        {
            this.Running = false;
            return;
        }

        var createSettings = new NDIlib.recv_create_v3_t
        {
            color_format = NDIlib.recv_color_format_e.recv_color_format_fastest,
            bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest,
            p_ndi_recv_name = nint.Zero,
            source_to_connect_to = new NDIlib.source_t
            {
                p_ndi_name = UTF.StringToUtf8(this.Source)
            }
        };

        var listenerPtr = NDIlib.recv_create_v3(ref createSettings);
        Marshal.FreeHGlobal(createSettings.source_to_connect_to.p_ndi_name);

        var videoFrame = new NDIlib.video_frame_v2_t();

        Process? ffmpeg = null;
        Stream? videoStream = null;
        var needNotify = true;

        var pathToWebRoot = Path.Combine(this.writePath, "hls_output.m3u8");

        while (this.Running)
        {
            var frameResult = NDIlib.recv_capture_v3(
                listenerPtr,
                &videoFrame,
                (NDIlib.audio_frame_v3_t*)nint.Zero,
                (NDIlib.metadata_frame_t*)nint.Zero,
                100);

            if (frameResult != NDIlib.frame_type_e.frame_type_video)
            {
                continue;
            }

            if (ffmpeg is null
                || this.Width != videoFrame.xres
                || this.Height != videoFrame.yres
                || this.FrameRateNumerator != videoFrame.frame_rate_N
                || this.FrameRateDenominator != videoFrame.frame_rate_D)
            {
                if (ffmpeg is not null)
                {
                    ffmpeg.StandardInput.Close();
                    ffmpeg.StandardInput.Dispose();
                    ffmpeg.Kill();
                    ffmpeg.Dispose();
                    ffmpeg = null;
                }

                this.Width = videoFrame.xres;
                this.Height = videoFrame.yres;
                this.FrameRateNumerator = videoFrame.frame_rate_N;
                this.FrameRateDenominator = videoFrame.frame_rate_D;


                var ffmpegArgs = $"-re -f rawvideo -pixel_format uyvy422 -video_size {videoFrame.xres}x{videoFrame.yres} -framerate {videoFrame.frame_rate_N / videoFrame.frame_rate_D} -i - " +
                            "-c:v libx264 -preset ultrafast -crf 30 -tune zerolatency -g 60 -keyint_min 60 -sc_threshold 0 " +
                            $"-f hls -hls_time 1 -hls_list_size 3 -hls_flags delete_segments+omit_endlist+split_by_time+temp_file {pathToWebRoot}";

                ffmpeg = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        RedirectStandardInput = true
                    }
                };

                this.FramesReceived = 0;
                needNotify = true;

                ffmpeg.Start();

                videoStream = ffmpeg.StandardInput.BaseStream;
            }

            this.FramesReceived++;

            var videoDataSpan = new Span<byte>(videoFrame.p_data.ToPointer(), videoFrame.line_stride_in_bytes * videoFrame.yres);

            videoStream?.Write(videoDataSpan);

            NDIlib.recv_free_video_v2(listenerPtr, ref videoFrame);

            if (this.FramesReceived > 240 && needNotify)
            {
                needNotify = false;
                this.hub.Clients.All.SendAsync("NdiReceiverActive");
            }
        }

        NDIlib.recv_destroy(listenerPtr);
        videoStream?.Dispose();
        ffmpeg?.Dispose();

        this.hub.Clients.All.SendAsync("NdiReceiverStopped");

        this.CleanUpFiles();

        this.Running = false;
        this.workerThread = null;
    }
}