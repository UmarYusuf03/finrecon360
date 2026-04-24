namespace finrecon360_backend.Options
{
    public class PayHereOptions
    {
        public string MerchantId { get; set; } = string.Empty;
        public string MerchantSecret { get; set; } = string.Empty;
        public string CheckoutBaseUrl { get; set; } = "https://sandbox.payhere.lk/pay/checkout";
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public string NotifyUrl { get; set; } = string.Empty;
        public string Currency { get; set; } = "LKR";
    }
}
