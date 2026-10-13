using System.Buffers;
using System.Globalization;
using System.Text.Json;
using DataMonger.Jsonl;

namespace DataMonger.Synth;

/// <summary>One rendered event within a synthetic user session, in chronological order.</summary>
public sealed record SessionEvent(DateTimeOffset Timestamp, string EventType, byte[] Json);

/// <summary>One line in a synthetic shopping cart snapshot.</summary>
public sealed class CartLine
{
    public required ProductVariant Product { get; init; }
    public int Quantity { get; set; }
    public decimal LineSubtotal => Product.Price * Quantity;
}

/// <summary>Per-profile values copied onto every event of that profile's session.</summary>
public sealed record SessionIdentity(string? UserId, string Email, string StorefrontId, string Locale, string Currency);

/// <summary>
/// Builds one profile's whole synthetic browsing session: page_view, add_to_cart, remove_from_cart and
/// (30% of the time, when eligible; always when <c>forceFullFunnel</c> is set) a single purchase event, all
/// sharing one <see cref="EventSession"/> (session_id, channel and a shared 45-minute clock) and interleaved so that:
///   - the very first event is always a page_view,
///   - every add_to_cart item was viewed earlier in the same session,
///   - remove_from_cart only fires while the cart is non-empty,
///   - purchase (if any) is always the last event and only draws from what is still in the cart.
/// See Documentation/SynthesizeSessionEvents/EventSchema.md.
/// </summary>
public static class SessionEventsBuilder
{
    public const string PageViewEvent = "page_view";
    public const string AddToCartEvent = "add_to_cart";
    public const string RemoveFromCartEvent = "remove_from_cart";
    public const string PurchaseEvent = "purchase";
    public const string SourceSystem = "web_store";
    public const string SchemaVersion = "1.0";

    public const int MinViews = 0;
    public const int MaxViews = 10;
    public const int MinAddToCart = 0;
    public const int MaxAddToCart = 5;
    public const int MinRemoveFromCart = 0;
    public const int MaxRemoveFromCart = 2;
    public const double PurchaseChance = 0.30;
    public const double BumpExistingLineChance = 0.40;

    /// <summary>Payment method values; one is picked per purchase.</summary>
    public static readonly IReadOnlyList<string> PaymentMethods = new[] { "card", "debit", "wallet", "gift_card" };

    private static readonly char[] OrderIdChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static List<SessionEvent> BuildForProfile(
        JsonElement profile, ProductCatalog catalog, Random rng, DateTimeOffset now, bool forceFullFunnel = false)
    {
        if (profile.ValueKind != JsonValueKind.Object)
        {
            return new List<SessionEvent>();
        }
        var email = ProfileFields.ResolveEmail(profile);
        var viewCount = forceFullFunnel ? rng.Next(2, MaxViews + 1) : rng.Next(MinViews, MaxViews + 1);
        if (email is null || (viewCount == 0 && !forceFullFunnel))
        {
            return new List<SessionEvent>();
        }

        var identity = new SessionIdentity(
            ProfileFields.ResolveUserId(profile), email, ProfileFields.ResolveStorefrontId(profile),
            ProfileFields.ResolveLocale(profile), ProfileFields.ResolveCurrency(profile));
        var timeZone = EventSession.ResolveTimeZone(ProfileFields.ResolveTimezone(profile));
        var session = EventSession.Start(rng, timeZone, now);

        var addCount = forceFullFunnel ? rng.Next(1, MaxAddToCart + 1) : rng.Next(MinAddToCart, MaxAddToCart + 1);
        var removeCount = addCount > 0 ? rng.Next(MinRemoveFromCart, MaxRemoveFromCart + 1) : 0;

        // Token queue for everything after the mandatory first page_view.
        var tokens = new List<string>();
        tokens.AddRange(Enumerable.Repeat("view", viewCount - 1));
        tokens.AddRange(Enumerable.Repeat("add", addCount));
        tokens.AddRange(Enumerable.Repeat("remove", removeCount));
        Shuffle(tokens, rng);

        var events = new List<SessionEvent>();
        var viewedPool = new List<ProductVariant>();
        var cart = new List<CartLine>();
        var buffer = new ArrayBufferWriter<byte>();

        Execute("view", viewedPool, cart, catalog, rng, session, identity, buffer, events);

        while (tokens.Count > 0)
        {
            var index = FindFeasibleTokenIndex(tokens, cart);
            if (index < 0)
            {
                // Only "remove" tokens remain and the cart is empty: those removes can never fire.
                tokens.RemoveAll(t => t == "remove");
                continue;
            }
            var token = tokens[index];
            tokens.RemoveAt(index);
            Execute(token, viewedPool, cart, catalog, rng, session, identity, buffer, events);
        }

        if (addCount > 0 && cart.Count > 0 && (forceFullFunnel || rng.NextDouble() < PurchaseChance))
        {
            events.Add(BuildPurchaseEvent(cart, rng, session, identity, buffer));
        }

        return events;
    }

