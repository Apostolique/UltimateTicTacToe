using System;
using System.Text;
using LiteNetLib.Utils;
using Microsoft.Xna.Framework;

sealed class NetReader {
    static byte ReadByte(byte[] fromBuffer, int numberOfBits, int readBitOffset) {
        int bytePtr = readBitOffset >> 3;
        int startReadAtIndex = readBitOffset - (bytePtr * 8);
        if (startReadAtIndex == 0 && numberOfBits == 8)
            return fromBuffer[bytePtr];
        byte returnValue = (byte)(fromBuffer[bytePtr] >> startReadAtIndex);
        int numberOfBitsInSecondByte = numberOfBits - (8 - startReadAtIndex);
        if (numberOfBitsInSecondByte < 1)
            return (byte)(returnValue & (255 >>(8 - numberOfBits)));
        byte second = fromBuffer[bytePtr + 1];
        second &= (byte)(255 >>(8 - numberOfBitsInSecondByte));
        return (byte)(returnValue | (byte)(second << (numberOfBits - numberOfBitsInSecondByte)));
    }
    static void ReadBytes(byte[] fromBuffer, int numberOfBytes, int readBitOffset, byte[] destination, int destinationByteOffset) {
        int readPtr = readBitOffset >> 3;
        int startReadAtIndex = readBitOffset - (readPtr * 8);
        if (startReadAtIndex == 0) {
            Buffer.BlockCopy(fromBuffer, readPtr, destination, destinationByteOffset, numberOfBytes);
            return;
        }
        int secondPartLen = 8 - startReadAtIndex;
        int secondMask = 255 >> secondPartLen;
        for (int i = 0; i < numberOfBytes; i++) {
            int b = fromBuffer[readPtr] >> startReadAtIndex;
            readPtr++;
            int second = fromBuffer[readPtr] & secondMask;
            destination[destinationByteOffset++] = (byte)(b | (second << secondPartLen));
        }
    }
    static ushort ReadUInt16(byte[] fromBuffer, int numberOfBits, int readBitOffset) {
        ushort returnValue;
        if (numberOfBits <= 8) {
            returnValue = ReadByte(fromBuffer, numberOfBits, readBitOffset);
            return returnValue;
        }
        returnValue = ReadByte(fromBuffer, 8, readBitOffset);
        numberOfBits -= 8;
        readBitOffset += 8;
        if (numberOfBits <= 8)
            returnValue |= (ushort)(ReadByte(fromBuffer, numberOfBits, readBitOffset) << 8);
        return returnValue;
    }
    static uint ReadUInt32(byte[] fromBuffer, int numberOfBits, int readBitOffset) {
        uint returnValue;
        if (numberOfBits <= 8) {
            returnValue = ReadByte(fromBuffer, numberOfBits, readBitOffset);
            return returnValue;
        }
        returnValue = ReadByte(fromBuffer, 8, readBitOffset);
        numberOfBits -= 8;
        readBitOffset += 8;
        if (numberOfBits <= 8) {
            returnValue |= (uint)(ReadByte(fromBuffer, numberOfBits, readBitOffset) << 8);
            return returnValue;
        }
        returnValue |= (uint)(ReadByte(fromBuffer, 8, readBitOffset) << 8);
        numberOfBits -= 8;
        readBitOffset += 8;
        if (numberOfBits <= 8) {
            uint r = ReadByte(fromBuffer, numberOfBits, readBitOffset);
            r <<= 16;
            returnValue |= r;
            return returnValue;
        }
        returnValue |= (uint)(ReadByte(fromBuffer, 8, readBitOffset) << 16);
        numberOfBits -= 8;
        readBitOffset += 8;
        returnValue |= (uint)(ReadByte(fromBuffer, numberOfBits, readBitOffset) << 24);
        return returnValue;
    }

    public NetReader() {}
    public NetReader(NetDataReader reader) => ReadFrom(reader);

    public byte[] Data => _data;
    public int LengthBits => _lengthBits - _userDataOffsetBits;
    public int LengthBytes => (_lengthBits - _userDataOffsetBits + 7) >> 3;
    public int ReadBits => _readBits - _userDataOffsetBits;
    public bool EndOfData => _readBits >= _lengthBits;

    byte[] _data;
    int _readBits;
    int _lengthBits;
    int _userDataOffsetBits;

    public void ReadFrom(NetDataReader reader) {
        _data = reader.RawData;
        _readBits = _userDataOffsetBits = reader.UserDataOffset << 3;
        _userDataOffsetBits += 3;
        _lengthBits = (reader.RawDataSize << 3) - ReadByte(3);
    }

