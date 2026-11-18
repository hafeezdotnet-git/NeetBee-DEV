namespace PaymentService.Configuration;

/// <summary>
/// Payment settings ("Payments" section). Gateway secrets (Payments:Razorpay:KeySecret) go in an environment
/// variable or user secrets (Payments__Razorpay__KeySecret), never in source control.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";
    public const string FakeGateway = "Fake";

    /// <summary>"Fake" (default, no money moves). Add "Razorpay" when a real gateway class exists.</summary>
    public string Gateway { get; set; } = FakeGateway;

    /// <summary>Currency for every payment. Only "INR" is used today.</summary>
    public string Currency { get; set; } = "INR";

    /// <summary>Largest single payment accepted, in rupees.</summary>
    public decimal MaxAmount { get; set; } = 100000m;

    /// <summary>Settings for a future Razorpay gateway. Not read by the fake gateway.</summary>
    public RazorpayOptions Razorpay { get; set; } = new();

    public bool IsFake => string.Equals(Gateway, FakeGateway, StringComparison.OrdinalIgnoreCase);
}

public sealed class RazorpayOptions
{
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Secret. Set via Payments__Razorpay__KeySecret.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Secret used to check Razorpay webhook signatures. Set via Payments__Razorpay__WebhookSecret.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
