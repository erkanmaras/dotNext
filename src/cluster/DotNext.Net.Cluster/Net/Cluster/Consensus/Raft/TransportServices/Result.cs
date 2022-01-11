namespace DotNext.Net.Cluster.Consensus.Raft.TransportServices;

using Buffers;

internal static class Result
{
    internal const int Size = sizeof(long) + sizeof(byte);

    internal static void Write(ref SpanWriter<byte> writer, in Result<bool> result)
    {
        writer.WriteInt64(result.Term, true);
        writer.Add(result.Value.ToByte());
    }

    internal static int Write(Span<byte> output, in Result<bool> result)
    {
        var writer = new SpanWriter<byte>(output);
        Write(ref writer, in result);
        return writer.WrittenCount;
    }

    internal static Result<bool> Read(ref SpanReader<byte> reader)
        => new(reader.ReadInt64(true), ValueTypeExtensions.ToBoolean(reader.Read()));

    internal static Result<bool> Read(ReadOnlySpan<byte> input)
    {
        var reader = new SpanReader<byte>(input);
        return Read(ref reader);
    }
}