    public bool ReadBool() {
        var retval = ReadByte(_data, 1, _readBits);
        _readBits += 1;
        return retval == 1;
    }
    public sbyte ReadSByte() {
        var retval = ReadByte(_data, 8, _readBits);
        _readBits += 8;
        return (sbyte)retval;
    }
    public byte ReadByte() {
        var retval = ReadByte(_data, 8, _readBits);
        _readBits += 8;
        return retval;
    }
    public short ReadShort() {
        var retval = ReadUInt16(_data, 16, _readBits);
        _readBits += 16;
        return (short)retval;
    }
    public ushort ReadUShort() {
        var retval = ReadUInt16(_data, 16, _readBits);
        _readBits += 16;
        return (ushort)retval;
    }
    public int ReadInt() {
        var retval = ReadUInt32(_data, 32, _readBits);
        _readBits += 32;
        return (int)retval;
    }
    public int ReadInt(int min, int max) {
        var range = (uint)(max - min);
        var numBits = NetWriter.BitsToHoldUInt(range);
        var rvalue = ReadUInt(numBits);
        return (int)(min + rvalue);
    }
    public uint ReadUInt() {
        var retval = ReadUInt32(_data, 32, _readBits);
        _readBits += 32;
        return retval;
    }
    public uint ReadUInt(uint min, uint max) {
        var range = max - min;
        var numBits = NetWriter.BitsToHoldUInt(range);
        var rvalue = ReadUInt(numBits);
        return min + rvalue;
    }
    public float ReadFloat() {
        if ((_readBits & 7) == 0) {
            var retval = BitConverter.ToSingle(_data, _readBits >> 3);
            _readBits += 32;
            return retval;
        }
        var bytes = new byte[4];
        ReadBytes(bytes, 0, 4);
        return BitConverter.ToSingle(bytes, 0);
    }
    public float ReadFloat(float min, float max, int numberOfBits) => min + ((float)ReadUInt(numberOfBits) / ((1 << numberOfBits) - 1) * (max - min));
    public Vector2 ReadVector2() => new Vector2(ReadFloat(), ReadFloat());
    public Vector2 ReadPoint(Rectangle rect) => new Vector2(ReadInt(rect.Left, rect.Right), ReadInt(rect.Top, rect.Bottom));
    public float ReadRotation(int bits) => MathHelper.ToRadians((int)ReadUInt(bits) / (float)(1 << bits) * 360) - MathF.PI;
    public long ReadLong() {
        unchecked {
            var retval = ReadULong();
            var longRetval = (long)retval;
            return longRetval;
        }
    }
    public long ReadLong(int numberOfBits) => (long)ReadULong(numberOfBits);
    public long ReadLong(long min, long max) => min + (long)ReadULong(NetWriter.BitsToHoldULong((ulong)(max - min)));
    public ulong ReadULong() {
        var low = ReadUInt32(_data, 32, _readBits);
        _readBits += 32;
        var high = ReadUInt32(_data, 32, _readBits);
        var retval = low + (high << 32);
        _readBits += 32;
        return retval;
    }
    public double ReadDouble() {
        if ((_readBits & 7) == 0) {
            var retval = BitConverter.ToDouble(_data, _readBits >> 3);
            _readBits += 64;
            return retval;
        }
        return BitConverter.ToDouble(ReadBytes(8), 0);
    }
    public string ReadString() {
        var byteLen = (int)ReadVariableUInt();
        if (byteLen <= 0)
            return string.Empty;
        if ((ulong)(_lengthBits - _readBits) < (ulong)byteLen * 8) {
            _readBits = _lengthBits;
            return null;
        }
        if ((_readBits & 7) == 0) {
            var retval = Encoding.UTF8.GetString(_data, _readBits >> 3, byteLen);
            _readBits += 8 * byteLen;
            return retval;
        }
        return Encoding.UTF8.GetString(ReadBytes(byteLen), 0, byteLen);
    }

    byte ReadByte(int numberOfBits) {
        var retval = ReadByte(_data, numberOfBits, _readBits);
        _readBits += numberOfBits;
        return retval;
    }
    void ReadBytes(byte[] into, int offset, int numberOfBytes) {
        ReadBytes(_data, numberOfBytes, _readBits, into, offset);
        _readBits += (8 * numberOfBytes);
        return;
    }
    byte[] ReadBytes(int numberOfBytes) {
        var retval = new byte[numberOfBytes];
        ReadBytes(_data, numberOfBytes, _readBits, retval, 0);
        _readBits += (8 * numberOfBytes);
        return retval;
    }
    uint ReadUInt(int numberOfBits) {
        var retval = ReadUInt32(_data, numberOfBits, _readBits);
        _readBits += numberOfBits;
        return retval;
    }
    uint ReadVariableUInt() {
        int num1 = 0,
            num2 = 0;
        while (_lengthBits - _readBits >= 8) {
            var num3 = ReadByte();
            num1 |= (num3 & 0x7f) << num2;
            num2 += 7;
            if ((num3 & 0x80) == 0)
                return (uint)num1;
        }
        return (uint)num1;
    }
    ulong ReadULong(int numberOfBits) {
        ulong retval;
        if (numberOfBits <= 32)
            retval = ReadUInt32(_data, numberOfBits, _readBits);
        else {
            retval = ReadUInt32(_data, 32, _readBits);
            retval |= (ulong)ReadUInt32(_data, numberOfBits - 32, _readBits + 32) << 32;
        }
        _readBits += numberOfBits;
        return retval;
    }
}