namespace SillyRedisMcp.RedisClient;

public enum RespKind { SimpleString, Error, Integer, BulkString, Array }

public sealed record RespResult
{
    public RespKind Kind { get; init; }
    public string? StringValue { get; init; }
    public long LongValue { get; init; }
    public RespResult?[] ArrayValue { get; init; } = [];

    public static RespResult SimpleString(string v) => new() { Kind = RespKind.SimpleString, StringValue = v };
    public static RespResult Error(string v)         => new() { Kind = RespKind.Error,        StringValue = v };
    public static RespResult Integer(long v)         => new() { Kind = RespKind.Integer,       LongValue = v };
    public static RespResult BulkString(string? v)  => new() { Kind = RespKind.BulkString,   StringValue = v };
    public static RespResult Array(RespResult?[] v) => new() { Kind = RespKind.Array,         ArrayValue = v };
}

public sealed class InsufficientDataException() : Exception("RESP message is incomplete — need more data.");

public static class RespParser
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    public static RespResult Parse(ReadOnlySpan<byte> data, out int consumed)
    {
        if (data.Length < 3)
            throw new InsufficientDataException();

        return data[0] switch
        {
            (byte)'+' => ParseLine(data, 1, out consumed, RespResult.SimpleString),
            (byte)'-' => ParseLine(data, 1, out consumed, RespResult.Error),
            (byte)':' => ParseInteger(data, out consumed),
            (byte)'$' => ParseBulkString(data, out consumed),
            (byte)'*' => ParseArray(data, out consumed),
            _ => throw new InvalidDataException($"Unknown RESP type byte: {data[0]}")
        };
    }

    private static RespResult ParseLine(ReadOnlySpan<byte> data, int offset, out int consumed, Func<string, RespResult> factory)
    {
        int crlfPos = IndexOfCrlf(data, offset);
        if (crlfPos < 0) throw new InsufficientDataException();
        var value = System.Text.Encoding.UTF8.GetString(data[offset..crlfPos]);
        consumed = crlfPos + 2;
        return factory(value);
    }

    private static RespResult ParseInteger(ReadOnlySpan<byte> data, out int consumed)
    {
        int crlfPos = IndexOfCrlf(data, 1);
        if (crlfPos < 0) throw new InsufficientDataException();
        var numStr = System.Text.Encoding.UTF8.GetString(data[1..crlfPos]);
        consumed = crlfPos + 2;
        return RespResult.Integer(long.Parse(numStr));
    }

    private static RespResult ParseBulkString(ReadOnlySpan<byte> data, out int consumed)
    {
        int headerEnd = IndexOfCrlf(data, 1);
        if (headerEnd < 0) throw new InsufficientDataException();

        int length = int.Parse(System.Text.Encoding.UTF8.GetString(data[1..headerEnd]));

        if (length == -1)
        {
            consumed = headerEnd + 2;
            return RespResult.BulkString(null);
        }

        int dataStart = headerEnd + 2;
        int dataEnd = dataStart + length;
        // Need dataEnd + 2 bytes (trailing \r\n)
        if (data.Length < dataEnd + 2) throw new InsufficientDataException();

        var value = System.Text.Encoding.UTF8.GetString(data[dataStart..dataEnd]);
        consumed = dataEnd + 2;
        return RespResult.BulkString(value);
    }

    private static RespResult ParseArray(ReadOnlySpan<byte> data, out int consumed)
    {
        int headerEnd = IndexOfCrlf(data, 1);
        if (headerEnd < 0) throw new InsufficientDataException();

        int count = int.Parse(System.Text.Encoding.UTF8.GetString(data[1..headerEnd]));

        if (count == -1)
        {
            consumed = headerEnd + 2;
            return RespResult.Array([]);
        }

        var elements = new RespResult?[count];
        int offset = headerEnd + 2;

        for (int i = 0; i < count; i++)
        {
            if (offset >= data.Length) throw new InsufficientDataException();
            var element = Parse(data[offset..], out int elementConsumed);
            elements[i] = element;
            offset += elementConsumed;
        }

        consumed = offset;
        return RespResult.Array(elements);
    }

    private static int IndexOfCrlf(ReadOnlySpan<byte> data, int startAt)
    {
        for (int i = startAt; i < data.Length - 1; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n')
                return i;
        }
        return -1;
    }
}

internal static class ToolHelper
{
    public static string Stringify(RespResult r) => r.Kind switch
    {
        RespKind.SimpleString => r.StringValue!,
        RespKind.Error        => r.StringValue!,
        RespKind.Integer      => r.LongValue.ToString(),
        RespKind.BulkString   => r.StringValue ?? "(nil)",
        RespKind.Array        => r.ArrayValue.Length == 0
            ? "(empty list)"
            : string.Join("\n", r.ArrayValue.Select((v, i) =>
                $"{i + 1}) {(v is null ? "(nil)" : Stringify(v))}")),
        _ => string.Empty
    };
}
