using System.Threading.Channels;

namespace IronST.LanguageServer.Tests;

/// <summary>
/// 基于内存管道的双工流，用在进程内驱动 LanguageServer，不依赖真实 stdio。
/// 行为对齐真实管道：只要缓冲区里有数据就立刻返回，允许部分读。
/// </summary>
internal sealed class ChannelStream : Stream
{
    private readonly Channel<byte> _channel;
    private readonly List<byte> _buffer = new();
    private int _readPos;

    public ChannelStream()
    {
        _channel = Channel.CreateUnbounded<byte>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    }

    /// <summary>对端（模拟客户端）写入的数据端。</summary>
    public ChannelWriter<byte> Writer => _channel.Writer;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        // 缓冲区里还有存货，先吐出去
        if (_readPos < _buffer.Count)
            return Drain(buffer, offset, count);

        // 等新数据到达
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var b))
                _buffer.Add(b);

            if (_buffer.Count > 0)
                return Drain(buffer, offset, count);
        }

        return 0; // 通道关闭且缓冲为空 → EOF
    }

    /// <summary>从缓冲区取最多 count 字节拷进调用方的数组。</summary>
    private int Drain(byte[] buffer, int offset, int count)
    {
        int n = Math.Min(count, _buffer.Count - _readPos);
        _buffer.CopyTo(_readPos, buffer, offset, n);
        _readPos += n;

        // 一次性清空，避免 List 无界增长
        if (_readPos == _buffer.Count)
        {
            _buffer.Clear();
            _readPos = 0;
        }

        return n;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        for (int i = offset; i < offset + count; i++)
            await _channel.Writer.WriteAsync(buffer[i], ct).ConfigureAwait(false);
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    // 以下均不支持（本流只用于异步场景）
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("请用 ReadAsync");
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("请用 WriteAsync");
}
