namespace Dressfield.Core.Enums;

public enum OrderStatus
{
    Pending = 0,          // Created, not yet sent to payment
    AwaitingPayment = 1,  // Redirected to BOG iPay
    Paid = 2,             // BOG webhook confirmed payment
    Processing = 3,       // Admin is preparing the order
    Shipped = 4,          // Dispatched
    Delivered = 5,        // Confirmed delivered
    Cancelled = 6,        // Cancelled before payment
    Refunded = 7          // Payment reversed
}