    private static int FindFeasibleTokenIndex(List<string> tokens, List<CartLine> cart)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            // "view" and "add" are always feasible (event #1 was a view); "remove" needs a non-empty cart.
            if (tokens[i] is "view" or "add" || (tokens[i] == "remove" && cart.Count > 0))
            {
                return i;
            }
        }
        return -1;
    }

    private static void Execute(
        string token, List<ProductVariant> viewedPool, List<CartLine> cart, ProductCatalog catalog, Random rng,
        EventSession session, SessionIdentity who, ArrayBufferWriter<byte> buffer, List<SessionEvent> events)
    {
        var timestamp = events.Count == 0 ? session.NextTimestamp : session.AdvanceAndGetTimestamp();
        var eventId = $"evt-{EventSession.HexId(rng)}";

        switch (token)
        {
            case "view":
            {
                var product = catalog.RandomVariant(rng);
                viewedPool.Add(product);
                var rating = Math.Round(3.0 + rng.NextDouble() * 2.0, 2);
                var reviewCount = rng.Next(0, 2000);
                events.Add(new SessionEvent(timestamp, PageViewEvent, Render(buffer, w =>
                {
                    WriteEnvelope(w, PageViewEvent, eventId, timestamp, session, who);
                    w.WriteStartObject("page");
                    w.WriteString("type", "product_detail");
                    w.WriteString("url", product.Url);
                    w.WriteEndObject();
                    w.WritePropertyName("product");
                    WriteProduct(w, product, rating, reviewCount);
                    w.WriteEndObject();
                })));
                break;
            }
            case "add":
            {
                var notInCart = viewedPool
                    .Where(p => cart.All(l => l.Product.Sku != p.Sku))
                    .Select(p => p.Sku)
                    .Distinct()
                    .ToList();

                var addNew = cart.Count == 0 || (notInCart.Count > 0 && rng.NextDouble() >= BumpExistingLineChance);
                if (addNew && notInCart.Count > 0)
                {
                    var sku = notInCart[rng.Next(notInCart.Count)];
                    var product = viewedPool.First(p => p.Sku == sku);
                    cart.Add(new CartLine { Product = product, Quantity = RollQuantity(rng) });
                }
                else
                {
                    cart[rng.Next(cart.Count)].Quantity += 1;
                }

                events.Add(new SessionEvent(timestamp, AddToCartEvent,
                    Render(buffer, w => WriteCartEvent(w, AddToCartEvent, eventId, timestamp, session, who, cart))));
                break;
            }
            case "remove":
            {
                cart.RemoveAt(rng.Next(cart.Count));
                events.Add(new SessionEvent(timestamp, RemoveFromCartEvent,
                    Render(buffer, w => WriteCartEvent(w, RemoveFromCartEvent, eventId, timestamp, session, who, cart))));
                break;
            }
        }
    }

    private static SessionEvent BuildPurchaseEvent(
        List<CartLine> cart, Random rng, EventSession session, SessionIdentity who, ArrayBufferWriter<byte> buffer)
    {
        var timestamp = session.AdvanceAndGetTimestamp();
        var eventId = $"evt-{EventSession.HexId(rng)}";
        var purchaseCount = rng.Next(1, Math.Min(5, cart.Count) + 1);
        var lines = cart.OrderBy(_ => rng.Next()).Take(purchaseCount).ToList();

        var orderId = "ORD-" + new string(Enumerable.Range(0, 7).Select(_ => OrderIdChars[rng.Next(OrderIdChars.Length)]).ToArray());
        var subtotal = lines.Sum(l => l.LineSubtotal);
        var itemCount = lines.Sum(l => l.Quantity);

        var isPickup = rng.NextDouble() < 0.20;
        var hasPromo = !isPickup && rng.NextDouble() < 0.20;
        var discount = hasPromo ? Math.Round(subtotal * (decimal)(0.10 + rng.NextDouble() * 0.10), 2) : 0m;
        var shipping = isPickup || subtotal >= 50m ? 0m : Math.Round((decimal)(5.99 + rng.NextDouble() * 2.00), 2);
        var tax = Math.Round((subtotal - discount) * (decimal)(0.06 + rng.NextDouble() * 0.03), 2);
        var total = Math.Round(subtotal - discount + shipping + tax, 2);
        var fulfillment = isPickup ? "pickup" : "ship";
        var shippingMethod = isPickup ? "store_pickup" : shipping == 0m ? "free_shipping" : "standard";
        var paymentMethod = PaymentMethods[rng.Next(PaymentMethods.Count)];

        return new SessionEvent(timestamp, PurchaseEvent, Render(buffer, w =>
        {
            WriteEnvelope(w, PurchaseEvent, eventId, timestamp, session, who);
            w.WriteStartObject("order");
            w.WriteString("order_id", orderId);
            w.WriteNumber("item_count", itemCount);
            w.WriteNumber("subtotal", subtotal);
            w.WriteNumber("discount", discount);
            w.WriteNumber("shipping", shipping);
            w.WriteNumber("tax", tax);
            w.WriteNumber("total", total);
            w.WriteString("currency", who.Currency);
            w.WriteString("payment_method", paymentMethod);
            w.WriteString("shipping_method", shippingMethod);
            w.WriteString("fulfillment", fulfillment);
            w.WriteStartArray("promo_codes");
            if (hasPromo)
            {
                w.WriteStringValue("WELCOME10");
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartArray("items");
            foreach (var line in lines)
            {
                var p = line.Product;
                w.WriteStartObject();
                w.WriteString("sku", p.Sku);
                w.WriteString("product_id", p.ProductId);
                w.WriteString("name", p.Name);
                w.WriteString("brand", p.Brand);
                w.WriteString("category", p.Category);
                w.WriteString("color", p.Color);
                w.WriteString("size", p.Size);
                w.WriteNumber("quantity", line.Quantity);
                w.WriteNumber("unit_price", p.Price);
                w.WriteNumber("line_total", line.LineSubtotal);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }));
    }

    private static int RollQuantity(Random rng)
    {
        var roll = rng.NextDouble();
        return roll < 0.70 ? 1 : roll < 0.95 ? 2 : 3;
    }

    private static void Shuffle(List<string> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static byte[] Render(ArrayBufferWriter<byte> buffer, Action<Utf8JsonWriter> write)
    {
        buffer.ResetWrittenCount();
        using var writer = new Utf8JsonWriter(buffer, JsonlRender.Options);
        write(writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    // ----------------------------------------------------------------------------------------------
    // Event writers
    // ----------------------------------------------------------------------------------------------

    /// <summary>Opens the root object and writes the fields every event shares. The caller writes the rest and closes the object.</summary>
    private static void WriteEnvelope(
        Utf8JsonWriter w, string eventType, string eventId, DateTimeOffset timestamp, EventSession session, SessionIdentity who)
    {
        w.WriteStartObject();
        w.WriteString("schema_version", SchemaVersion);
        w.WriteString("event_id", eventId);
        w.WriteString("event_type", eventType);
        w.WriteString("event_time", FormatTime(timestamp));
        w.WriteString("session_id", session.SessionId);
        if (who.UserId is null) w.WriteNull("user_id"); else w.WriteString("user_id", who.UserId);
        w.WriteString("email", who.Email);
        w.WriteString("storefront_id", who.StorefrontId);
        w.WriteString("channel", session.Channel);
        w.WriteString("locale", who.Locale);
        w.WriteString("currency", who.Currency);
        w.WriteString("source_system", SourceSystem);
    }

    private static void WriteProduct(Utf8JsonWriter w, ProductVariant p, double rating, int reviewCount)
    {
        w.WriteStartObject();
        w.WriteString("sku", p.Sku);
        w.WriteString("product_id", p.ProductId);
        w.WriteString("name", p.Name);
        w.WriteString("brand", p.Brand);
        w.WriteString("category", p.Category);
        w.WriteString("subcategory", p.Subcategory);
        w.WriteString("color", p.Color);
        w.WriteString("size", p.Size);
        w.WriteNumber("price", p.Price);
        w.WriteNumber("list_price", p.ListPrice);
        w.WriteString("url", p.Url);
        w.WriteString("image_url", p.ImageUrl);
        w.WriteNumber("rating", rating);
        w.WriteNumber("review_count", reviewCount);
        w.WriteEndObject();
    }

    private static void WriteCartEvent(
        Utf8JsonWriter w, string eventType, string eventId, DateTimeOffset timestamp, EventSession session,
        SessionIdentity who, List<CartLine> cart)
    {
        WriteEnvelope(w, eventType, eventId, timestamp, session, who);
        w.WriteStartArray("items");
        foreach (var line in cart)
        {
            var p = line.Product;
            w.WriteStartObject();
            w.WriteString("sku", p.Sku);
            w.WriteString("product_id", p.ProductId);
            w.WriteString("name", p.Name);
            w.WriteString("brand", p.Brand);
            w.WriteString("category", p.Category);
            w.WriteString("color", p.Color);
            w.WriteString("size", p.Size);
            w.WriteNumber("quantity", line.Quantity);
            w.WriteNumber("unit_price", p.Price);
            w.WriteNumber("line_total", line.LineSubtotal);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteNumber("item_count", cart.Sum(l => l.Quantity));
        w.WriteNumber("cart_total", cart.Sum(l => l.LineSubtotal));
        w.WriteEndObject();
    }

    public static string FormatTime(DateTimeOffset timestamp) =>
        timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
}
