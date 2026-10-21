namespace GrubifyApi.Controllers
{
    internal sealed class CouponValidationCache<T> where T : class
    {
        private readonly IReadOnlyDictionary<string, Entry> _entries;

        public CouponValidationCache(IEnumerable<string> codes, Func<string, T> validate)
        {
            ArgumentNullException.ThrowIfNull(codes);
            ArgumentNullException.ThrowIfNull(validate);

            _entries = codes.ToDictionary(
                code => code,
                code => new Entry(code, validate),
                StringComparer.OrdinalIgnoreCase);
        }

        public T Get(string code) => _entries[code].Get();

        private sealed class Entry
        {
            private readonly string _code;
            private readonly Func<string, T> _validate;
            private readonly object _gate = new();
            private T? _value;

            public Entry(string code, Func<string, T> validate)
            {
                _code = code;
                _validate = validate;
            }

            public T Get()
            {
                var cached = Volatile.Read(ref _value);
                if (cached is not null)
                    return cached;

                lock (_gate)
                {
                    _value ??= _validate(_code)
                        ?? throw new InvalidOperationException("Coupon validation returned no result");
                    return _value;
                }
            }
        }
    }
}
