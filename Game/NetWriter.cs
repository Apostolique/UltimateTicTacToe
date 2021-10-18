using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;

sealed class NetWriter {
    [StructLayout(LayoutKind.Explicit)]
    struct SingleUIntUnion {
        [FieldOffset(0)]
        public float SingleValue;
        [FieldOffset(0)]
        public uint UIntValue;
    }

    public static int BitsToHoldUInt(uint value) {
        var bits = 1;
        while ((value >>= 1) != 0)
            bits++;
        return bits;
    }
    public static int BitsToHoldULong(ulong value) {
        var bits = 1;
        while ((value >>= 1) != 0)
            bits++;
        return bits;
    }

    static void WriteByte(byte source, int numberOfBits, byte[] destination, int destBitOffset) {
        source = (byte)(source & (0xFF >>(8 - numberOfBits)));
        int p = destBitOffset >> 3;
        int bitsUsed = destBitOffset & 0x7;
        int bitsFree = 8 - bitsUsed;
        int bitsLeft = bitsFree - numberOfBits;
        if (bitsLeft >= 0) {
            int mask = (0xFF >> bitsFree) | (0xFF << (8 - bitsLeft));
            destination[p] = (byte)((destination[p] & mask) | (source << bitsUsed));
            return;
        }
        destination[p] = (byte)((destination[p] & (0xFF >> bitsFree)) | (source << bitsUsed));
        p += 1;
        destination[p] = (byte)((destination[p] & (0xFF << (numberOfBits - bitsFree))) | (source >> bitsFree));
    }
    static void WriteBytes(byte[] source, int sourceByteOffset, int numberOfBytes, byte[] destination, int destBitOffset) {
        int dstBytePtr = destBitOffset >> 3;
        int firstPartLen = (destBitOffset % 8);
        if (firstPartLen == 0) {
            Buffer.BlockCopy(source, sourceByteOffset, destination, dstBytePtr, numberOfBytes);
            return;
        }
        int lastPartLen = 8 - firstPartLen;
        for (int i = 0; i < numberOfBytes; i++) {
            byte src = source[sourceByteOffset + i];
            destination[dstBytePtr] &= (byte)(255 >> lastPartLen);
            destination[dstBytePtr] |= (byte)(src << firstPartLen);
            dstBytePtr++;
            destination[dstBytePtr] &= (byte)(255 << firstPartLen);
            destination[dstBytePtr] |= (byte)(src >> lastPartLen);
        }
    }
    static void WriteUInt16(ushort source, int numberOfBits, byte[] destination, int destinationBitOffset) {
        if (numberOfBits == 0)
            return;
        if (numberOfBits <= 8) {
            WriteByte((byte)source, numberOfBits, destination, destinationBitOffset);
            return;
        }
        WriteByte((byte)source, 8, destination, destinationBitOffset);
        numberOfBits -= 8;
        if (numberOfBits > 0)
            WriteByte((byte)(source >> 8), numberOfBits, destination, destinationBitOffset + 8);
    }
    static int WriteUInt32(uint source, int numberOfBits, byte[] destination, int destinationBitOffset) {
        int returnValue = destinationBitOffset + numberOfBits;
        if (numberOfBits <= 8) {
            WriteByte((byte)source, numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)source, 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 8), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 8), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 16), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 16), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        WriteByte((byte)(source >> 24), numberOfBits, destination, destinationBitOffset);
        return returnValue;
    }
    static int WriteUInt64(ulong source, int numberOfBits, byte[] destination, int destinationBitOffset) {
        int returnValue = destinationBitOffset + numberOfBits;
        if (numberOfBits <= 8) {
            WriteByte((byte)source, numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)source, 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 8), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 8), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 16), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 16), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 24), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 24), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 32), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 32), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 40), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 40), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 48), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 48), 8, destination, destinationBitOffset);
        destinationBitOffset += 8;
        numberOfBits -= 8;
        if (numberOfBits <= 8) {
            WriteByte((byte)(source >> 56), numberOfBits, destination, destinationBitOffset);
            return returnValue;
        }
        WriteByte((byte)(source >> 56), 8, destination, destinationBitOffset);
        return returnValue;
    }

    public NetWriter() => EnsureSize(_lengthBits = 3);
    public NetWriter(int capacity) => EnsureSize(_lengthBits = 3 + capacity);

    public byte[] Data {
        get {
            WriteByte((byte)((LengthBytes << 3) - _lengthBits), 3, _data, 0);
            return _data;
        }
    }

    public int LengthBits => _lengthBits - 3;
    public int LengthBytes => (_lengthBits + 7) >> 3;

    byte[] _data;
    int _lengthBits;

    public void Clear() => _lengthBits = 3;
    public void Clear(int startBits) => _lengthBits = 3 + startBits;

    public void Put(bool value) {
        EnsureSize(_lengthBits + 1);
        WriteByte(value ? (byte)1 : (byte)0, 1, _data, _lengthBits);
        _lengthBits += 1;
    }
    public void Put(byte source) {
        EnsureSize(_lengthBits + 8);
        WriteByte(source, 8, _data, _lengthBits);
        _lengthBits += 8;
    }
    public void Put(sbyte source) {
        EnsureSize(_lengthBits + 8);
        WriteByte((byte)source, 8, _data, _lengthBits);
        _lengthBits += 8;
    }
    public void Put(byte[] source) {
        var bits = source.Length * 8;
        EnsureSize(_lengthBits + bits);
        WriteBytes(source, 0, source.Length, _data, _lengthBits);
        _lengthBits += bits;
    }
    public void Put(short source) {
        EnsureSize(_lengthBits + 16);
        WriteUInt16((ushort)source, 16, _data, _lengthBits);
        _lengthBits += 16;
    }
    public void Put(ushort source) {
        EnsureSize(_lengthBits + 16);
        WriteUInt16(source, 16, _data, _lengthBits);
        _lengthBits += 16;
    }
    public void Put(int source) {
        EnsureSize(_lengthBits + 32);
        WriteUInt32((uint)source, 32, _data, _lengthBits);
        _lengthBits += 32;
    }
    public void Put(uint source) {
        EnsureSize(_lengthBits + 32);
        WriteUInt32(source, 32, _data, _lengthBits);
        _lengthBits += 32;
    }
    public int Put(int min, int max, int value) {
        var range = (uint)(max - min);
        var numBits = BitsToHoldUInt(range);
        var rvalue = (uint)(value - min);
        Write(rvalue, numBits);
        return numBits;
    }
    public int Put(uint min, uint max, uint value) {
        var range = max - min;
        var numBits = BitsToHoldUInt(range);
        var rvalue = value - min;
        Write(rvalue, numBits);
        return numBits;
    }
    public int WriteVariableUInt(uint value) {
        var retval = 1;
        var num1 = value;
        while (num1 >= 0x80) {
            Put((byte)(num1 | 0x80));
            num1 >>= 7;
            retval++;
        }
        Put((byte)num1);
        return retval;
    }
    public void Put(float source) {
        SingleUIntUnion su;
        su.UIntValue = 0;
        su.SingleValue = source;
        Put(su.UIntValue);
    }
    public void Put(float value, float min, float max, int numberOfBits) => Write((uint)(((1 << numberOfBits) - 1) * ((value - min) / (max - min))), numberOfBits);
    public void Put(Vector2 xy) {
        Put(xy.X);
        Put(xy.Y);
    }
    public void Put(Vector2 point, Rectangle rect) {
        Put(rect.Left, rect.Right, (int)point.X);
        Put(rect.Top, rect.Bottom, (int)point.Y);
    }
    public void PutRotation(float value, int bits) {
        const float twoPI = MathF.PI * 2;
        var maxVal = 1 << bits;
        var packedRadians = (int)MathF.Round((MathHelper.WrapAngle(value) + MathF.PI) / twoPI * maxVal);
        if (packedRadians == maxVal)
            packedRadians = 0;
        Write((uint)packedRadians, bits);
    }
    public void Put(long source) {
        EnsureSize(_lengthBits + 64);
        var usource = (ulong)source;
        WriteUInt64(usource, 64, _data, _lengthBits);
        _lengthBits += 64;
    }
    public void Put(ulong source) {
        EnsureSize(_lengthBits + 64);
        WriteUInt64(source, 64, _data, _lengthBits);
        _lengthBits += 64;
    }
    public void Put(double source) {
        var val = BitConverter.GetBytes(source);
        Put(val);
    }
    public void Put(string source) {
        if (string.IsNullOrEmpty(source)) {
            WriteVariableUInt(0);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(source);
        WriteVariableUInt((uint)bytes.Length);
        Put(bytes);
    }

    void EnsureSize(int bits) {
        var byteLen = (bits + 7) >> 3;
        const int overAllocateAmount = 0;
        if (_data == null)
            _data = new byte[byteLen + overAllocateAmount];
        else if (_data.Length < byteLen)
            Array.Resize(ref _data, byteLen + overAllocateAmount);
    }

    int Write(uint source, int numberOfBits) {
        EnsureSize(_lengthBits + numberOfBits);
        WriteUInt32(source, numberOfBits, _data, _lengthBits);
        _lengthBits += numberOfBits;
        return numberOfBits;
    }
}