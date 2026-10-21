using Grubify.Coupon.Validation;
using GrubifyApi.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Tests;

public class CouponsControllerTests
{
    [Fact]
    public void ApplyingKnownCodeKeepsFingerprintAndDiscount()
    {
        var controller = new CouponsController();
        var expectedFingerprint = new CouponValidator().Validate("WELCOME10").Fingerprint;
        var response = Assert.IsType<OkObjectResult>(controller.ApplyCoupon(new ApplyCouponRequest
        {
            CouponCode = "welcome10",
            UserId = Guid.NewGuid().ToString(),
            OrderTotal = 30m
        }));

        Assert.Equal("WELCOME10", Property<string>(response.Value, "couponCode"));
        Assert.Equal(expectedFingerprint, Property<string>(response.Value, "fingerprint"));
        Assert.Equal(3m, Property<decimal>(response.Value, "discountAmount"));
        Assert.Equal(27m, Property<decimal>(response.Value, "newTotal"));
    }

    [Fact]
    public void ValidationEndpointAndInvalidCodeKeepTheirBehavior()
    {
        var controller = new CouponsController();
        var valid = Assert.IsType<OkObjectResult>(controller.ValidateCoupon(new ValidateCouponRequest
        {
            CouponCode = "freedel"
        }));
        Assert.True(Property<bool>(valid.Value, "valid"));
        Assert.Equal("FREEDEL", Property<string>(valid.Value, "code"));

        Assert.IsType<NotFoundObjectResult>(controller.ApplyCoupon(new ApplyCouponRequest
        {
            CouponCode = "UNKNOWN",
            OrderTotal = 30m
        }));
        var invalid = Assert.IsType<OkObjectResult>(controller.ValidateCoupon(new ValidateCouponRequest
        {
            CouponCode = "UNKNOWN"
        }));
        Assert.False(Property<bool>(invalid.Value, "valid"));
    }

    [Fact]
    public void UsageAndMinimumOrderChecksKeepTheirResponses()
    {
        var controller = new CouponsController();
        var userId = Guid.NewGuid().ToString();
        var lowOrder = Assert.IsType<BadRequestObjectResult>(controller.ApplyCoupon(new ApplyCouponRequest
        {
            CouponCode = "WELCOME10",
            UserId = userId,
            OrderTotal = 10m
        }));
        Assert.Equal(15m, Property<decimal>(lowOrder.Value, "minOrderAmount"));

        var request = new ApplyCouponRequest { CouponCode = "WELCOME10", UserId = userId, OrderTotal = 30m };
        Assert.IsType<OkObjectResult>(controller.ApplyCoupon(request));
        var overLimit = Assert.IsType<BadRequestObjectResult>(controller.ApplyCoupon(request));
        Assert.Equal(1, Property<int>(overLimit.Value, "maxUses"));
    }

    private static T Property<T>(object? value, string name) =>
        (T)value!.GetType().GetProperty(name)!.GetValue(value)!;
}
