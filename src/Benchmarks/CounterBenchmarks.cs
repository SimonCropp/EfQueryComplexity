// Counter.Count runs for every execution of a query with a Contains list, once for each list
[MemoryDiagnoser]
public class CounterBenchmarks
{
    object values = null!;

    [ParamsAllValues]
    public ValuesKind Kind { get; set; }

    [Params(10, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var list = Enumerable.Range(0, Size).ToList();
        values = Kind switch
        {
            ValuesKind.Array => list.ToArray(),
            ValuesKind.List => list,
            ValuesKind.HashSet => list.ToHashSet(),
            ValuesKind.Select => list.Select(_ => _),
            ValuesKind.Where => list.Where(_ => _ >= 0),
            ValuesKind.Yield => Iterate(Size),
            _ => throw new UnreachableException()
        };
    }

    [Benchmark]
    public int Count() =>
        Counter.Count(values);

    static IEnumerable<int> Iterate(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return index;
        }
    }
}

public enum ValuesKind
{
    Array,
    List,
    HashSet,
    Select,
    Where,
    Yield
}
