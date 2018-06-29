﻿#if NETCORE2_1

using System;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ServiceStack.Text;
using ServiceStack.Text.Common;
using ServiceStack.Text.Pools;

namespace ServiceStack.Memory
{
    public sealed class NetCoreMemory : MemoryProvider
    {
        private NetCoreMemory(){}
        public static readonly NetCoreMemory Provider = new NetCoreMemory();  
        
        public static void Configure() => Instance = Provider;
        
        public override bool ParseBoolean(ReadOnlySpan<char> value) => bool.Parse(value);

        public override bool TryParseBoolean(ReadOnlySpan<char> value, out bool result) =>
            bool.TryParse(value, out result);

        public override bool TryParseDecimal(ReadOnlySpan<char> value, out decimal result) => DefaultMemory.TryParseDecimal(value, allowThousands: true, out result);
        public override decimal ParseDecimal(ReadOnlySpan<char> value) => DefaultMemory.ParseDecimal(value, allowThousands: true);

        public override bool TryParseFloat(ReadOnlySpan<char> value, out float result) =>
            float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

        public override bool TryParseDouble(ReadOnlySpan<char> value, out double result) =>
            double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        
        public override float ParseFloat(ReadOnlySpan<char> value) =>
            float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        public override double ParseDouble(ReadOnlySpan<char> value) =>
            double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        public override sbyte ParseSByte(ReadOnlySpan<char> value) => SignedInteger<sbyte>.ParseSByte(value);

        public override byte ParseByte(ReadOnlySpan<char> value) => UnsignedInteger<byte>.ParseByte(value);

        public override short ParseInt16(ReadOnlySpan<char> value) => SignedInteger<short>.ParseInt16(value);

        public override ushort ParseUInt16(ReadOnlySpan<char> value) => UnsignedInteger<ushort>.ParseUInt16(value);

        public override int ParseInt32(ReadOnlySpan<char> value) => SignedInteger<int>.ParseInt32(value);

        public override uint ParseUInt32(ReadOnlySpan<char> value) => UnsignedInteger<uint>.ParseUInt32(value);

        public override uint ParseUInt32(ReadOnlySpan<char> value, NumberStyles style) => uint.Parse(value.ToString(), style);

        public override long ParseInt64(ReadOnlySpan<char> value) => SignedInteger<int>.ParseInt64(value);

        public override ulong ParseUInt64(ReadOnlySpan<char> value) => UnsignedInteger<ulong>.ParseUInt64(value);

        public override Guid ParseGuid(ReadOnlySpan<char> value) => Guid.Parse(value);
        
        public override byte[] ParseBase64(ReadOnlySpan<char> value)
        {
            byte[] bytes = BufferPool.GetBuffer(Base64.GetMaxDecodedFromUtf8Length(value.Length));
            try
            {
                if (Convert.TryFromBase64Chars(value, bytes, out var bytesWritten))
                {
                    var ret = new byte[bytesWritten];
                    Buffer.BlockCopy(bytes, 0, ret, 0, bytesWritten);
                    return ret;
                }
                else
                {
                    var chars = value.ToArray();
                    return Convert.FromBase64CharArray(chars, 0, chars.Length);
                }
            }
            finally 
            {
                BufferPool.ReleaseBufferToPool(ref bytes);
            }
        }

        public override Task WriteAsync(Stream stream, ReadOnlySpan<char> value, CancellationToken token=default)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen:true))
            {
                writer.Write(value);
            }

            return TypeConstants.EmptyTask;
        }

        public override async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> value, CancellationToken token = default)
        {
            await stream.WriteAsync(value, token);
        }

        public override object Deserialize(Stream stream, Type type, DeserializeStringSpanDelegate deserializer)
        {
            var fromPool = false;

            if (!(stream is MemoryStream ms))
            {
                fromPool = true;

                if (stream.CanSeek)
                    stream.Position = 0;

                ms = stream.CopyToNewMemoryStream();
            }

            return Deserialize(ms, fromPool, type, deserializer);
        }

        public override async Task<object> DeserializeAsync(Stream stream, Type type, DeserializeStringSpanDelegate deserializer)
        {
            var fromPool = false;
            
            if (!(stream is MemoryStream ms))
            {
                fromPool = true;
                
                if (stream.CanSeek)
                    stream.Position = 0;

                ms = await stream.CopyToNewMemoryStreamAsync();
            }

            return Deserialize(ms, fromPool, type, deserializer);
        }

        private static object Deserialize(MemoryStream memoryStream, bool fromPool, Type type, DeserializeStringSpanDelegate deserializer)
        {
            var bytes = memoryStream.GetBufferAsSpan();
            var chars = CharPool.GetBuffer(Encoding.UTF8.GetCharCount(bytes));
            try
            {
                var charsWritten = Encoding.UTF8.GetChars(bytes, chars);
                ReadOnlySpan<char> charsSpan = chars; 
                var ret = deserializer(type, charsSpan.Slice(0, charsWritten));
                return ret;
            }
            finally
            {
                CharPool.ReleaseBufferToPool(ref chars);

                if (fromPool)
                    memoryStream.Dispose();
            }
        }

        public override StringBuilder Append(StringBuilder sb, ReadOnlySpan<char> value)
        {
            return sb.Append(value);
        }

        public override int GetUtf8CharCount(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetCharCount(bytes);

        public override int GetUtf8ByteCount(ReadOnlySpan<char> chars) => Encoding.UTF8.GetByteCount(chars);

        public override ReadOnlyMemory<byte> ToUtf8(ReadOnlySpan<char> source)
        {
            Memory<byte> bytes = new byte[Encoding.UTF8.GetByteCount(source)];
            var bytesWritten = Encoding.UTF8.GetBytes(source, bytes.Span);
            return bytes.Slice(0, bytesWritten);
        }

        public override ReadOnlyMemory<char> FromUtf8(ReadOnlySpan<byte> source)
        {
            Memory<char> chars = new char[Encoding.UTF8.GetCharCount(source)];
            var charsWritten = Encoding.UTF8.GetChars(source, chars.Span);
            return chars.Slice(0, charsWritten);
        }

        public override int ToUtf8(ReadOnlySpan<char> source, Span<byte> destination) => Encoding.UTF8.GetBytes(source, destination);

        public override int FromUtf8(ReadOnlySpan<byte> source, Span<char> destination) => Encoding.UTF8.GetChars(source, destination);
    }    
}

#endif
