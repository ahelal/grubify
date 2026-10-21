using GrubifyApi.Controllers;

namespace GrubifyApi.Tests;

public class CouponValidationCacheTests
{
    [Fact]
    public void KnownCodeIsValidatedOnlyOnceAcrossCaseVariants()
    {
        var calls = 0;
        var cache = new CouponValidationCache<string>(["WELCOME10"], code =>
        {
            Interlocked.Increment(ref calls);
            return code;
        });

        Assert.Equal(0, calls);
        Assert.Equal("WELCOME10", cache.Get("WELCOME10"));
        Assert.Equal("WELCOME10", cache.Get("welcome10"));
        Assert.Equal(1, calls);
        Assert.Throws<KeyNotFoundException>(() => cache.Get("UNKNOWN"));
    }

    [Fact]
    public void ConcurrentFirstUseRunsValidationOncePerCode()
    {
        var calls = 0;
        var cache = new CouponValidationCache<string>(["WELCOME10", "FREEDEL"], code =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(15);
            return code;
        });

        Parallel.For(0, 40, i => Assert.Equal("WELCOME10", cache.Get("WELCOME10")));
        Assert.Equal(1, calls);
        Assert.Equal("FREEDEL", cache.Get("FREEDEL"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FailedValidationIsRetriedRatherThanCached()
    {
        var calls = 0;
        var cache = new CouponValidationCache<string>(["WELCOME10"], code =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("transient failure");
            return code;
        });

        Assert.Throws<InvalidOperationException>(() => cache.Get("WELCOME10"));
        Assert.Equal("WELCOME10", cache.Get("WELCOME10"));
        Assert.Equal(2, calls);
    }
}
