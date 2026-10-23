using JellyJam.Core;
using JellyJam.Infrastructure.Windows.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JellyJam.Tests;

public class AudioPipelineTests
{
    [Fact]
    public void PartialNetworkReadsAreFilledAndPositionIsTracked()
    {
        using var network = new FragmentedStream([1, 2, 3, 4, 5]);
        using var stream = new CancellableReadStream(network, CancellationToken.None);
        var buffer = new byte[8];
        Assert.Equal(5, stream.Read(buffer, 0, 8));
        Assert.Equal(5, stream.Position);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer.Take(5));
        Assert.Equal(0, stream.Read(buffer, 0, 8));
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public async Task CancellationInterruptsBlockedFrameRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var network = new BlockedStream();
        using var stream = new CancellableReadStream(network, cancellation.Token);
        var read = Task.Run(() => stream.Read(new byte[4], 0, 4));
        await network.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("Category", "LiveAudio")]
    public async Task GeneratedMp3PlaysThroughDefaultWindowsDevice()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            var signal = new SignalGenerator(44100, 2) { Gain = 0.01, Frequency = 440, Type = SignalGeneratorType.Sin };
            MediaFoundationEncoder.EncodeToMp3(signal.Take(TimeSpan.FromMilliseconds(600)).ToWaveProvider16(), file, 128000);
            using var http = new HttpClient(new FileHandler(file));
            var started = 0;
            var player = new NAudioPlayer(http);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await player.PlayAsync(new PlaybackSource(new Uri("https://music.example/stream.mp3"), "test-token", "test-session"),
                () => Interlocked.Increment(ref started), timeout.Token);
            Assert.Equal(1, started);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UnderrunIsSilenceButCompletedEmptyBufferIsEof()
    {
        var buffer = new DrainingWaveProvider(new WaveFormat(44100, 16, 2));
        var destination = new byte[16];
        Assert.Equal(16, buffer.Read(destination));
        Assert.All(destination, sample => Assert.Equal(0, sample));
        buffer.Add(new byte[] { 1, 2, 3, 4 });
        buffer.Complete();
        Assert.Equal(16, buffer.Read(destination));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, destination.Take(4));
        Assert.Equal(0, buffer.Read(destination));
    }

    [Fact]
    public async Task CancelledStreamDoesNotOpenAnAudioDevice()
    {
        using var cancellation = new CancellationTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new WaitingHandler(reached));
        var player = new NAudioPlayer(http);
        var playback = player.PlayAsync(new PlaybackSource(new Uri("https://music.example/stream"), "fake", "session"),
            () => Assert.Fail("Cancelled load must not start output."), cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class WaitingHandler(TaskCompletionSource reached) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            reached.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private sealed class FileHandler(string path) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-token", request.Headers.GetValues("X-Emby-Token").Single());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) });
        }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class BlockedStream : MemoryStream
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}