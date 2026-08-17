using System.Text;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace finrecon360_backend.Controllers.Payments
{
    [ApiController]
    [Route("api/payments/payhere")]
    public class PayHereCheckoutController : ControllerBase
    {
        private readonly IPayHereCheckoutService _payHereCheckoutService;

        public PayHereCheckoutController(IPayHereCheckoutService payHereCheckoutService)
        {
            _payHereCheckoutService = payHereCheckoutService;
        }

        [HttpGet("checkout/{orderId}")]
        public IActionResult LaunchCheckout(string orderId)
        {
            if (!_payHereCheckoutService.TryGetCheckoutLaunchHtml(orderId, out var launchHtml) || string.IsNullOrWhiteSpace(launchHtml))
            {
                return NotFound(new { message = "Checkout session not found or expired." });
            }

            return Content(launchHtml, "text/html", Encoding.UTF8);
        }
    }
}