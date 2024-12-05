
using System.Text;

namespace firehosing;

public class AtprotoFrame {
    private AtprotoFrame() {}
    public Dictionary<string, object> Header { get; set; } = [];
    public Dictionary<string, object> Payload { get; set; } = [];

    public static AtprotoFrame FromBytes(byte[] frame) {
        var index = 0;
        var h = DecodeCborObject(frame, ref index);
        var b = DecodeCborObject(frame, ref index);
        return new AtprotoFrame {
            Header = h,
            Payload = b,
        };
        // todo: each data type to be wrapped in a generic interface or base class that has its data type
    }

    /// <summary>
    /// Returns the byte array that represents the argument value for the current CBOR object.
    /// Per the spec, this will always be 1, 2, 4, or 8 bytes in length.
    /// On returning, `index` will be incremented to the index after the last byte returned, relative to the `bytes` argument.
    /// </summary>
    /// <param name="bytes">The complete CBOR byte sequence.</param>
    /// <param name="index">The first byte (major + arg specifier) of the current CBOR object.</param>
    /// <returns>The byte array that represents the argument value for the current CBOR object.</returns>
    /// <exception cref="Exception">invalid arg code for atproto cbor</exception>
    private static CborArgument GetCborArgument(byte[] bytes, ref int index) {
        var argLoadCode = bytes[index] & 31;
        if (argLoadCode >= (int)CborArgCode.Invalid) throw new Exception("invalid arg code for atproto cbor");

        ArraySegment<byte> argBytes;
        ulong v;
        if (argLoadCode <= (int)CborArgCode.Self) {
            argBytes = new ArraySegment<byte>([(byte)argLoadCode]);
            index++;
            v = (ulong)argLoadCode;
        } else {
            var n = (int)Math.Pow(2, argLoadCode - 24);
            argBytes = new ArraySegment<byte>(bytes, ++index, n);
            index += n;

            if (n == 1) {
                v = argBytes[0];
            }
            // multiple bytes are in network byte order
            else if (n == 2) {
                v = ((ulong)argBytes[0] << 8) | argBytes[1];
            } else if (n == 4) {
                v = ((ulong)argBytes[0] << 24) | ((ulong)argBytes[1] << 16) | ((ulong)argBytes[2] << 8) | argBytes[3];
            } else if (n == 8) {
                v = ((ulong)argBytes[0] << 56) | ((ulong)argBytes[1] << 48) | ((ulong)argBytes[2] << 40) | ((ulong)argBytes[3] << 32)
                    | ((ulong)argBytes[4] << 24) | ((ulong)argBytes[5] << 16) | ((ulong)argBytes[6] << 8) | argBytes[7];
            } else {
                throw new Exception("invalid number of arg bytes specified");
            }
        }

        return new CborArgument { Bytes = argBytes, Value = v, };
    }

    private static Dictionary<string, object> DecodeCborObject(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.Object) throw new Exception("major type is not object/map");

