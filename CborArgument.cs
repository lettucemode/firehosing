
namespace firehosing;

public class CborArgument
{
  public required ArraySegment<byte> Bytes { get; set; }
  public required ulong Value { get; set; }
}
