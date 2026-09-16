public class CounterTests
{
    [Test]
    public async Task Null() =>
        await Assert.That(Counter.Count(null)).IsEqualTo(0);

    // A string is enumerable, but it is never a list of values
    [Test]
    public async Task String() =>
        await Assert.That(Counter.Count("abc")).IsEqualTo(0);

    [Test]
    public async Task NotEnumerable() =>
        await Assert.That(Counter.Count(5)).IsEqualTo(0);

    [Test]
    public async Task Collection() =>
        await Assert.That(Counter.Count(new List<int> {1, 2, 3})).IsEqualTo(3);

    [Test]
    public async Task GenericCollection() =>
        await Assert.That(Counter.Count(new HashSet<int> {1, 2, 3})).IsEqualTo(3);

    [Test]
    public async Task ReadOnlyCollection() =>
        await Assert.That(Counter.Count(new ReadOnlyValues([1, 2, 3]))).IsEqualTo(3);

    [Test]
    public async Task Enumerable() =>
        await Assert.That(Counter.Count(Values(3))).IsEqualTo(3);

    // A LINQ iterator over a list knows its count, so the selector is not run just to count
    [Test]
    public async Task SelectIterator()
    {
        var selected = 0;
        var values = new List<int> {1, 2, 3}.Select(_ =>
        {
            selected++;
            return _;
        });

        await Assert.That(Counter.Count(values)).IsEqualTo(3);
        await Assert.That(selected).IsEqualTo(0);
    }

    // A filter has no count until it runs, so the values are enumerated
    [Test]
    public async Task WhereIterator() =>
        await Assert.That(Counter.Count(new List<int> {1, 2, 3}.Where(_ => _ > 1))).IsEqualTo(2);

    [Test]
    public async Task NonGenericEnumerable() =>
        await Assert.That(Counter.Count(new NonGenericValues())).IsEqualTo(3);

    static IEnumerable<int> Values(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return index;
        }
    }

    // Only the non-generic interface, so there is no element type to count with
    class NonGenericValues :
        IEnumerable
    {
        public IEnumerator GetEnumerator() =>
            new[] {1, 2, 3}.GetEnumerator();
    }

    // Only IReadOnlyCollection, so the count comes from that interface
    class ReadOnlyValues(int[] values) :
        IReadOnlyCollection<int>
    {
        public int Count => values.Length;

        public IEnumerator<int> GetEnumerator() =>
            ((IEnumerable<int>) values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