        var arg = GetCborArgument(bytes, ref index);
        var numPairs = arg.Value;
        var map = new Dictionary<string, object>();
        for (var i = 0U; i < numPairs; i++) {
            // first, we expect a utf8 string key, per atproto spec
            string key = DecodeUTF8String(bytes, ref index);

            // next, we expect any valid cbor data type
            var valMajor = (AtprotoDataType)(bytes[index] >> 5);
            object val = valMajor switch {
                AtprotoDataType.PositiveInteger => DecodeInteger(bytes, ref index),
                AtprotoDataType.NegativeInteger => DecodeInteger(bytes, ref index),
                AtprotoDataType.ByteString => DecodeByteString(bytes, ref index),
                AtprotoDataType.UTF8String => DecodeUTF8String(bytes, ref index),
                AtprotoDataType.Array => DecodeArray(bytes, ref index),
                AtprotoDataType.Object => DecodeCborObject(bytes, ref index),
                AtprotoDataType.Tag => DecodeTag(bytes, ref index),
                AtprotoDataType.Simple => DecodeSimple(bytes, ref index),
                _ => throw new NotImplementedException(), // todo: something sensible
            };
            map[key] = val;
        }
        return map;
    }
    
    private static object DecodeSimple(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.Simple) throw new Exception("major type is not simple");

        var arg = GetCborArgument(bytes, ref index);
        if (arg.Value == 20) return false;
        if (arg.Value == 21) return true;
        if (arg.Value == 22) return null!;
        throw new Exception("invalid simple value per atproto spec");
    }

    private static ArraySegment<byte> DecodeTag(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.Tag) throw new Exception("major type is not tag");
        var arg = GetCborArgument(bytes, ref index);
        if (arg.Value != 42) throw new Exception($"tag value must always be 42 per dag-cbor spec, was {arg.Value}");

        var nextMajor = (AtprotoDataType)(bytes[index] >> 5);
        if (nextMajor != AtprotoDataType.ByteString) throw new Exception("tag 42 must be a byte string representing a CID");
        return DecodeByteString(bytes, ref index);
    }

    private static object[] DecodeArray(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.Array) throw new Exception("major type is not array");
        var arg = GetCborArgument(bytes, ref index);
        var arr = new object[arg.Value];
        for (var i = 0U; i < arg.Value; ++i) {
            // array elements can be any valid cbor type
            var valMajor = (AtprotoDataType)(bytes[index] >> 5);
            object val = valMajor switch {
                AtprotoDataType.PositiveInteger => DecodeInteger(bytes, ref index),
                AtprotoDataType.NegativeInteger => DecodeInteger(bytes, ref index),
                AtprotoDataType.ByteString => DecodeByteString(bytes, ref index),
                AtprotoDataType.UTF8String => DecodeUTF8String(bytes, ref index),
                AtprotoDataType.Array => DecodeArray(bytes, ref index),
                AtprotoDataType.Object => DecodeCborObject(bytes, ref index),
                AtprotoDataType.Tag => DecodeTag(bytes, ref index),
                AtprotoDataType.Simple => DecodeSimple(bytes, ref index),
                _ => throw new NotImplementedException(), // todo: something sensible
            };
            arr[i] = val;
        }
        return arr;
    }

    private static ArraySegment<byte> DecodeByteString(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.ByteString) throw new Exception("major type is not byte string");
        var arg = GetCborArgument(bytes, ref index);
        index += (int)arg.Value;
        return new ArraySegment<byte>(bytes, index - (int)arg.Value, (int)arg.Value); // questionable cast
    }

    private static string DecodeUTF8String(byte[] bytes, ref int index) {
        var major = (AtprotoDataType)(bytes[index] >> 5);
        if (major != AtprotoDataType.UTF8String) throw new Exception("major type is not utf8 string");
        var arg = GetCborArgument(bytes, ref index);
        index += (int)arg.Value;
        return Encoding.UTF8.GetString(new ArraySegment<byte>(bytes, index - (int)arg.Value, (int)arg.Value)); // questionable cast
    }

    private static long DecodeInteger(byte[] bytes, ref int index) {
        var major = bytes[index] >> 5;
        if (major != (int)AtprotoDataType.PositiveInteger && major != (int)AtprotoDataType.NegativeInteger)
            throw new Exception("integer first byte type is invalid");

        var arg = GetCborArgument(bytes, ref index);
        return major == (int)AtprotoDataType.PositiveInteger
            ? (long)arg.Value
            : -1 - (long)arg.Value; // questionable cast
        // also, idk what's going on with the numbers here
        // cbor spec makes these effectively 65-bit integers, but
        // atproto spec suggests never having these longer than 53 bits anyway.
        // so we punt and make em all longs
    }

    private enum AtprotoDataType {
        PositiveInteger,
        NegativeInteger,
        ByteString,
        UTF8String,
        Array,
        Object,
        Tag,
        Simple,
    }
    
    private enum AtprotoTag {
        CID = 42,
    }

    private enum CborArgCode {
        Self = 23,
        OneByte,
        TwoBytes,
        FourBytes,
        EightBytes,
        Invalid,
    }
}
