using FluentAssertions;
using PolyAI.Tests.QaProbes.Fakes;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-123 root-cause isolation for the streaming-cancellation wedge (P2.1–P2.5).
/// These probes remove every PolyAI type from the picture and exercise StreamReader
/// directly, so the failure is attributed to the exact line rather than inferred.
/// </summary>
public sealed class P6_RootCauseIsolationProbes
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// CONTROL: ReadLineAsync(token) — the call INSIDE the loop body — cancels correctly.
    /// This rules out the token plumbing and the fake stream as the cause.
    /// </summary>
    [Fact]
    public async Task P6_1_ReadLineAsync_with_a_token_cancels_on_an_idle_stream()
    {
        using var idle = new IdleAfterPrefixStream("first line\n");
        using var reader = new StreamReader(idle);
        using var cts = new CancellationTokenSource();

        (await reader.ReadLineAsync(cts.Token)).Should().Be("first line");
        await cts.CancelAsync();

        var act = async () => await Task.Run(async () => await reader.ReadLineAsync(cts.Token)).WaitAsync(Budget);

        (await act.Should().ThrowAsync<Exception>()).Which
            .Should().BeAssignableTo<OperationCanceledException>(
                "the async read observes the token, so the token plumbing is sound");
    }

    /// <summary>
    /// THE ROOT CAUSE: StreamReader.EndOfStream refills its buffer with a SYNCHRONOUS read.
    /// It takes no CancellationToken and cannot be cancelled. Because ProviderBase evaluates
    /// it as the `while` condition, the ThrowIfCancellationRequested() in the loop body is
    /// unreachable once the connection goes idle.
    /// </summary>
    [Fact]
    public async Task P6_2_EndOfStream_blocks_forever_on_an_idle_stream_and_ignores_cancellation()
    {
        using var idle = new IdleAfterPrefixStream("first line\n");
        using var reader = new StreamReader(idle);
        using var cts = new CancellationTokenSource();

        (await reader.ReadLineAsync(cts.Token)).Should().Be("first line");
        await cts.CancelAsync();

        // Mirrors `while (!reader.EndOfStream)` in ProviderBase.ReadSseChunksAsync
        // and OllamaProvider.StreamAsync.
        var act = async () => await Task.Run(() => reader.EndOfStream).WaitAsync(Budget);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().BeOfType<TimeoutException>(
            "EndOfStream performs a blocking, non-cancellable read; this is the wedge. " +
            "Fix: drop EndOfStream and loop on `await reader.ReadLineAsync(ct)` until it returns null.");
    }

    /// <summary>
    /// The corrected loop shape, run against the same stream, to prove the recommended fix works.
    /// </summary>
    [Fact]
    public async Task P6_3_The_recommended_loop_shape_cancels_promptly()
    {
        using var idle = new IdleAfterPrefixStream("first line\n");
        using var reader = new StreamReader(idle);
        using var cts = new CancellationTokenSource();

        var act = async () => await Task.Run(async () =>
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (line is null) break;
                await cts.CancelAsync();
            }
        }).WaitAsync(Budget);

        (await act.Should().ThrowAsync<Exception>()).Which
            .Should().BeAssignableTo<OperationCanceledException>();
    }
}
