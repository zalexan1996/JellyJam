using JellyJam.Core;
using NAudio.Wave;

namespace JellyJam.Infrastructure.Windows.Audio;

public sealed class NAudioPlayer(HttpClient httpClient) : IAudioPlayer
{
    private readonly object positionLock = new();
    private WasapiPlayer? activeOutput;
    private TimeSpan lastPosition;

    public TimeSpan Position
    {
        get
        {
            lock (positionLock)
            {
                if (activeOutput is { } output)
                {
                    try { lastPosition = TimeSpan.FromSeconds((double)output.GetPosition() / output.OutputWaveFormat.AverageBytesPerSecond); }
                    catch (Exception) { }
                }
                return lastPosition;
            }
        }
    }

    public Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken)
        => Task.Run(() => PlayStreamAsync(source, onStarted, cancellationToken), cancellationToken);

    private async Task PlayStreamAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken)
    {
        lock (positionLock) lastPosition = TimeSpan.Zero;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Uri);
            request.Headers.Add("X-Emby-Token", source.AccessToken);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new JellyJamException("The audio stream was denied. Run 'configure' and check streaming permissions.");
            response.EnsureSuccessStatusCode();
            await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var stream = new CancellableReadStream(network, cancellationToken);
            var frame = Mp3Frame.LoadFromStream(stream) ?? throw new JellyJamException("The server returned an empty audio stream.", false);
            var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
            using var decoder = new AcmMp3FrameDecompressor(new Mp3WaveFormat(frame.SampleRate, channels, frame.FrameLength, frame.BitRate));
            var buffer = new DrainingWaveProvider(decoder.OutputFormat);
            await using var output = CreateOutput(buffer);
            var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) => stopped.TrySetResult(args.Exception);
            var pcm = new byte[65536];
            var started = false;
            try
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var decoded = decoder.DecompressFrame(frame, pcm, 0);
                    while (buffer.BufferedBytes + decoded > buffer.Capacity)
                    {
                        if (stopped.Task.IsCompleted) throw new JellyJamException("The audio device stopped unexpectedly.");
                        await Task.Delay(20, cancellationToken);
                    }
                    buffer.Add(pcm.AsSpan(0, decoded));
                    if (!started && buffer.BufferedDuration >= TimeSpan.FromMilliseconds(400)) Start();
                    if (stopped.Task.IsCompleted) throw new JellyJamException("The audio device stopped unexpectedly.");
                    frame = Mp3Frame.LoadFromStream(stream);
                } while (frame is not null);
                buffer.Complete();
                if (!started) Start();
                var error = await stopped.Task.WaitAsync(cancellationToken);
                if (error is not null) throw new JellyJamException("Audio output failed. Check the default Windows playback device.");
            }
            finally
            {
                lock (positionLock) activeOutput = null;
                output.Stop();
            }

            void Start()
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Play();
                lock (positionLock) activeOutput = output;
                started = true;
                onStarted();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JellyJamException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        { throw new JellyJamException("Audio streaming failed or timed out. Check the connection and try the next track.", false); }
        catch (Exception)
        { throw new JellyJamException("Audio could not be decoded or played. Check Windows audio and Jellyfin's MP3 transcoding support.", false); }
    }

    private static WasapiPlayer CreateOutput(IWaveProvider source)
    {
        WasapiPlayer? output = null;
        try
        {
            output = new WasapiPlayerBuilder().WithSharedMode().Build();
            output.Init(source);
            return output;
        }
        catch (Exception)
        {
            output?.Dispose();
            throw new JellyJamException("Cannot open the default Windows audio device. Connect or enable a playback device.");
        }
    }
}

internal sealed class DrainingWaveProvider(WaveFormat format) : IWaveProvider
{
    private readonly BufferedWaveProvider buffer = new(format, TimeSpan.FromSeconds(3)) { ReadFully = true };
    private int complete;
    public WaveFormat WaveFormat => buffer.WaveFormat;
    public int BufferedBytes => buffer.BufferedBytes;
    public int Capacity => buffer.BufferLength;
    public TimeSpan BufferedDuration => buffer.BufferedDuration;
    public void Add(ReadOnlySpan<byte> samples) => buffer.AddSamples(samples);
    public void Complete() => Volatile.Write(ref complete, 1);
    public int Read(Span<byte> destination)
        => Volatile.Read(ref complete) == 1 && buffer.BufferedBytes == 0 ? 0 : buffer.Read(destination);
}

internal sealed class CancellableReadStream(Stream inner, CancellationToken cancellationToken) : Stream
{
    private long position;
    public override int Read(byte[] buffer, int offset, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var total = 0;
        while (total < count)
        {
            var read = inner.ReadAsync(buffer.AsMemory(offset + total, count - total), timeout.Token).AsTask().GetAwaiter().GetResult();
            if (read == 0) break;
            total += read;
            position += read;
        }
        return total;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => position; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}