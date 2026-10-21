using Microsoft.AspNetCore.Mvc;
using Grubify.Coupon.Validation;

namespace GrubifyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponsController : ControllerBase
    {
        private static readonly CouponValidator Validator = new();

        private static readonly List<CouponInfo> AvailableCoupons = new()
        {
            new CouponInfo { Code = "WELCOME10", DiscountPercent = 10, Description = "10% off your first order", MinOrderAmount = 15.00m, MaxUses = 1 },
            new CouponInfo { Code = "GRUBIFY20", DiscountPercent = 20, Description = "20% off orders over $30", MinOrderAmount = 30.00m, MaxUses = 3 },
            new CouponInfo { Code = "FREEDEL", DiscountPercent = 0, Description = "Free delivery on any order", MinOrderAmount = 0m, FreeDelivery = true, MaxUses = 5 },
            new CouponInfo { Code = "SUMMER25", DiscountPercent = 25, Description = "Summer special - 25% off", MinOrderAmount = 20.00m, MaxUses = 2 },
            new CouponInfo { Code = "LOYALTY15", DiscountPercent = 15, Description = "Loyalty reward - 15% off", MinOrderAmount = 10.00m, MaxUses = 10 },
        };

        private static readonly CouponValidationCache<CouponValidationResult> Validations = new(
            AvailableCoupons.Select(c => c.Code), code => Validator.Validate(code));
        private static readonly Dictionary<string, Dictionary<string, int>> UserCouponUsage = new();

        [HttpGet]
        public ActionResult<IEnumerable<object>> GetAvailableCoupons()
        {
            var coupons = AvailableCoupons.Select(c => new
            {
                c.Code,
                c.Description,
                c.DiscountPercent,
                c.MinOrderAmount,
                c.FreeDelivery
            });
            return Ok(coupons);
        }

        [HttpPost("apply")]
        public ActionResult ApplyCoupon([FromBody] ApplyCouponRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CouponCode))
                return BadRequest(new { error = "Coupon code is required" });

            var coupon = AvailableCoupons.FirstOrDefault(c =>
                c.Code.Equals(request.CouponCode, StringComparison.OrdinalIgnoreCase));

            if (coupon == null)
                return NotFound(new { error = "Invalid coupon code" });

            // Coupon codes and validator settings are fixed for this process.
            var validation = Validations.Get(coupon.Code);
            if (!validation.IsValid)
                return BadRequest(new { error = validation.ErrorMessage });

            var userId = request.UserId ?? "anonymous";
            if (!UserCouponUsage.ContainsKey(userId))
                UserCouponUsage[userId] = new Dictionary<string, int>();

            var usage = UserCouponUsage[userId];
            var timesUsed = usage.GetValueOrDefault(coupon.Code, 0);
            if (timesUsed >= coupon.MaxUses)
                return BadRequest(new { error = "Coupon usage limit reached", maxUses = coupon.MaxUses });

            if (request.OrderTotal < coupon.MinOrderAmount)
            {
                return BadRequest(new
                {
                    error = $"Minimum order amount of ${coupon.MinOrderAmount} required",
                    minOrderAmount = coupon.MinOrderAmount,
                    currentTotal = request.OrderTotal
                });
            }

            var discountAmount = coupon.DiscountPercent > 0
                ? Math.Round(request.OrderTotal * (coupon.DiscountPercent / 100m), 2)
                : 0m;

            usage[coupon.Code] = timesUsed + 1;

            return Ok(new
            {
                couponCode = coupon.Code,
                description = coupon.Description,
                discountPercent = coupon.DiscountPercent,
                discountAmount,
                freeDelivery = coupon.FreeDelivery,
                originalTotal = request.OrderTotal,
                newTotal = request.OrderTotal - discountAmount,
                fingerprint = validation.Fingerprint,
                message = $"Coupon {coupon.Code} applied successfully!"
            });
        }

        [HttpPost("validate")]
        public ActionResult ValidateCoupon([FromBody] ValidateCouponRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CouponCode))
                return BadRequest(new { valid = false, error = "Coupon code is required" });

            var coupon = AvailableCoupons.FirstOrDefault(c =>
                c.Code.Equals(request.CouponCode, StringComparison.OrdinalIgnoreCase));

            if (coupon == null)
                return Ok(new { valid = false, error = "Invalid coupon code" });

            var validation = Validations.Get(coupon.Code);

            return Ok(new
            {
                valid = validation.IsValid,
                code = coupon.Code,
                description = coupon.Description,
                discountPercent = coupon.DiscountPercent,
                minOrderAmount = coupon.MinOrderAmount,
                freeDelivery = coupon.FreeDelivery
            });
        }
    }

    public class ApplyCouponRequest
    {
        public string CouponCode { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public decimal OrderTotal { get; set; }
    }

    public class ValidateCouponRequest
    {
        public string CouponCode { get; set; } = string.Empty;
    }

    public class CouponInfo
    {
        public string Code { get; set; } = string.Empty;
        public int DiscountPercent { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal MinOrderAmount { get; set; }
        public bool FreeDelivery { get; set; }
        public int MaxUses { get; set; } = 1;
    }
}
