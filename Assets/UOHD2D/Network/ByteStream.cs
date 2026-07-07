using System;
using System.Text;

namespace UOHD2D.Network
{
    // Big-endian reader over a fixed byte buffer, matching ServUO's PacketReader layout.
    public class ByteReader
    {
        private readonly byte[] _data;
        private int _index;

        public ByteReader(byte[] data, int offset, int length)
        {
            _data = data;
            _index = offset;
            Size = offset + length;
        }

        public int Size { get; }
        public int Index { get { return _index; } }
        public int Remaining { get { return Size - _index; } }

        public byte ReadByte()
        {
            return _index < Size ? _data[_index++] : (byte)0;
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        public ushort ReadUInt16()
        {
            return (ushort)((ReadByte() << 8) | ReadByte());
        }

        public short ReadInt16()
        {
            return (short)ReadUInt16();
        }

        public uint ReadUInt32()
        {
            return (uint)((ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte());
        }

        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        public void Skip(int count)
        {
            _index += count;
        }

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            var n = Math.Min(count, Math.Max(0, Size - _index));
            Array.Copy(_data, _index, result, 0, n);
            _index += count;
            return result;
        }

        public string ReadAsciiFixed(int length)
        {
            var bytes = ReadBytes(length);
            var end = Array.IndexOf(bytes, (byte)0);
            return Encoding.ASCII.GetString(bytes, 0, end < 0 ? length : end);
        }
    }

    // Big-endian writer that grows a buffer, matching ServUO's outgoing packet layout.
    public class ByteWriter
    {
        private byte[] _buffer;
        private int _length;

        public ByteWriter(int capacity = 64)
        {
            _buffer = new byte[Math.Max(4, capacity)];
        }

        public int Length { get { return _length; } }

        private void EnsureCapacity(int extra)
        {
            if (_length + extra <= _buffer.Length)
                return;

            var newSize = _buffer.Length * 2;
            while (newSize < _length + extra)
                newSize *= 2;

            Array.Resize(ref _buffer, newSize);
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_length++] = value;
        }

        public void WriteSByte(sbyte value)
        {
            WriteByte((byte)value);
        }

        public void WriteBoolean(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(2);
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)value;
        }

        public void WriteInt16(short value)
        {
            WriteUInt16((ushort)value);
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(4);
            _buffer[_length++] = (byte)(value >> 24);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)value;
        }

        public void WriteInt32(int value)
        {
            WriteUInt32((uint)value);
        }

        public void WriteAsciiFixed(string value, int length)
        {
            EnsureCapacity(length);
            var bytes = Encoding.ASCII.GetBytes(value ?? "");
            var n = Math.Min(length, bytes.Length);
            Array.Copy(bytes, 0, _buffer, _length, n);
            for (var i = n; i < length; i++)
                _buffer[_length + i] = 0;
            _length += length;
        }

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Array.Copy(_buffer, result, _length);
            return result;
        }
    }
}
