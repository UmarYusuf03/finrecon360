namespace finrecon360_backend.Options
{
    public class PayHereOptions
    {
        public string MerchantId { get; set; } = string.Empty;
        public string MerchantSecret { get; set; } = string.Empty;
        // PayHere's dashboard secret is a plain numeric string, but some environments store it
        // base64-encoded (e.g. to dodge shell-quoting issues in a .env file). Set to "Base64" to
        // decode it before use; anything else (including unset) treats it as already plain.
        public string MerchantSecretMode { get; set; } = string.Empty;
        public string CheckoutBaseUrl { get; set; } = "https://sandbox.payhere.lk/pay/checkout";
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public string NotifyUrl { get; set; } = string.Empty;
        public string Currency { get; set; } = "LKR";
    }
}
