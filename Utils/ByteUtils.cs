using System.Buffers.Binary;

namespace NodeTunnel.Utils;

public class ByteUtils {
    public static uint UnpackU32(byte[] data, int offset) {
        if (offset + 4 > data.Length) 
            return 0;

        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    public static byte[] PackU32(uint value) {
        var buff = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buff, value);
        return buff;
    }
}