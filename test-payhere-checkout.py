#!/usr/bin/env python3
"""
PayHere Checkout Verification Script
Tests if merchant credentials work with PayHere sandbox
"""

import hashlib
import sys
from urllib.parse import urlencode

def generate_payhere_checkout_url(
    merchant_id: str,
    merchant_secret: str,
    order_id: str,
    amount: str,
    currency: str = "LKR",
    return_url: str = "http://localhost:4200/onboarding/success",
    base_url: str = "https://sandbox.payhere.lk/pay/checkout"
) -> str:
    """
    Generate PayHere checkout URL with proper MD5 hash
    """
    # Step 1: Calculate merchant secret hash
    secret_hash = hashlib.md5(merchant_secret.encode()).hexdigest().upper()
    
    # Step 2: Calculate checkout hash
    hash_input = f"{merchant_id}{order_id}{amount}{currency}{secret_hash}"
    checkout_hash = hashlib.md5(hash_input.encode()).hexdigest().upper()
    
    # Step 3: Build query parameters
    params = {
        "merchant_id": merchant_id,
        "return_url": return_url,
        "cancel_url": return_url.replace("success", "cancel"),
        "notify_url": "https://your-backend.com/api/webhooks/payhere",
        "order_id": order_id,
        "items": "Subscription",
        "currency": currency,
        "amount": amount,
        "first_name": "Tenant",
        "last_name": "Admin",
        "email": "test@finrecon.local",
        "phone": "0000000000",
        "address": "N/A",
        "city": "Colombo",
        "country": "Sri Lanka",
        "hash": checkout_hash,
    }
    
    # Step 4: Build final URL
    query_string = urlencode(params)
    checkout_url = f"{base_url}?{query_string}"
    
    return checkout_url, {
        "merchant_id": merchant_id,
        "order_id": order_id,
        "amount": amount,
        "currency": currency,
        "secret_hash": secret_hash,
        "hash_input": hash_input,
        "final_hash": checkout_hash,
    }

def main():
    print("=" * 80)
    print("PayHere Sandbox Checkout Verification")
    print("=" * 80)
    
    # Your credentials
    merchant_id = "1235059"
    merchant_secret = "2062380248294231799519543775622691672540"
    order_id = "TEST-ORDER-001"
    amount = "0.50"  # 50 cents in LKR
    currency = "LKR"
    
    print(f"\n📋 Configuration:")
    print(f"  Merchant ID:     {merchant_id}")
    print(f"  Merchant Secret: {merchant_secret}")
    print(f"  Order ID:        {order_id}")
    print(f"  Amount:          {amount} {currency}")
    
    # Generate URL
    checkout_url, debug_info = generate_payhere_checkout_url(
        merchant_id=merchant_id,
        merchant_secret=merchant_secret,
        order_id=order_id,
        amount=amount,
        currency=currency
    )
    
    print(f"\n🔗 Generated Checkout URL:")
    print(f"{checkout_url}\n")
    
    print(f"🔐 Hash Calculation Details:")
    print(f"  Merchant Secret MD5:  {debug_info['secret_hash']}")
    print(f"  Hash Input String:    {debug_info['hash_input']}")
    print(f"  Final Hash (MD5):     {debug_info['final_hash']}")
    
    print(f"\n✅ Next Steps:")
    print(f"  1. Copy the checkout URL above")
    print(f"  2. Paste it into your browser")
    print(f"  3. If you see PayHere home page instead of payment form:")
    print(f"     → Your merchant account may not be active")
    print(f"     → Log into https://sandbox.payhere.lk/ and verify account status")
    print(f"     → Check if merchant ID {merchant_id} exists and is approved")
    print(f"\n  4. If you see a payment form:")
    print(f"     → Your credentials are valid!")
    print(f"     → Use these credentials in your .env file")
    print(f"     → Update PAYHERE_MERCHANT_ID={merchant_id}")
    print(f"     → Update PAYHERE_MERCHANT_SECRET={merchant_secret}")
    
    print(f"\n⚠️  Debug Commands:")
    print(f"  # To verify hash locally:")
    print(f"  python3 {sys.argv[0]}")
    print(f"\n  # To test from your backend:")
    print(f"  curl http://localhost:5279/api/onboarding/debug/payhere-config")
    
    print("\n" + "=" * 80)

if __name__ == "__main__":
    main()
