
namespace firehosing;

public record CborObject {
    private readonly Dictionary<string, object> _fields = [];
    public object this[string key] {
        get { return _fields[key]; }
        set { _fields[key] = value; }
    }
}